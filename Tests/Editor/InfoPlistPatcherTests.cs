// These tests use Unity's PlistDocument, so they exist only while iOS is the active build target.
// They are not part of the `dotnet test` run.
#if UNITY_IOS
using NUnit.Framework;
using UnityEditor.iOS.Xcode;

namespace IosExportFixes.Tests
{
    public class InfoPlistPatcherTests
    {
        private const string ExportedPlist =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n" +
            "  <dict>\n" +
            "    <key>CFBundleDevelopmentRegion</key>\n" +
            "    <string>en</string>\n" +
            "    <key>CFBundleDisplayName</key>\n" +
            "    <string>Sample</string>\n" +
            "    <key>UIRequiredDeviceCapabilities</key>\n" +
            "    <array>\n" +
            "      <string>arm64</string>\n" +
            "    </array>\n" +
            "    <key>UIRequiresFullScreen</key>\n" +
            "    <true/>\n" +
            "  </dict>\n" +
            "</plist>\n";

        [Test]
        public void WritesTheThreeKeys()
        {
            PlistDocument plist = Read(ExportedPlist);

            var options = new InfoPlistOptions(new[] { "en", "de", "sr-Latn" }, "de", "Used to show relevant ads.");

            var written = InfoPlistPatcher.Apply(plist.root, options);

            Assert.That(written, Is.EqualTo(new[]
            {
                "CFBundleDevelopmentRegion", "NSUserTrackingUsageDescription", "CFBundleLocalizations",
            }));
            Assert.That(Languages(plist), Is.EqualTo(new[] { "en", "de", "sr-Latn" }));
            Assert.That(plist.root["CFBundleDevelopmentRegion"].AsString(), Is.EqualTo("de"));
            Assert.That(plist.root["NSUserTrackingUsageDescription"].AsString(), Is.EqualTo("Used to show relevant ads."));
        }

        [Test]
        public void LeavesEveryOtherEntryAsItWas()
        {
            PlistDocument plist = Read(ExportedPlist);

            var written = InfoPlistPatcher.Apply(plist.root, new InfoPlistOptions(new[] { "en", "de" }, null, null));

            Assert.That(written, Is.EqualTo(new[] { "CFBundleLocalizations" }));
            Assert.That(plist.root["CFBundleDisplayName"].AsString(), Is.EqualTo("Sample"));
            Assert.That(plist.root["UIRequiresFullScreen"].AsBoolean(), Is.True);
            Assert.That(plist.root["UIRequiredDeviceCapabilities"].AsArray().values.Count, Is.EqualTo(1));
            // Unset options do not create or change keys.
            Assert.That(plist.root["CFBundleDevelopmentRegion"].AsString(), Is.EqualTo("en"));
            Assert.That(plist.root.values.ContainsKey("NSUserTrackingUsageDescription"), Is.False);
            Assert.That(plist.root.values.Count, Is.EqualTo(5));
        }

        [Test]
        public void SecondRun_ReplacesTheArray_InsteadOfGrowingIt()
        {
            PlistDocument plist = Read(ExportedPlist);
            var options = new InfoPlistOptions(new[] { "en", "de" }, "en", "Used to show relevant ads.");

            InfoPlistPatcher.Apply(plist.root, options);
            string once = plist.WriteToString();
            PlistDocument again = Read(once);
            InfoPlistPatcher.Apply(again.root, options);

            Assert.That(again.WriteToString(), Is.EqualTo(once));
            Assert.That(Languages(again), Is.EqualTo(new[] { "en", "de" }));
        }

        [Test]
        public void ShorterList_ReplacesALongerOne()
        {
            PlistDocument plist = Read(ExportedPlist);

            InfoPlistPatcher.Apply(plist.root, new InfoPlistOptions(new[] { "en", "de", "fr" }, null, null));
            InfoPlistPatcher.Apply(plist.root, new InfoPlistOptions(new[] { "en" }, null, null));

            Assert.That(Languages(plist), Is.EqualTo(new[] { "en" }));
        }

        [Test]
        public void NoValues_NoChange()
        {
            PlistDocument plist = Read(ExportedPlist);
            string before = plist.WriteToString();

            var written = InfoPlistPatcher.Apply(plist.root, new InfoPlistOptions(null, null, null));

            Assert.That(written, Is.Empty);
            Assert.That(plist.WriteToString(), Is.EqualTo(before));
        }

        private static PlistDocument Read(string xml)
        {
            var plist = new PlistDocument();
            plist.ReadFromString(xml);
            return plist;
        }

        private static string[] Languages(PlistDocument plist)
        {
            PlistElementArray array = plist.root[InfoPlistPatcher.LocalizationsKey].AsArray();
            var languages = new string[array.values.Count];
            for (int i = 0; i < languages.Length; i++)
            {
                languages[i] = array.values[i].AsString();
            }

            return languages;
        }
    }
}
#endif
