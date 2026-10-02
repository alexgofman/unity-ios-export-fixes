using System.Collections.Generic;

namespace IosExportFixes.Tests
{
    /// <summary>
    /// Podfiles in the shape External Dependency Manager for Unity writes them (GenPodfile in its
    /// IOSResolver.cs): source lines, the platform line, a block for the UnityFramework target, an
    /// empty block for the application target and the use_frameworks! line.
    /// </summary>
    /// <remarks>
    /// Built from line arrays so that every line break is explicit and does not depend on how this
    /// file was checked out. Pod names are real public pods; the versions are placeholders.
    /// </remarks>
    internal static class PodfileFixtures
    {
        public const string DynamicPod = "FBAudienceNetwork";
        public const string AdapterPod = "IronSourceFacebookAdapter";

        /// <summary>What a default export looks like: the adapter is there, the dynamic pod is not named.</summary>
        public static readonly string Standard = Lf(
            "source 'https://cdn.cocoapods.org/'",
            "",
            "platform :ios, '15.0'",
            "",
            "target 'UnityFramework' do",
            "  pod 'Firebase/Analytics', '11.4.0'",
            "  pod 'IronSourceSDK', '8.4.0'",
            "  pod 'IronSourceFacebookAdapter', '4.3.47'",
            "end",
            "target 'Unity-iPhone' do",
            "end",
            "use_frameworks! :linkage => :static");

        /// <summary>The same export with a hook another tool added, using its own block variable.</summary>
        public static readonly string WithExistingHook = Standard + Lf(
            "",
            "post_install do |pi|",
            "  pi.pods_project.targets.each do |t|",
            "    t.build_configurations.each do |c|",
            "      c.build_settings['CODE_SIGNING_ALLOWED'] = 'NO'",
            "    end",
            "  end",
            "end");

        /// <summary>EDM4U with "always add the main target to the Podfile" switched off.</summary>
        public static readonly string NoAppTarget = Lf(
            "source 'https://cdn.cocoapods.org/'",
            "",
            "platform :ios, '15.0'",
            "",
            "target 'UnityFramework' do",
            "  pod 'Firebase/Analytics', '11.4.0'",
            "  pod 'IronSourceFacebookAdapter', '4.3.47'",
            "end",
            "use_frameworks! :linkage => :static");

        /// <summary>A project that uses neither the dynamic pod nor the adapter that depends on it.</summary>
        public static readonly string NoDynamicPod = Lf(
            "source 'https://cdn.cocoapods.org/'",
            "",
            "platform :ios, '15.0'",
            "",
            "target 'UnityFramework' do",
            "  pod 'Firebase/Analytics', '11.4.0'",
            "  pod 'IronSourceSDK', '8.4.0'",
            "end",
            "target 'Unity-iPhone' do",
            "end",
            "use_frameworks! :linkage => :static");

        /// <summary>
        /// An export made by the Windows editor. EDM4U ends lines with StreamWriter.WriteLine, and two
        /// of its strings carry a "\n" of their own, so the file is CRLF with two bare LF in it.
        /// </summary>
        public static readonly string WindowsExport =
            "source 'https://cdn.cocoapods.org/'\n\r\n" +
            "platform :ios, '15.0'\n\r\n" +
            "target 'UnityFramework' do\r\n" +
            "  pod 'Firebase/Analytics', '11.4.0'\r\n" +
            "  pod 'IronSourceFacebookAdapter', '4.3.47'\r\n" +
            "end\r\n" +
            "target 'Unity-iPhone' do\r\n" +
            "end\r\n" +
            "use_frameworks! :linkage => :static\r\n";

        /// <summary>A Windows export that already has a hook.</summary>
        public static readonly string WindowsExportWithHook =
            WindowsExport +
            "\r\n" +
            "post_install do |pi|\r\n" +
            "  puts pi.pods_project.targets.size\r\n" +
            "end\r\n";

        /// <summary>A hook written with braces, which the patcher does not edit.</summary>
        public static readonly string BraceHook = Standard + Lf(
            "",
            "post_install { |installer| puts installer.pods_project.targets.size }");

        /// <summary>Only a commented-out hook: for CocoaPods this Podfile has no hook at all.</summary>
        public static readonly string CommentedOutHook = Standard + Lf(
            "",
            "# post_install do |installer|",
            "#   puts 'disabled'",
            "# end");

