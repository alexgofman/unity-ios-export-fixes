using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace IosExportFixes.Tests
{
    public class PodfilePatcherTests
    {
        private const string EmbedNote = " # ios-export-fixes: dynamic framework, the app target has to embed it";
        private const string EmbedLine = "pod 'FBAudienceNetwork'" + EmbedNote;
        private const string RaiseNote = "# ios-export-fixes: build every pod for iOS 15.0 or later";
        private const string Setting = "config.build_settings['IPHONEOS_DEPLOYMENT_TARGET']";

        // ---- The cases the hook meets in a real export ---------------------------------------

        [Test]
        public void StandardExport_GetsEmbedLineAndNewHook()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.Standard, PodfileFixtures.Options());

            string expected = PodfileFixtures.Lf(
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
                "  pod 'FBAudienceNetwork' # ios-export-fixes: dynamic framework, the app target has to embed it",
                "end",
                "use_frameworks! :linkage => :static",
                "",
                "# ios-export-fixes: build every pod for iOS 15.0 or later",
                "post_install do |installer|",
                "  installer.generated_projects.each do |project|",
                "    project.targets.each do |target|",
                "      target.build_configurations.each do |config|",
                "        next unless Gem::Version.correct?(config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'].to_s)",
                "        next unless Gem::Version.new(config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'].to_s) < Gem::Version.new('15.0')",
                "        config.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '15.0'",
                "      end",
                "    end",
                "  end",
                "end");

            Assert.That(result.Podfile, Is.EqualTo(expected));
            Assert.That(result.Changed, Is.True);
            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.HookAppended));
            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(result.Summary, Is.EqualTo(
                "Pods below iOS 15.0 are raised to it (post_install hook added). " +
                "Declared on the application target for embedding: FBAudienceNetwork."));
        }

        [Test]
        public void ExistingHook_IsExtended_WithItsOwnBlockVariable()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.WithExistingHook, PodfileFixtures.Options());

            var expectedHook = new List<string> { "post_install do |pi|", "  " + RaiseNote };
            expectedHook.AddRange(RaiseBlock("  ", "pi", "15.0"));
            expectedHook.AddRange(new[]
            {
                "  pi.pods_project.targets.each do |t|",
                "    t.build_configurations.each do |c|",
                "      c.build_settings['CODE_SIGNING_ALLOWED'] = 'NO'",
                "    end",
                "  end",
                "end",
            });

            Assert.That(result.Podfile, Does.EndWith(PodfileFixtures.Lf(expectedHook.ToArray())));
            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.MergedIntoExistingHook));
            // CocoaPods rejects a Podfile with two hooks.
            Assert.That(Count(result.Podfile, "post_install"), Is.EqualTo(1));
            Assert.That(result.Podfile, Does.Not.Contain("installer."));
            Assert.That(result.Warnings, Is.Empty);
            Assert.That(result.Summary, Does.StartWith(
                "Pods below iOS 15.0 are raised to it (added to the existing post_install hook)."));
        }

        [Test]
        public void MissingAppTarget_IsAdded_WithTheDynamicPod()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.NoAppTarget, PodfileFixtures.Options());

            Assert.That(result.Podfile, Does.Contain(PodfileFixtures.Lf(
                "use_frameworks! :linkage => :static",
                "target 'Unity-iPhone' do",
                "  " + EmbedLine,
                "end",
                "",
                RaiseNote,
                "post_install do |installer|")));
            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
            Assert.That(Count(result.Podfile, "target 'Unity-iPhone' do"), Is.EqualTo(1));
        }

        [Test]
        public void DynamicPodNotInUse_NothingIsEmbedded()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.NoDynamicPod, PodfileFixtures.Options());

            Assert.That(result.EmbeddedPods, Is.Empty);
            Assert.That(result.Podfile, Does.Not.Contain("FBAudienceNetwork"));
            Assert.That(result.Podfile, Does.Contain("target 'Unity-iPhone' do\nend\n"));
            // The deployment-target fix does not depend on the embed rule.
            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.HookAppended));
        }

        [Test]
        public void WindowsExport_InsertedLinesUseCrLf()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.WindowsExport, PodfileFixtures.Options());

            string expected =
                "source 'https://cdn.cocoapods.org/'\n\r\n" +
                "platform :ios, '15.0'\n\r\n" +
                "target 'UnityFramework' do\r\n" +
                "  pod 'Firebase/Analytics', '11.4.0'\r\n" +
                "  pod 'IronSourceFacebookAdapter', '4.3.47'\r\n" +
                "end\r\n" +
                "target 'Unity-iPhone' do\r\n" +
                "  " + EmbedLine + "\r\n" +
                "end\r\n" +
                "use_frameworks! :linkage => :static\r\n" +
                "\r\n" +
                RaiseNote + "\r\n" +
                "post_install do |installer|\r\n" +
                string.Join("\r\n", RaiseBlock("  ", "installer", "15.0")) + "\r\n" +
                "end\r\n";

            Assert.That(result.Podfile, Is.EqualTo(expected));
        }

        [Test]
        public void WindowsExport_ExistingHookIsFoundAndExtended()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.WindowsExportWithHook, PodfileFixtures.Options());

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.MergedIntoExistingHook));
            Assert.That(result.Podfile, Does.Contain(
                "post_install do |pi|\r\n" +
                "  " + RaiseNote + "\r\n" +
                "  pi.generated_projects.each do |project|\r\n"));
            Assert.That(Count(result.Podfile, "post_install"), Is.EqualTo(1));
            // Only the two bare LF that EDM4U itself wrote may remain.
            Assert.That(Regex.Matches(result.Podfile, "(?<!\r)\n").Count, Is.EqualTo(2));
        }

        // ---- Hooks ----------------------------------------------------------------------------

        [TestCase("post_install do |installer|")]
        [TestCase("post_install do |installer| # project hook")]
        [TestCase("post_install do | installer |")]
        [TestCase("post_install do|installer|")]
        public void HookInTheUsualShape_IsExtended(string hookLine)
        {
            string podfile = PodfileFixtures.NoDynamicPod + "\n" + hookLine + "\nend\n";

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, MinimumOnly("15.0"));

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.MergedIntoExistingHook));
            Assert.That(result.Podfile, Does.Contain(
                hookLine + "\n" +
                "  " + RaiseNote + "\n" +
                "  installer.generated_projects.each do |project|\n"));
            Assert.That(result.Warnings, Is.Empty);
        }

        [TestCase("post_install do\n  puts 'done'\nend")]
        [TestCase("post_install { |installer| puts installer }")]
        [TestCase("post_install do |installer| puts installer end")]
        [TestCase("post_install(&finish)")]
        [TestCase("self.post_install do |installer|\nend")]
        [TestCase("puts 'x'; post_install do |installer|\nend")]
        public void HookInAnotherShape_IsNotTouched_AndNoSecondHookIsAdded(string hook)
        {
            string podfile = PodfileFixtures.NoDynamicPod + "\n" + hook + "\n";

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, MinimumOnly("15.0"));

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.SkippedUnsupportedHook));
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Podfile, Is.SameAs(podfile));
            Assert.That(result.Warnings.Count, Is.EqualTo(1));
            Assert.That(result.Summary, Is.Empty);
        }

        [Test]
        public void BraceHook_IsLeftAlone_ButTheEmbedRuleStillApplies()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.BraceHook, PodfileFixtures.Options());

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.SkippedUnsupportedHook));
            Assert.That(Count(result.Podfile, "post_install"), Is.EqualTo(1));
            Assert.That(result.Podfile, Does.Not.Contain("IPHONEOS_DEPLOYMENT_TARGET"));
            Assert.That(result.Warnings.Count, Is.EqualTo(1));
            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
        }

        [Test]
        public void TwoHookLines_AreReported_AndNothingIsAdded()
        {
            string podfile = PodfileFixtures.WithExistingHook + PodfileFixtures.Lf("post_install do |other|", "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, MinimumOnly("15.0"));

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.SkippedUnsupportedHook));
            Assert.That(result.Changed, Is.False);
            Assert.That(result.Warnings.Count, Is.EqualTo(1));
        }

        [Test]
        public void CommentedOutHook_DoesNotCountAsAHook()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.CommentedOutHook, PodfileFixtures.Options());

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.HookAppended));
            Assert.That(result.Podfile, Does.Contain("# post_install do |installer|\n#   puts 'disabled'\n# end\n"));
            Assert.That(result.Podfile, Does.EndWith(
                "\n" + RaiseNote + "\n" +
                "post_install do |installer|\n" +
                string.Join("\n", RaiseBlock("  ", "installer", "15.0")) + "\n" +
                "end\n"));
        }

        [Test]
        public void WordInAStringOrComment_DoesNotCountAsAHook()
        {
            string podfile = PodfileFixtures.NoDynamicPod + PodfileFixtures.Lf(
                "puts 'no post_install hook in this file' # post_install comes from a tool");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, MinimumOnly("15.0"));

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.HookAppended));
        }

        [Test]
        public void BlockComment_IsNotScanned()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.BlockComment, PodfileFixtures.Options());

            // The commented-out hook is not extended and the commented-out target gets no pod line.
            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.HookAppended));
            Assert.That(result.Podfile, Does.StartWith(PodfileFixtures.Lf(
                "=begin",
                "Previous setup, kept for reference:",
                "target 'Unity-iPhone' do",
                "  pod 'FBAudienceNetwork'",
                "end",
                "post_install do |installer|",
                "  puts 'old hook'",
                "end",
                "=end")));
            Assert.That(result.Podfile, Does.Contain(PodfileFixtures.Lf(
                "target 'Unity-iPhone' do",
                "  " + EmbedLine,
                "end",
                "use_frameworks! :linkage => :static",
                "",
                RaiseNote,
                "post_install do |installer|",
                "  installer.generated_projects.each do |project|")));
        }

        [Test]
        public void EndMarker_NewCodeGoesBeforeIt()
        {
            string podfile = PodfileFixtures.NoAppTarget + PodfileFixtures.Lf(
                "__END__",
                "post_install do |installer|",
                "target 'Unity-iPhone' do");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options());

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.HookAppended));
            Assert.That(result.Podfile, Does.Contain(PodfileFixtures.Lf(
                "use_frameworks! :linkage => :static",
                "target 'Unity-iPhone' do",
                "  " + EmbedLine,
                "end",
                "",
                RaiseNote,
                "post_install do |installer|")));
            Assert.That(result.Podfile, Does.EndWith(PodfileFixtures.Lf(
                "  end",
                "end",
                "__END__",
                "post_install do |installer|",
                "target 'Unity-iPhone' do")));
        }

        [Test]
        public void IndentedHook_IsExtended_WithAWarningThatItMayNotRun()
        {
            string podfile = "if ENV['CI']\n\tpost_install do |installer|\n\tend\nend\n";

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, MinimumOnly("15.0"));

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.MergedIntoExistingHook));
            Assert.That(result.Podfile, Does.StartWith(
                "if ENV['CI']\n" +
                "\tpost_install do |installer|\n" +
                "\t  " + RaiseNote + "\n" +
                "\t  installer.generated_projects.each do |project|\n"));
            Assert.That(result.Warnings.Count, Is.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("inside another block"));
        }

        [Test]
        public void HookThatSetsTheDeploymentTargetItself_GetsAWarning()
        {
            string podfile = PodfileFixtures.Lf(
                "post_install do |installer|",
                "  installer.pods_project.targets.each do |t|",
                "    t.build_configurations.each do |c|",
                "      c.build_settings['IPHONEOS_DEPLOYMENT_TARGET'] = '12.0'",
                "    end",
                "  end",
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, MinimumOnly("15.0"));

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.MergedIntoExistingHook));
            Assert.That(result.Warnings.Count, Is.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("IPHONEOS_DEPLOYMENT_TARGET"));
        }

        // ---- Embedding -----------------------------------------------------------------------

        [Test]
        public void PodAlreadyOnAppTarget_IsNotDeclaredAgain()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.AppTargetDeclaresPod, PodfileFixtures.Options());

            Assert.That(result.EmbeddedPods, Is.Empty);
            Assert.That(Count(result.Podfile, "FBAudienceNetwork"), Is.EqualTo(1));
        }

        [Test]
        public void SubspecOnTheAppTarget_CountsAsThePodBeingThere()
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'IronSourceFacebookAdapter', '4.3.47'",
                "end",
                "target 'Unity-iPhone' do",
                "  pod 'FBAudienceNetwork/Core'",
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Changed, Is.False);
            Assert.That(result.EmbeddedPods, Is.Empty);
        }

        [Test]
        public void PodDeclaredAfterANestedTarget_StillCountsAsOnTheAppTarget()
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'IronSourceFacebookAdapter', '4.3.47'",
                "end",
                "target 'Unity-iPhone' do",
                "  target 'Unity-iPhone Tests' do",
                "    inherit! :search_paths",
                "  end",
                "  pod 'FBAudienceNetwork'",
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Changed, Is.False);
            Assert.That(result.EmbeddedPods, Is.Empty);
        }

        [Test]
        public void PodDeclaredOnlyOnANestedTarget_IsStillAddedToTheAppTarget()
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'IronSourceFacebookAdapter', '4.3.47'",
                "end",
                "target 'Unity-iPhone' do",
                "  target 'Unity-iPhone Tests' do",
                "    pod 'FBAudienceNetwork'",
                "  end",
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
            Assert.That(result.Podfile, Does.Contain(PodfileFixtures.Lf(
                "target 'Unity-iPhone' do",
                "  " + EmbedLine,
                "  target 'Unity-iPhone Tests' do")));
        }

        [Test]
        public void TargetBlockWithoutAnEnd_RunsToTheEndOfTheFile()
        {
            // Not valid Ruby, but the patcher must neither throw nor make it worse.
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'IronSourceFacebookAdapter'",
                "end",
                "target 'Unity-iPhone' do",
                "  pod 'FBAudienceNetwork'");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Changed, Is.False);
        }

        [Test]
        public void CommentedOutDeclaration_DoesNotCount_AndTheRealOneIsRepeated()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.AppTargetPodCommentedOut, PodfileFixtures.Options());

            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
            Assert.That(result.Podfile, Does.Contain(PodfileFixtures.Lf(
                "target 'Unity-iPhone' do",
                "  pod 'FBAudienceNetwork', '6.20.1'" + EmbedNote,
                "  # pod 'FBAudienceNetwork', '6.20.1'",
                "  # Commented due to iOS Resolver settings.",
                "end")));
        }

        [Test]
        public void DirectDeclaration_IsRepeatedWithItsVersion_WithoutItsComment()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.ExplicitDeclaration, PodfileFixtures.Options());

            Assert.That(result.Podfile, Does.Contain(PodfileFixtures.Lf(
                "target 'Unity-iPhone' do",
                "  pod 'FBAudienceNetwork', '6.20.1'" + EmbedNote,
                "end")));
            Assert.That(result.Podfile, Does.Contain("  pod 'FBAudienceNetwork', '6.20.1' # pinned by the project\n"));
            Assert.That(result.Warnings, Is.Empty);
        }

        [Test]
        public void HashInsideAString_IsNotTakenForAComment()
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'FBAudienceNetwork', :git => 'https://example.com/it\\'s.git#main' # fork",
                "end",
                "target 'Unity-iPhone' do",
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Podfile, Does.Contain(PodfileFixtures.Lf(
                "target 'Unity-iPhone' do",
                "  pod 'FBAudienceNetwork', :git => 'https://example.com/it\\'s.git#main'" + EmbedNote,
                "end")));
        }

        [TestCase("pod 'FBAudienceNetwork', '6.20.1', :configurations => ['Debug', 'Release']")]
        [TestCase("pod('FBAudienceNetwork', '6.20.1')")]
        [TestCase("pod 'FBAudienceNetwork', { :path => './Local/FBAudienceNetwork' }")]
        [TestCase("pod \"FBAudienceNetwork\", \"~> 6.20\" if ENV['WITH_META']")]
        public void DeclarationWithBracketsOrAModifier_IsRepeatedAsItIs(string declaration)
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  " + declaration,
                "end",
                "target 'Unity-iPhone' do",
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Podfile, Does.EndWith("target 'Unity-iPhone' do\n  " + declaration + EmbedNote + "\nend\n"));
            Assert.That(result.Warnings, Is.Empty);
        }

        [TestCase("pod 'FBAudienceNetwork',", "    '6.20.1'")]
        [TestCase("pod 'FBAudienceNetwork', \\", "    '6.20.1'")]
        [TestCase("pod 'FBAudienceNetwork', '6.20.1', :configurations => [", "    'Release']")]
        [TestCase("pod 'FBAudienceNetwork', :git =>", "    'https://example.com/fork.git'")]
        [TestCase("pod('FBAudienceNetwork', '6.20.1'", "    )")]
        [TestCase("pod 'FBAudienceNetwork', :path => %q(../My#Pods/FBAN)", "")]
        [TestCase("pod 'FBAudienceNetwork'; pod 'Other'", "")]
        public void DeclarationThatIsNotOneWholeLine_FallsBackToTheBareName_WithAWarning(string first, string second)
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  " + first,
                second,
                "end",
                "target 'Unity-iPhone' do",
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Podfile, Does.EndWith("target 'Unity-iPhone' do\n  " + EmbedLine + "\nend\n"));
            Assert.That(result.Warnings.Count, Is.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("FBAudienceNetwork"));
        }

        [Test]
        public void HandEditedPodfile_DoubleQuotesAndNestedTargets()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.HandEdited, PodfileFixtures.Options());

            Assert.That(result.Podfile, Does.Contain(PodfileFixtures.Lf(
                "    target \"Unity-iPhone\" do",
                "      " + EmbedLine,
                "        target 'Unity-iPhone Tests' do")));
            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
        }

        [Test]
        public void TabIndentedTarget_GetsALineIndentedOneLevelDeeper()
        {
            string podfile = PodfileFixtures.Lf(
                "abstract_target 'Shared' do",
                "\ttarget 'UnityFramework' do",
                "\t\tpod 'IronSourceFacebookAdapter'",
                "\tend",
                "\ttarget 'Unity-iPhone' do",
                "\tend",
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Podfile, Does.Contain("\ttarget 'Unity-iPhone' do\n\t  " + EmbedLine + "\n\tend\n"));
        }

        [TestCase("target 'Unity-iPhone' do")]
        [TestCase("target \"Unity-iPhone\" do")]
        [TestCase("target('Unity-iPhone') do")]
        [TestCase("target 'Unity-iPhone' do # the app")]
        public void AppTargetLine_IsFoundInItsUsualSpellings(string targetLine)
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'IronSourceFacebookAdapter'",
                "end",
                targetLine,
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Podfile, Does.EndWith(targetLine + "\n  " + EmbedLine + "\nend\n"));
        }

        [TestCase("target 'Unity-iPhone Tests' do", "end")]
        [TestCase("# target 'Unity-iPhone' do", "# end")]
        public void AnotherTarget_IsNotMistakenForTheAppTarget(string targetLine, string closingLine)
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'IronSourceFacebookAdapter'",
                "end",
                targetLine,
                closingLine);

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Podfile, Is.EqualTo(podfile + "target 'Unity-iPhone' do\n  " + EmbedLine + "\nend\n"));
        }

        [TestCase("['Unity-iPhone', 'Unity-iPhone Tests'].each do |name|\n  target name do\n  end\nend")]
        [TestCase("target('Unity-iPhone') {\n}")]
        [TestCase("target 'Unity-iPhone' do end")]
        [TestCase("app = \"Unity-iPhone\"\ntarget app do\nend")]
        public void AppTargetDeclaredInAnotherWay_IsNotDeclaredASecondTime(string declaration)
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'IronSourceFacebookAdapter'",
                "end",
                declaration);

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Changed, Is.False);
            Assert.That(result.EmbeddedPods, Is.Empty);
            Assert.That(result.Warnings.Count, Is.EqualTo(1));
            Assert.That(result.Warnings[0], Does.Contain("FBAudienceNetwork"));
        }

        [Test]
        public void SubspecOfATriggerPod_CountsAsThatPod()
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'IronSourceFacebookAdapter/Core', '4.3.47'",
                "end",
                "target 'Unity-iPhone' do",
                "end");

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
        }

        [Test]
        public void SeveralRules_KeepTheirOrder_AndUnusableEntriesAreSkipped()
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'AdapterA'",
                "  pod 'AdapterB'",
                "end",
                "target 'Unity-iPhone' do",
                "end");
            var options = new PodfilePatchOptions();
            options.EmbeddedPods.Add(new EmbeddedPodRule("DynamicB", "AdapterB"));
            options.EmbeddedPods.Add(new EmbeddedPodRule("DynamicA", "AdapterA"));
            options.EmbeddedPods.Add(new EmbeddedPodRule("Unused", "NotInThePodfile", null, " "));
            options.EmbeddedPods.Add(null);
            options.EmbeddedPods.Add(new EmbeddedPodRule("  "));
            options.EmbeddedPods.Add(new EmbeddedPodRule { pod = null });
            options.EmbeddedPods.Add(new EmbeddedPodRule { pod = "NoDependents", requiredBy = null });

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, options);

            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "DynamicB", "DynamicA" }));
            Assert.That(result.Podfile, Does.Contain(
                "target 'Unity-iPhone' do\n" +
                "  pod 'DynamicB'" + EmbedNote + "\n" +
                "  pod 'DynamicA'" + EmbedNote + "\n" +
                "end\n"));
            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.None));
            Assert.That(result.Summary, Is.EqualTo("Declared on the application target for embedding: DynamicB, DynamicA."));
        }

        [Test]
        public void TwoRulesForTheSamePod_AreMerged()
        {
            // The second entry names the only adapter this Podfile has.
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'GoogleMobileAdsMediationFacebook'",
                "end",
                "target 'Unity-iPhone' do",
                "end");
            var options = new PodfilePatchOptions();
            options.EmbeddedPods.Add(new EmbeddedPodRule("FBAudienceNetwork", "IronSourceFacebookAdapter"));
            options.EmbeddedPods.Add(new EmbeddedPodRule("FBAudienceNetwork", "GoogleMobileAdsMediationFacebook"));

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, options);

            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
            Assert.That(Count(result.Podfile, EmbedLine), Is.EqualTo(1));
        }

        [Test]
        public void CustomAppTargetName_IsUsed()
        {
            string podfile = PodfileFixtures.Lf(
                "target 'UnityFramework' do",
                "  pod 'IronSourceFacebookAdapter'",
                "end",
                "target 'Game' do",
                "end");
            PodfilePatchOptions options = PodfileFixtures.Options(minimum: null);
            options.AppTargetName = " Game ";

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, options);

            Assert.That(result.Podfile, Does.Contain("target 'Game' do\n  " + EmbedLine + "\nend\n"));
        }

        // ---- Running again --------------------------------------------------------------------

        [TestCaseSource(typeof(PodfileFixtures), nameof(PodfileFixtures.Names))]
        public void PatchingTwice_EqualsPatchingOnce(string fixture)
        {
            string podfile = PodfileFixtures.Get(fixture);
            PodfilePatchResult once = PodfilePatcher.Patch(podfile, PodfileFixtures.Options());
            PodfilePatchResult twice = PodfilePatcher.Patch(once.Podfile, PodfileFixtures.Options());

            Assert.That(twice.Podfile, Is.SameAs(once.Podfile));
            Assert.That(twice.Changed, Is.False);
            Assert.That(twice.EmbeddedPods, Is.Empty);
            if (once.PostInstall == PostInstallChange.HookAppended || once.PostInstall == PostInstallChange.MergedIntoExistingHook)
            {
                Assert.That(twice.PostInstall, Is.EqualTo(PostInstallChange.AlreadyPresent));
            }
        }

        [Test]
        public void SecondCallWithMoreRules_AddsOnlyWhatIsMissing()
        {
            // The build hook without a settings asset raises deployment targets only. A build script
            // of the project that calls the patcher afterwards with its own rules still gets them.
            PodfilePatchResult first = PodfilePatcher.Patch(PodfileFixtures.Standard, MinimumOnly("15.0"));

            PodfilePatchResult second = PodfilePatcher.Patch(first.Podfile, PodfileFixtures.Options());

            Assert.That(second.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
            Assert.That(second.PostInstall, Is.EqualTo(PostInstallChange.AlreadyPresent));
            Assert.That(second.Podfile, Is.EqualTo(PodfilePatcher.Patch(PodfileFixtures.Standard, PodfileFixtures.Options()).Podfile));
        }

        [Test]
        public void ExistingBlockForAnotherMinimum_IsKept_WithAWarning()
        {
            PodfilePatchResult first = PodfilePatcher.Patch(PodfileFixtures.Standard, MinimumOnly("15.0"));

            PodfilePatchResult second = PodfilePatcher.Patch(first.Podfile, MinimumOnly("16.0"));

            Assert.That(second.Changed, Is.False);
            Assert.That(second.PostInstall, Is.EqualTo(PostInstallChange.AlreadyPresent));
            Assert.That(second.Warnings.Count, Is.EqualTo(1));
            Assert.That(second.Warnings[0], Does.Contain("15.0").And.Contain("16.0"));
        }

        [Test]
        public void MarkerWordInAnOrdinaryComment_SwitchesNothingOff()
        {
            string podfile = "# ios-export-fixes takes care of the deployment targets\n" + PodfileFixtures.Standard;

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options());

            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.HookAppended));
            Assert.That(result.EmbeddedPods, Is.EqualTo(new[] { "FBAudienceNetwork" }));
        }

        // ---- Text handling -------------------------------------------------------------------

        [TestCaseSource(typeof(PodfileFixtures), nameof(PodfileFixtures.Names))]
        public void OnlyInsertsLines(string fixture)
        {
            string podfile = PodfileFixtures.Get(fixture);
            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options());

            List<string> before = SplitKeepingLineBreaks(podfile);
            List<string> after = SplitKeepingLineBreaks(result.Podfile);
            int matched = 0;
            foreach (string line in after)
            {
                if (matched < before.Count && line == before[matched]) matched++;
            }

            Assert.That(matched, Is.EqualTo(before.Count), "an existing line was changed, moved or removed");
        }

        [TestCase("target 'UnityFramework' do\n  pod 'Firebase/Analytics'\nend", "\n\n" + RaiseNote + "\n")]
        [TestCase("post_install do |installer|", "\n  " + RaiseNote + "\n")]
        public void MissingFinalLineBreak_IsAddedBeforeAnythingFollows(string podfile, string expectedNext)
        {
            PodfilePatchResult result = PodfilePatcher.Patch(podfile, MinimumOnly("15.0"));

            Assert.That(result.Podfile, Does.StartWith(podfile + expectedNext));
        }

        [Test]
        public void TargetLineWithoutALineBreak_GetsOneBeforeThePodLine()
        {
            string podfile = "pod 'IronSourceFacebookAdapter'\ntarget 'Unity-iPhone' do";

            PodfilePatchResult result = PodfilePatcher.Patch(podfile, PodfileFixtures.Options(minimum: null));

            Assert.That(result.Podfile, Is.EqualTo(podfile + "\n  " + EmbedLine + "\n"));
        }

        [Test]
        public void EmptyPodfile_GetsOnlyTheHook()
        {
            PodfilePatchResult result = PodfilePatcher.Patch("", MinimumOnly("16.4"));

            Assert.That(result.Podfile, Does.StartWith(
                "# ios-export-fixes: build every pod for iOS 16.4 or later\n" +
                "post_install do |installer|\n"));
            Assert.That(result.Podfile, Does.Contain("Gem::Version.new('16.4')"));
            Assert.That(result.Podfile, Does.Contain(Setting + " = '16.4'"));
        }

        // ---- Options -------------------------------------------------------------------------

        [Test]
        public void NothingRequested_NothingChanges()
        {
            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.Standard, new PodfilePatchOptions());

            Assert.That(result.Changed, Is.False);
            Assert.That(result.Podfile, Is.SameAs(PodfileFixtures.Standard));
            Assert.That(result.PostInstall, Is.EqualTo(PostInstallChange.None));
            Assert.That(result.Summary, Is.Empty);
        }

        [TestCase("15")]
        [TestCase("15.0")]
        [TestCase("15.0.1")]
        [TestCase(" 15.0 ")]
        public void MinimumThatIsAVersionNumber_IsAccepted(string minimum)
        {
            PodfilePatchResult result = PodfilePatcher.Patch("", MinimumOnly(minimum));

            Assert.That(result.Podfile, Does.Contain("Gem::Version.new('" + minimum.Trim() + "')"));
        }

        [TestCase("fifteen")]
        [TestCase("15.x")]
        [TestCase("1.0.0.1")]
        [TestCase("15,0")]
        [TestCase("15.0'); system('true")]
        public void MinimumThatIsNotAVersionNumber_IsRejected(string minimum)
        {
            Assert.Throws<ArgumentException>(() => PodfilePatcher.Patch(PodfileFixtures.Standard, MinimumOnly(minimum)));
        }

        [TestCase("Two Words")]
        [TestCase("Quote'd")]
        [TestCase("Double\"Quote")]
        [TestCase("Back\\slash")]
        [TestCase("Hash#tag")]
        [TestCase("Comma,")]
        [TestCase("Line\nBreak")]
        public void PodNameThatWouldBreakTheRuby_IsRejected(string name)
        {
            var asPod = new PodfilePatchOptions();
            asPod.EmbeddedPods.Add(new EmbeddedPodRule(name));
            var asDependent = new PodfilePatchOptions();
            asDependent.EmbeddedPods.Add(new EmbeddedPodRule("Fine", name));

            Assert.Throws<ArgumentException>(() => PodfilePatcher.Patch(PodfileFixtures.Standard, asPod));
            Assert.Throws<ArgumentException>(() => PodfilePatcher.Patch(PodfileFixtures.Standard, asDependent));
        }

        [TestCase("Unity'iPhone")]
        [TestCase("Unity\"iPhone")]
        [TestCase("Unity\\iPhone")]
        [TestCase("Unity\niPhone")]
        public void TargetNameThatWouldBreakTheRuby_IsRejected(string name)
        {
            var options = new PodfilePatchOptions { AppTargetName = name };

            Assert.Throws<ArgumentException>(() => PodfilePatcher.Patch(PodfileFixtures.Standard, options));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void BlankTargetName_MeansTheUnityDefault(string name)
        {
            PodfilePatchOptions options = PodfileFixtures.Options(minimum: null);
            options.AppTargetName = name;

            PodfilePatchResult result = PodfilePatcher.Patch(PodfileFixtures.Standard, options);

            Assert.That(result.Podfile, Does.Contain("target 'Unity-iPhone' do\n  " + EmbedLine + "\nend\n"));
        }

        [Test]
        public void NullArguments_AreRejected()
        {
            Assert.Throws<ArgumentNullException>(() => PodfilePatcher.Patch(null, new PodfilePatchOptions()));
            Assert.Throws<ArgumentNullException>(() => PodfilePatcher.Patch("", null));
        }

        [TestCase("", "15.0", "15.0")]
        [TestCase("  ", "15.0", "15.0")]
        [TestCase(null, "15.0", "15.0")]
        [TestCase("16.4", "15.0", "16.4")]
        public void OptionsFromSettings_UseTheOverrideOrElseTheAppMinimum(string minimumOverride, string appMinimum, string expected)
        {
            var rules = new List<EmbeddedPodRule> { new EmbeddedPodRule("FBAudienceNetwork", "IronSourceFacebookAdapter") };

            PodfilePatchOptions options = PodfilePatchOptions.Create(true, minimumOverride, appMinimum, "Game", rules);

            Assert.That(options.MinimumDeploymentTarget, Is.EqualTo(expected));
            Assert.That(options.AppTargetName, Is.EqualTo("Game"));
            Assert.That(options.EmbeddedPods, Is.EqualTo(rules));
        }

        [Test]
        public void OptionsFromSettings_CanSwitchTheDeploymentTargetFixOff()
        {
            PodfilePatchOptions options = PodfilePatchOptions.Create(false, "16.4", "15.0", null, null);

            Assert.That(options.MinimumDeploymentTarget, Is.Null);
            Assert.That(options.EmbeddedPods, Is.Empty);
            Assert.That(PodfilePatcher.Patch(PodfileFixtures.Standard, options).Changed, Is.False);
        }

        // ---- Helpers -------------------------------------------------------------------------

        private static PodfilePatchOptions MinimumOnly(string minimum)
        {
            return new PodfilePatchOptions { MinimumDeploymentTarget = minimum };
        }

        /// <summary>The lines the deployment-target block is expected to consist of.</summary>
        private static string[] RaiseBlock(string indent, string installer, string minimum)
        {
            return new[]
            {
                indent + installer + ".generated_projects.each do |project|",
                indent + "  project.targets.each do |target|",
                indent + "    target.build_configurations.each do |config|",
                indent + "      next unless Gem::Version.correct?(" + Setting + ".to_s)",
                indent + "      next unless Gem::Version.new(" + Setting + ".to_s) < Gem::Version.new('" + minimum + "')",
                indent + "      " + Setting + " = '" + minimum + "'",
                indent + "    end",
                indent + "  end",
                indent + "end",
            };
        }

        private static int Count(string text, string value)
        {
            int count = 0;
            int at = text.IndexOf(value, StringComparison.Ordinal);
            while (at >= 0)
            {
                count++;
                at = text.IndexOf(value, at + value.Length, StringComparison.Ordinal);
            }

            return count;
        }

        private static List<string> SplitKeepingLineBreaks(string text)
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
    }
}
