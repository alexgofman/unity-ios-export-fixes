using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace IosExportFixes
{
    /// <summary>
    /// Edits the text of the Podfile that External Dependency Manager for Unity (EDM4U) writes into
    /// an iOS export. String in, string out: no Unity types and no file access, so it compiles and
    /// is tested on any platform.
    /// </summary>
    /// <remarks>
    /// Two fixes, each optional.
    ///
    /// Deployment targets. CocoaPods gives every pod target the deployment target of that pod's own
    /// podspec, which is often lower than the app's. Xcode 27.0 stops the build for a target below
    /// the SDK minimum:
    ///   error: The iOS deployment target 'IPHONEOS_DEPLOYMENT_TARGET' is set to 12.0, but the range
    ///   of supported deployment target versions is 15.0 to 27.0.x.
    /// A block in the post_install hook raises those targets to the app minimum. Targets already at
    /// or above it keep their value.
    ///
    /// Dynamic frameworks. EDM4U declares every pod on the UnityFramework target. CocoaPods adds
    /// its "[CP] Embed Pods Frameworks" phase to application and test targets, not to a framework
    /// target, so a pod that ships as a dynamic framework is linked but never copied into the app,
    /// and dyld stops at launch with "Library not loaded: @rpath/...". Declaring the pod on the
    /// application target as well puts it into that phase.
    ///
    /// The patcher only inserts lines. It never rewrites or removes a line that is already there,
    /// apart from ending the last line with a line break when it has none. It reads the Podfile line
    /// by line and does not parse Ruby: it knows # comments, =begin/=end blocks and __END__, and
    /// nothing about heredocs or %-literals.
    /// </remarks>
    public static class PodfilePatcher
    {
        /// <summary>Comment that marks what the patcher inserted.</summary>
        public const string Marker = "# ios-export-fixes";

        private const string EmbedNote = ": dynamic framework, the app target has to embed it";
        private const string RaiseNote = ": build every pod for iOS ";
        private const string SettingName = "IPHONEOS_DEPLOYMENT_TARGET";
        private const string Setting = "config.build_settings['" + SettingName + "']";

        private static readonly Regex VersionNumber =
            new Regex(@"^[0-9]+(\.[0-9]+){0,2}$", RegexOptions.CultureInvariant);

        // First token of the line has to be `pod`, so commented-out declarations do not match.
        private static readonly Regex PodDeclaration =
            new Regex(@"^\s*pod\s*\(?\s*(?<quote>['""])(?<name>[^'""]+)\k<quote>", RegexOptions.CultureInvariant);

        private static readonly Regex PostInstallWord =
            new Regex(@"\bpost_install\b", RegexOptions.CultureInvariant);

        private static readonly Regex DoPostInstall = new Regex(
            @"^(?<indent>\s*)post_install\s+do\s*\|\s*(?<installer>[A-Za-z_][A-Za-z0-9_]*)\s*\|$",
            RegexOptions.CultureInvariant);

        private static readonly Regex OwnBlock = new Regex(
            @"^\s*" + Regex.Escape(Marker + RaiseNote) + @"(?<minimum>\S+)", RegexOptions.CultureInvariant);

        private static readonly Regex BlockEnd = new Regex(@"^\s*end\b", RegexOptions.CultureInvariant);

        private static readonly Regex AnyTarget =
            new Regex(@"^\s*(abstract_)?target\b", RegexOptions.CultureInvariant);

        /// <summary>Applies the fixes selected in <paramref name="options"/> to a Podfile.</summary>
        /// <remarks>
        /// Each fix leaves alone what is already there, so the function can be called again on its
        /// own output, with the same options or with more rules.
        /// </remarks>
        /// <exception cref="ArgumentException">
        /// An option cannot be written into the Podfile safely: a minimum that is not a version
        /// number, a pod name with white space, quotes, commas, backslashes or #, or a target name
        /// with quotes or backslashes.
        /// </exception>
        public static PodfilePatchResult Patch(string podfile, PodfilePatchOptions options)
        {
            if (podfile == null) throw new ArgumentNullException(nameof(podfile));
            if (options == null) throw new ArgumentNullException(nameof(options));

            // These values end up inside Ruby source. Checking them here turns a typo in the settings
            // into a clear message instead of a syntax error in the middle of `pod install`.
            string minimum = ReadMinimum(options.MinimumDeploymentTarget);
            string appTarget = ReadAppTarget(options.AppTargetName);
            List<EmbeddedPodRule> rules = ReadRules(options.EmbeddedPods);

            // EDM4U writes the file with StreamWriter.WriteLine, so an export made on Windows has CRLF
            // line breaks (mixed with a few bare LF). Inserted lines use CRLF as soon as the file does.
            string newline = podfile.IndexOf("\r\n", StringComparison.Ordinal) >= 0 ? "\r\n" : "\n";
            List<string> lines = SplitLines(podfile);
            var warnings = new List<string>();

            List<string> embedded = EmbedDynamicPods(lines, rules, appTarget, newline, warnings);
            PostInstallChange postInstall = RaiseDeploymentTargets(lines, minimum, newline, warnings);

            string patched = string.Concat(lines);
            bool changed = !string.Equals(patched, podfile, StringComparison.Ordinal);
            return new PodfilePatchResult(
                podfile: changed ? patched : podfile,
                changed: changed,
                postInstall: postInstall,
                embeddedPods: embedded,
                warnings: warnings,
                summary: Summarise(postInstall, minimum, embedded));
        }

        // ---- Dynamic frameworks -------------------------------------------------------------

        private static List<string> EmbedDynamicPods(
            List<string> lines, List<EmbeddedPodRule> rules, string appTarget, string newline, List<string> warnings)
        {
            var embedded = new List<string>();
            if (rules.Count == 0) return embedded;

            Scan scan = Read(lines);
            List<string> code = scan.Code;
            List<Declaration> declarations = FindDeclarations(code);
            int blockStart = FindTarget(code, appTarget);
            int blockEnd = blockStart < 0 ? -1 : FindBlockEnd(code, blockStart);
            string indent = blockStart < 0 ? "  " : Indent(code[blockStart]) + "  ";

            // The target may be declared in a way this patcher does not edit: in a loop over target
            // names, with braces, on one line. Appending a block would declare it a second time,
            // which CocoaPods rejects, so in that case nothing is added.
            bool targetOutOfReach = blockStart < 0 && MentionsTarget(code, appTarget);

            var additions = new List<string>();
            var notAdded = new List<string>();
            foreach (EmbeddedPodRule rule in rules)
            {
                if (!IsUsed(rule, declarations)) continue;
                if (IsDeclaredOnTarget(declarations, code, rule.pod, blockStart, blockEnd)) continue;
                if (targetOutOfReach)
                {
                    notAdded.Add(rule.pod);
                    continue;
                }

                // When the Podfile declares the pod itself, repeat that declaration so both targets
                // ask for the same version or path. When the pod only arrives as a dependency of
                // another pod, a bare name is enough: CocoaPods resolves one version of a pod for the
                // whole Podfile, and the dependent pod already constrains it.
                string declaration = ReusableDeclaration(declarations, rule.pod, warnings) ?? "pod '" + rule.pod + "'";
                additions.Add(indent + declaration + " " + Marker + EmbedNote + newline);
                embedded.Add(rule.pod);
            }

            if (notAdded.Count > 0)
            {
                warnings.Add(
                    "The Podfile names the target '" + appTarget + "' but has no plain `target '" + appTarget +
                    "' do` line, so nothing was added to it. Declare these pods on that target by hand: " +
                    string.Join(", ", notAdded) + ".");
            }

            if (additions.Count == 0) return embedded;

            if (blockStart >= 0)
            {
                EndLine(lines, blockStart, newline);
                lines.InsertRange(blockStart + 1, additions);
            }
            else
            {
                // EDM4U leaves the application target out when "always add the main target" is off.
                var block = new List<string> { "target '" + appTarget + "' do" + newline };
                block.AddRange(additions);
                block.Add("end" + newline);
                AppendCode(lines, scan.End, block, newline, false);
            }

            return embedded;
        }

        private static bool MentionsTarget(List<string> code, string name)
        {
            foreach (string line in code)
            {
                string text = Strip(line, true);
                if (text.IndexOf("'" + name + "'", StringComparison.Ordinal) >= 0 ||
                    text.IndexOf("\"" + name + "\"", StringComparison.Ordinal) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsUsed(EmbeddedPodRule rule, List<Declaration> declarations)
        {
            foreach (Declaration declaration in declarations)
            {
                if (IsPodOrSubspec(declaration.Name, rule.pod)) return true;
                foreach (string dependent in rule.requiredBy)
                {
                    if (IsPodOrSubspec(declaration.Name, dependent)) return true;
                }
            }

            return false;
        }

        // A pod declared inside a nested target (a test target inside the application target) belongs
        // to that nested target only, so it does not count as declared on the application target.
        private static bool IsDeclaredOnTarget(
            List<Declaration> declarations, List<string> code, string pod, int start, int end)
        {
            foreach (Declaration declaration in declarations)
            {
                if (declaration.Line <= start || declaration.Line >= end) continue;
                if (!string.Equals(Root(declaration.Name), Root(pod), StringComparison.Ordinal)) continue;
                if (!IsInsideNestedTarget(code, declaration.Line, start)) return true;
            }

            return false;
        }

        private static bool IsInsideNestedTarget(List<string> code, int line, int start)
        {
            for (int i = start + 1; i < line; i++)
            {
                if (!AnyTarget.IsMatch(code[i])) continue;

                int nestedEnd = FindBlockEnd(code, i);
                if (line < nestedEnd) return true;
                i = nestedEnd;
            }

            return false;
        }

        private static string ReusableDeclaration(List<Declaration> declarations, string pod, List<string> warnings)
        {
            foreach (Declaration declaration in declarations)
            {
                if (!string.Equals(declaration.Name, pod, StringComparison.Ordinal)) continue;

                if (!IsWholeStatement(declaration.Text))
                {
                    warnings.Add(
                        "The declaration of pod '" + pod + "' does not fit on one line, so the application target got " +
                        "a bare declaration. Check that it resolves to the same version or path.");
                    return null;
                }

                return Strip(declaration.Text, true).Trim();
            }

            return null;
        }

        // A line can only be repeated if it is a whole statement: brackets balanced outside strings,
        // no second statement after a semicolon, and nothing left hanging at the end (a comma, a
        // backslash, => or another operator).
        private static bool IsWholeStatement(string text)
        {
            string code = Strip(text, false).TrimEnd();
            int depth = 0;
            foreach (char c in code)
            {
                if (c == '(' || c == '[' || c == '{') depth++;
                else if (c == ')' || c == ']' || c == '}') depth--;
                else if (c == ';') return false;

                if (depth < 0) return false;
            }

            if (depth != 0) return false;

            char last = code[code.Length - 1];
            return char.IsLetterOrDigit(last) || last == '_' || last == '\'' || last == '"' ||
                   last == ')' || last == ']' || last == '}';
        }

        private static List<Declaration> FindDeclarations(List<string> code)
        {
            var declarations = new List<Declaration>();
            for (int i = 0; i < code.Count; i++)
            {
                Match match = PodDeclaration.Match(code[i]);
                if (match.Success)
                {
                    declarations.Add(new Declaration(i, match.Groups["name"].Value, code[i]));
                }
            }

            return declarations;
        }

        // Only the usual spelling counts: `target 'Name' do` with nothing after it but a comment.
        private static int FindTarget(List<string> code, string name)
        {
            var target = new Regex(
                @"^\s*target\s*\(?\s*(?<quote>['""])" + Regex.Escape(name) + @"\k<quote>\s*\)?\s*do$",
                RegexOptions.CultureInvariant);

            for (int i = 0; i < code.Count; i++)
            {
                if (target.IsMatch(Strip(code[i], true).TrimEnd())) return i;
            }

            return -1;
        }

        // The block is taken to close at the first `end` indented exactly like its opening line,
        // which holds for what EDM4U writes and for any conventionally indented Podfile.
        private static int FindBlockEnd(List<string> code, int start)
        {
            string indent = Indent(code[start]);
            for (int i = start + 1; i < code.Count; i++)
            {
                if (BlockEnd.IsMatch(code[i]) && string.Equals(Indent(code[i]), indent, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return code.Count;
        }

        // ---- Deployment targets -------------------------------------------------------------

        private static PostInstallChange RaiseDeploymentTargets(
            List<string> lines, string minimum, string newline, List<string> warnings)
        {
            if (minimum == null) return PostInstallChange.None;

            Scan scan = Read(lines);
            List<string> code = scan.Code;

            foreach (string line in code)
            {
                Match own = OwnBlock.Match(line);
                if (!own.Success) continue;

                if (!string.Equals(own.Groups["minimum"].Value, minimum, StringComparison.Ordinal))
                {
                    warnings.Add(
                        "The Podfile already has this package's block for iOS " + own.Groups["minimum"].Value +
                        ". It was left as it is, although the minimum is now " + minimum + ".");
                }

                return PostInstallChange.AlreadyPresent;
            }

            // Any mention of post_install in code counts, not only a line that starts with it:
            // CocoaPods stops with "Specifying multiple `post_install` hooks is unsupported." when a
            // Podfile registers two hooks, so a hook this scanner overlooks must never get a second
            // one next to it.
            var hooks = new List<int>();
            for (int i = 0; i < code.Count; i++)
            {
                if (PostInstallWord.IsMatch(Strip(code[i], false))) hooks.Add(i);
            }

            string note = Marker + RaiseNote + minimum + " or later" + newline;

            if (hooks.Count == 0)
            {
                var hook = new List<string> { note, "post_install do |installer|" + newline };
                hook.AddRange(RaiseBlock("  ", "installer", minimum, newline));
                hook.Add("end" + newline);
                AppendCode(lines, scan.End, hook, newline, true);
                return PostInstallChange.HookAppended;
            }

            // An existing hook is extended, but only in the shape that can be extended safely: one
            // `post_install do |name|` line. Anything else is left alone and reported.
            Match existing = hooks.Count == 1
                ? DoPostInstall.Match(Strip(code[hooks[0]], true).TrimEnd())
                : Match.Empty;
            if (!existing.Success)
            {
                warnings.Add(
                    hooks.Count > 1
                        ? "The Podfile mentions post_install on more than one line. The deployment-target block " +
                          "was not added; add it to the hook by hand."
                        : "The Podfile has a post_install hook that is not written as `post_install do |name|` " +
                          "on a line of its own. The deployment-target block was not added; add it to that hook by hand.");
                return PostInstallChange.SkippedUnsupportedHook;
            }

            if (existing.Groups["indent"].Length > 0)
            {
                warnings.Add(
                    "The post_install hook sits inside another block. The deployment-target block only takes " +
                    "effect when that block runs.");
            }

            for (int i = hooks[0] + 1; i < code.Count; i++)
            {
                if (Strip(code[i], true).IndexOf(SettingName, StringComparison.Ordinal) < 0) continue;

                warnings.Add(
                    "The post_install hook sets " + SettingName + " itself further down. That code runs after " +
                    "the inserted block and has the last word.");
                break;
            }

            // Inserted as the first statements of the hook: that position is valid whatever the rest
            // of the hook contains, and it needs no knowledge of where the hook ends.
            string indent = existing.Groups["indent"].Value + "  ";
            var block = new List<string> { indent + note };
            block.AddRange(RaiseBlock(indent, existing.Groups["installer"].Value, minimum, newline));
            EndLine(lines, hooks[0], newline);
            lines.InsertRange(hooks[0] + 1, block);
            return PostInstallChange.MergedIntoExistingHook;
        }

        // generated_projects rather than pods_project: with the generate_multiple_pod_projects
        // install option the pod targets live in projects of their own, and with
        // incremental_installation on top of that pods_project is nil when nothing had to be rebuilt.
        //
        // Versions are compared with Gem::Version. Comparing them as floats gets "13.10" < "13.4"
        // wrong and ignores a third component. A setting that is missing counts as version 0 and is
        // raised; a setting that is not a version number (a build-setting reference) is left alone.
        // The block declares no local variables, so it cannot clash with the hook it is merged into.
        private static string[] RaiseBlock(string indent, string installer, string minimum, string newline)
        {
            return new[]
            {
                indent + installer + ".generated_projects.each do |project|" + newline,
                indent + "  project.targets.each do |target|" + newline,
                indent + "    target.build_configurations.each do |config|" + newline,
                indent + "      next unless Gem::Version.correct?(" + Setting + ".to_s)" + newline,
                indent + "      next unless Gem::Version.new(" + Setting + ".to_s) < Gem::Version.new('" + minimum + "')" + newline,
                indent + "      " + Setting + " = '" + minimum + "'" + newline,
                indent + "    end" + newline,
                indent + "  end" + newline,
                indent + "end" + newline,
            };
        }

        private static string Summarise(PostInstallChange postInstall, string minimum, List<string> embedded)
        {
            var parts = new List<string>();
            if (postInstall == PostInstallChange.HookAppended)
            {
                parts.Add("Pods below iOS " + minimum + " are raised to it (post_install hook added).");
            }
            else if (postInstall == PostInstallChange.MergedIntoExistingHook)
            {
                parts.Add("Pods below iOS " + minimum + " are raised to it (added to the existing post_install hook).");
            }

            if (embedded.Count > 0)
            {
                parts.Add("Declared on the application target for embedding: " + string.Join(", ", embedded) + ".");
            }

            return string.Join(" ", parts);
        }

        // ---- Options ------------------------------------------------------------------------

        private static string ReadMinimum(string value)
        {
            string minimum = (value ?? "").Trim();
            if (minimum.Length == 0) return null;
            if (!VersionNumber.IsMatch(minimum))
            {
                throw new ArgumentException(
                    "MinimumDeploymentTarget has to be a version number such as 15.0, but is '" + minimum + "'.");
            }

            return minimum;
        }

        private static string ReadAppTarget(string value)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0) return PodfilePatchOptions.DefaultAppTargetName;
            foreach (char c in name)
            {
                if (char.IsControl(c) || c == '\'' || c == '"' || c == '\\')
                {
                    throw new ArgumentException("AppTargetName '" + name + "' is not a usable target name.");
                }
            }

            return name;
        }

        // Entries for the same pod are merged, so two rules that name different dependents both count.
        private static List<EmbeddedPodRule> ReadRules(List<EmbeddedPodRule> source)
        {
            var rules = new List<EmbeddedPodRule>();
            var byPod = new Dictionary<string, EmbeddedPodRule>(StringComparer.Ordinal);
            foreach (EmbeddedPodRule entry in source)
            {
                string pod = entry == null ? "" : (entry.pod ?? "").Trim();
                if (pod.Length == 0) continue;
                RequirePodName(pod);

                EmbeddedPodRule rule;
                if (!byPod.TryGetValue(pod, out rule))
                {
                    rule = new EmbeddedPodRule(pod);
                    byPod.Add(pod, rule);
                    rules.Add(rule);
                }

                if (entry.requiredBy == null) continue;
                foreach (string dependent in entry.requiredBy)
                {
                    string name = (dependent ?? "").Trim();
                    if (name.Length == 0) continue;
                    RequirePodName(name);
                    rule.requiredBy.Add(name);
                }
            }

            return rules;
        }

        private static void RequirePodName(string name)
        {
            foreach (char c in name)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '\'' || c == '"' || c == '\\' || c == '#' || c == ',')
                {
                    throw new ArgumentException("'" + name + "' is not a usable pod name.");
                }
            }
        }

        // ---- Text helpers -------------------------------------------------------------------

        // What the scanners look at: every line without its line break, and nothing at all for a
        // line that is not code (inside a =begin/=end block comment, or from __END__ on), so that
        // commented-out targets, pods and hooks are not mistaken for live ones. Indexes match the
        // list of real lines.
        private sealed class Scan
        {
            public readonly List<string> Code = new List<string>();

            /// <summary>Where appended code has to go: the __END__ line if there is one, else the end.</summary>
            public int End;
        }

        private static Scan Read(List<string> lines)
        {
            var scan = new Scan { End = lines.Count };
            bool inBlockComment = false;
            bool afterEnd = false;
            for (int i = 0; i < lines.Count; i++)
            {
                string text = Content(lines[i]);
                if (!afterEnd && !inBlockComment)
                {
                    if (text == "__END__")
                    {
                        afterEnd = true;
                        scan.End = i;
                    }
                    else if (text.StartsWith("=begin", StringComparison.Ordinal))
                    {
                        inBlockComment = true;
                    }
                }

                scan.Code.Add(afterEnd || inBlockComment ? "" : text);
                if (inBlockComment && text.StartsWith("=end", StringComparison.Ordinal)) inBlockComment = false;
            }

            return scan;
        }

        // Puts a block after the last line of code, with an empty line before it when asked for and
        // not already there.
        private static void AppendCode(List<string> lines, int end, List<string> block, string newline, bool separate)
        {
            if (end > 0) EndLine(lines, end - 1, newline);

            if (separate && end > 0 && Content(lines[end - 1]).Trim().Length > 0)
            {
                lines.Insert(end, newline);
                end++;
            }

            lines.InsertRange(end, block);
        }

        // Only the last line of a file can lack a line break; it needs one before anything follows it.
        private static void EndLine(List<string> lines, int index, string newline)
        {
            if (!lines[index].EndsWith("\n", StringComparison.Ordinal))
            {
                lines[index] += newline;
            }
        }

        // Each element keeps its own line break, so joining the list gives back the input exactly.
        private static List<string> SplitLines(string text)
        {
            var lines = new List<string>();
            int start = 0;
            while (start < text.Length)
            {
                int lineFeed = text.IndexOf('\n', start);
                int end = lineFeed < 0 ? text.Length : lineFeed + 1;
                lines.Add(text.Substring(start, end - start));
                start = end;
            }

            return lines;
        }

        private static string Content(string line)
        {
            return line.TrimEnd('\r', '\n');
        }

        private static string Indent(string line)
        {
            int length = 0;
            while (length < line.Length && (line[length] == ' ' || line[length] == '\t')) length++;
            return line.Substring(0, length);
        }

        private static string Root(string pod)
        {
            int slash = pod.IndexOf('/');
            return slash < 0 ? pod : pod.Substring(0, slash);
        }

        private static bool IsPodOrSubspec(string declared, string pod)
        {
            return string.Equals(declared, pod, StringComparison.Ordinal) ||
                   declared.StartsWith(pod + "/", StringComparison.Ordinal);
        }

        // The line without its trailing comment. With keepStrings false the text inside quotes goes
        // as well, so that a word in a string is not taken for code.
        private static string Strip(string text, bool keepStrings)
        {
            var result = new StringBuilder(text.Length);
            char quote = '\0';
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quote == '\0')
                {
                    if (c == '#') break;
                    if (c == '\'' || c == '"') quote = c;
                    result.Append(c);
                }
                else if (c == quote)
                {
                    quote = '\0';
                    result.Append(c);
                }
                else
                {
                    if (keepStrings) result.Append(c);
                    if (c == '\\' && i + 1 < text.Length)
                    {
                        i++;
                        if (keepStrings) result.Append(text[i]);
                    }
                }
            }

            return result.ToString();
        }

        private readonly struct Declaration
        {
            public Declaration(int line, string name, string text)
            {
                Line = line;
                Name = name;
                Text = text;
            }

            public int Line { get; }

            public string Name { get; }

            public string Text { get; }
        }
    }
}
