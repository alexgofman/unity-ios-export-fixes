using NUnit.Framework;

namespace IosExportFixes.Tests
{
    public class InfoPlistOptionsTests
    {
        [Test]
        public void Languages_AreTrimmed_BlanksDropped_OrderKept()
        {
            var options = new InfoPlistOptions(new[] { " en ", "", "pt-BR", null, "sr-Latn", "   " }, null, null);

            Assert.That(options.Localizations, Is.EqualTo(new[] { "en", "pt-BR", "sr-Latn" }));
        }

        [Test]
        public void RepeatedLanguages_AreListedOnce_FirstSpellingWins()
        {
            var options = new InfoPlistOptions(new[] { "en", "sr-Latn", "EN", "sr-latn", "fr" }, null, null);

            Assert.That(options.Localizations, Is.EqualTo(new[] { "en", "sr-Latn", "fr" }));
        }

        [Test]
        public void BlankTexts_MeanLeaveTheKeyAlone()
        {
            var options = new InfoPlistOptions(null, "  ", "");

            Assert.That(options.Localizations, Is.Empty);
            Assert.That(options.DevelopmentRegion, Is.Null);
            Assert.That(options.UserTrackingUsageDescription, Is.Null);
            Assert.That(options.HasChanges, Is.False);
        }

        [Test]
        public void Texts_AreTrimmed()
        {
            var options = new InfoPlistOptions(null, " en ", "  Used to show relevant ads.\n");

            Assert.That(options.DevelopmentRegion, Is.EqualTo("en"));
            Assert.That(options.UserTrackingUsageDescription, Is.EqualTo("Used to show relevant ads."));
            Assert.That(options.HasChanges, Is.True);
        }

        [TestCase(new[] { "en" }, null, null)]
        [TestCase(new string[0], "en", null)]
        [TestCase(new string[0], null, "Used to show relevant ads.")]
        public void AnySingleValue_CountsAsAChange(string[] languages, string region, string tracking)
        {
            Assert.That(new InfoPlistOptions(languages, region, tracking).HasChanges, Is.True);
        }
    }
}
