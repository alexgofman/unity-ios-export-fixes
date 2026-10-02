using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using NUnit.Framework;

namespace IosExportFixes.Tests
{
    /// <summary>
    /// Runs patched Podfiles through a real Ruby interpreter, with podfile_harness.rb standing in for
    /// CocoaPods. The string comparisons in PodfilePatcherTests show what text is inserted; these
    /// show that Ruby accepts it and that the hook does what it claims.
    /// </summary>
    /// <remarks>
    /// Kept out of the Unity package because the tests start a process. Without a <c>ruby</c> on the
    /// PATH they are reported as skipped, unless REQUIRE_RUBY is set, as it is in CI.
    /// </remarks>
    public class RubyChecks
    {
        [TestCaseSource(typeof(PodfileFixtures), nameof(PodfileFixtures.Names))]
        public void PatchedFixture_IsAcceptedByRuby(string fixture)
        {
            PodfilePatchResult patch = PodfilePatcher.Patch(PodfileFixtures.Get(fixture), PodfileFixtures.Options());

            HarnessOutput output = RunHarness(patch.Podfile, "9.0", "15.0", "16.4");

            Assert.That(output.HasHook, Is.True);
            bool hookRan = patch.PostInstall == PostInstallChange.HookAppended ||
                           patch.PostInstall == PostInstallChange.MergedIntoExistingHook;
            Assert.That(output.DeploymentTargets, Is.EqualTo(hookRan
                ? new[] { "15.0", "15.0", "16.4" }
                : new[] { "9.0", "15.0", "16.4" }));
        }

        [TestCaseSource(typeof(PodfileFixtures), nameof(PodfileFixtures.Names))]
        public void UnpatchedFixture_IsAcceptedByTheHarness(string fixture)
        {
            // Guards the harness itself: a fixture it cannot read would make the test above meaningless.
            HarnessOutput output = RunHarness(PodfileFixtures.Get(fixture), "9.0");

            Assert.That(output.DeploymentTargets, Is.EqualTo(new[] { "9.0" }));
        }

        [Test]
        public void Hook_RaisesTargetsBelowTheMinimum_AndLeavesTheOthers()
        {
            PodfilePatchResult patch = PodfilePatcher.Patch(PodfileFixtures.Standard, PodfileFixtures.Options("15.0"));

            HarnessOutput output = RunHarness(
                patch.Podfile, "9.0", "12.4", "14.8", "15.0", "15.1", "16.0", "26.2", "-", "$(inherited)");

            Assert.That(output.DeploymentTargets, Is.EqualTo(new[]
            {
                "15.0", "15.0", "15.0", "15.0", "15.1", "16.0", "26.2",
                "15.0", // no setting at all: the target would inherit the project-wide value, so it is set
                "$(inherited)", // not a version number: left alone
            }));
        }

        [Test]
        public void Hook_ComparesVersions_NotFloats()
        {
            PodfilePatchResult patch = PodfilePatcher.Patch(PodfileFixtures.Standard, PodfileFixtures.Options("13.4"));

            HarnessOutput output = RunHarness(patch.Podfile, "13.10", "13.4.1", "13.3.9", "13");

            // As floats, 13.10 reads as 13.1 and would be "raised" down to 13.4.
            Assert.That(output.DeploymentTargets, Is.EqualTo(new[] { "13.10", "13.4.1", "13.4", "13.4" }));
        }

        [Test]
        public void Hook_ReachesPodTargetsThatHaveProjectsOfTheirOwn()
        {
            PodfilePatchResult patch = PodfilePatcher.Patch(PodfileFixtures.Standard, PodfileFixtures.Options("15.0"));

            HarnessOutput output = RunHarness(patch.Podfile, "--multi", "9.0", "16.0", "-");

            Assert.That(output.DeploymentTargets, Is.EqualTo(new[] { "15.0", "16.0", "15.0" }));
        }

        [Test]
        public void Hook_DoesNotFail_WhenAnIncrementalInstallWroteNoProject()
        {
            PodfilePatchResult patch = PodfilePatcher.Patch(PodfileFixtures.Standard, PodfileFixtures.Options("15.0"));

            // RunHarness fails the test if Ruby exits with an error.
            HarnessOutput output = RunHarness(patch.Podfile, "--incremental", "9.0");

            Assert.That(output.HasHook, Is.True);
            Assert.That(output.DeploymentTargets, Is.EqualTo(new[] { "9.0" }));
        }