        /// <summary>The application target already declares the dynamic pod.</summary>
        public static readonly string AppTargetDeclaresPod = Lf(
            "platform :ios, '15.0'",
            "",
            "target 'UnityFramework' do",
            "  pod 'IronSourceFacebookAdapter', '4.3.47'",
            "end",
            "target 'Unity-iPhone' do",
            "  pod 'FBAudienceNetwork', '6.20.1'",
            "end",
            "use_frameworks! :linkage => :static");

        /// <summary>
        /// What EDM4U writes for a pod marked addToAllTargets when "allow the same pod in multiple
        /// targets" is off: the declaration is there, but commented out.
        /// </summary>
        public static readonly string AppTargetPodCommentedOut = Lf(
            "platform :ios, '15.0'",
            "",
            "target 'UnityFramework' do",
            "  pod 'FBAudienceNetwork', '6.20.1'",
            "end",
            "target 'Unity-iPhone' do",
            "  # pod 'FBAudienceNetwork', '6.20.1'",
            "  # Commented due to iOS Resolver settings.",
            "end",
            "use_frameworks! :linkage => :static");

        /// <summary>The dynamic pod is declared directly, with a version and a trailing comment.</summary>
        public static readonly string ExplicitDeclaration = Lf(
            "platform :ios, '15.0'",
            "",
            "target 'UnityFramework' do",
            "  pod 'FBAudienceNetwork', '6.20.1' # pinned by the project",
            "end",
            "target 'Unity-iPhone' do",
            "end",
            "use_frameworks! :linkage => :static");

        /// <summary>A hand-edited Podfile: double quotes, deeper indentation, a nested target.</summary>
        public static readonly string HandEdited = Lf(
            "platform :ios, '15.0'",
            "use_frameworks! :linkage => :static",
            "",
            "abstract_target 'Shared' do",
            "    target \"UnityFramework\" do",
            "        pod \"IronSourceFacebookAdapter\", \"4.3.47\"",
            "    end",
            "    target \"Unity-iPhone\" do",
            "        target 'Unity-iPhone Tests' do",
            "            inherit! :search_paths",
            "        end",
            "    end",
            "end");

        /// <summary>
        /// An old setup kept in a =begin/=end block comment above the live one. Nothing inside the
        /// comment is a target, a pod or a hook.
        /// </summary>
        public static readonly string BlockComment = Lf(
            "=begin",
            "Previous setup, kept for reference:",
            "target 'Unity-iPhone' do",
            "  pod 'FBAudienceNetwork'",
            "end",
            "post_install do |installer|",
            "  puts 'old hook'",
            "end",
            "=end",
            "platform :ios, '15.0'",
            "",
            "target 'UnityFramework' do",
            "  pod 'IronSourceFacebookAdapter', '4.3.47'",
            "end",
            "target 'Unity-iPhone' do",
            "end",
            "use_frameworks! :linkage => :static");

        /// <summary>Every fixture by name, for tests that check a property on all of them.</summary>
        private static readonly Dictionary<string, string> ByName = new Dictionary<string, string>
        {
            { nameof(Standard), Standard },
            { nameof(WithExistingHook), WithExistingHook },
            { nameof(NoAppTarget), NoAppTarget },
            { nameof(NoDynamicPod), NoDynamicPod },
            { nameof(WindowsExport), WindowsExport },
            { nameof(WindowsExportWithHook), WindowsExportWithHook },
            { nameof(BraceHook), BraceHook },
            { nameof(CommentedOutHook), CommentedOutHook },
            { nameof(AppTargetDeclaresPod), AppTargetDeclaresPod },
            { nameof(AppTargetPodCommentedOut), AppTargetPodCommentedOut },
            { nameof(ExplicitDeclaration), ExplicitDeclaration },
            { nameof(HandEdited), HandEdited },
            { nameof(BlockComment), BlockComment },
        };

        /// <summary>Names of all fixtures; test cases are named after them.</summary>
        public static IEnumerable<string> Names
        {
            get { return ByName.Keys; }
        }

        public static string Get(string name)
        {
            return ByName[name];
        }

        /// <summary>Joins lines with LF and ends the last one.</summary>
        public static string Lf(params string[] lines)
        {
            return string.Join("\n", lines) + "\n";
        }

        /// <summary>Minimum 15.0 plus the rule "embed the dynamic pod when the adapter is present".</summary>
        public static PodfilePatchOptions Options(string minimum = "15.0")
        {
            var options = new PodfilePatchOptions { MinimumDeploymentTarget = minimum };
            options.EmbeddedPods.Add(new EmbeddedPodRule(DynamicPod, AdapterPod));
            return options;
        }
    }
}