        [Test]
        public void MergedHook_KeepsTheOriginalBodyWorking()
        {
            PodfilePatchResult patch = PodfilePatcher.Patch(PodfileFixtures.WithExistingHook, PodfileFixtures.Options());

            HarnessOutput output = RunHarness(patch.Podfile, "11.0");

            Assert.That(output.TargetSettings[0], Is.EquivalentTo(new[]
            {
                "CODE_SIGNING_ALLOWED=NO",
                "IPHONEOS_DEPLOYMENT_TARGET=15.0",
            }));
        }

        [TestCase(nameof(PodfileFixtures.Standard))]
        [TestCase(nameof(PodfileFixtures.NoAppTarget))]
        [TestCase(nameof(PodfileFixtures.HandEdited))]
        [TestCase(nameof(PodfileFixtures.WindowsExport))]
        public void EmbedLine_IsADeclarationOnTheAppTarget(string fixture)
        {
            PodfilePatchResult patch = PodfilePatcher.Patch(PodfileFixtures.Get(fixture), PodfileFixtures.Options());

            HarnessOutput output = RunHarness(patch.Podfile);

            Assert.That(output.Pods, Does.Contain("Unity-iPhone|FBAudienceNetwork"));
            Assert.That(output.Pods, Does.Not.Contain("UnityFramework|FBAudienceNetwork"));
            Assert.That(output.Pods, Does.Contain("UnityFramework|IronSourceFacebookAdapter"));
        }

        // ---- Harness ---------------------------------------------------------------------------

        private sealed class HarnessOutput
        {
            public bool HasHook;
            public readonly List<string> Pods = new List<string>();
            public readonly List<string[]> TargetSettings = new List<string[]>();

            public string[] DeploymentTargets
            {
                get
                {
                    var values = new string[TargetSettings.Count];
                    for (int i = 0; i < values.Length; i++)
                    {
                        values[i] = "-";
                        foreach (string setting in TargetSettings[i])
                        {
                            const string key = "IPHONEOS_DEPLOYMENT_TARGET=";
                            if (setting.StartsWith(key, StringComparison.Ordinal)) values[i] = setting.Substring(key.Length);
                        }
                    }

                    return values;
                }
            }
        }

        private static HarnessOutput RunHarness(string podfile, params string[] deploymentTargets)
        {
            string harness = Path.Combine(TestContext.CurrentContext.TestDirectory, "podfile_harness.rb");
            string path = Path.Combine(Path.GetTempPath(), "Podfile-" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(path, podfile);
            try
            {
                var start = new ProcessStartInfo("ruby")
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                start.ArgumentList.Add(harness);
                start.ArgumentList.Add(path);
                foreach (string target in deploymentTargets) start.ArgumentList.Add(target);

                Process process;
                try
                {
                    process = Process.Start(start);
                }
                catch (Win32Exception)
                {
                    if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REQUIRE_RUBY")))
                    {
                        Assert.Fail("REQUIRE_RUBY is set but no `ruby` was found on the PATH.");
                    }

                    Assert.Ignore("No `ruby` on the PATH; Ruby checks skipped.");
                    return null;
                }

                using (process)
                {
                    string standardOutput = process.StandardOutput.ReadToEnd();
                    string standardError = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    Assert.That(process.ExitCode, Is.EqualTo(0), "Ruby rejected the Podfile:\n" + standardError + "\n" + podfile);
                    return Parse(standardOutput);
                }
            }
            finally
            {
                File.Delete(path);
            }
        }

        private static HarnessOutput Parse(string standardOutput)
        {
            var output = new HarnessOutput();
            foreach (string line in standardOutput.Split('\n'))
            {
                string[] fields = line.TrimEnd('\r').Split('|');
                if (fields[0] == "pod" && fields.Length == 3)
                {
                    output.Pods.Add(fields[1] + "|" + fields[2]);
                }
                else if (fields[0] == "hook" && fields.Length == 2)
                {
                    output.HasHook = fields[1] == "yes";
                }
                else if (fields[0] == "target" && fields.Length >= 2)
                {
                    var settings = new string[fields.Length - 2];
                    Array.Copy(fields, 2, settings, 0, settings.Length);
                    output.TargetSettings.Add(settings);
                }
            }

            return output;
        }
    }
}
