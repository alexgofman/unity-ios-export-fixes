using System;
using System.Collections.Generic;

namespace IosExportFixes
{
    /// <summary>
    /// The Info.plist values the plist hook writes. Plain data: the clean-up of the settings happens
    /// here, where it can be tested without the Xcode API.
    /// </summary>
    public sealed class InfoPlistOptions
    {
        public InfoPlistOptions(
            IEnumerable<string> localizations, string developmentRegion, string userTrackingUsageDescription)
        {
            Localizations = CleanLanguages(localizations);
            DevelopmentRegion = NullIfBlank(developmentRegion);
            UserTrackingUsageDescription = NullIfBlank(userTrackingUsageDescription);
        }

        /// <summary>
        /// Language identifiers for <c>CFBundleLocalizations</c>, for example <c>en</c>, <c>pt-BR</c>,
        /// <c>zh-Hans</c>. Empty means the key is left as exported.
        /// </summary>
        public IReadOnlyList<string> Localizations { get; }

        /// <summary>Value for <c>CFBundleDevelopmentRegion</c>, or null to leave the key as exported.</summary>
        public string DevelopmentRegion { get; }

        /// <summary>
        /// Value for <c>NSUserTrackingUsageDescription</c>, the text of the App Tracking Transparency
        /// prompt, or null to leave the key as exported.
        /// </summary>
        public string UserTrackingUsageDescription { get; }

        /// <summary>False when every value is unset, in which case the plist is not touched at all.</summary>
        public bool HasChanges
        {
            get { return Localizations.Count > 0 || DevelopmentRegion != null || UserTrackingUsageDescription != null; }
        }

        /// <summary>
        /// Trims the identifiers, drops blank entries and removes repeats (ignoring case, first
        /// spelling wins) while keeping the order.
        /// </summary>
        public static IReadOnlyList<string> CleanLanguages(IEnumerable<string> languages)
        {
            var cleaned = new List<string>();
            if (languages == null) return cleaned;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string language in languages)
            {
                string identifier = (language ?? "").Trim();
                if (identifier.Length > 0 && seen.Add(identifier))
                {
                    cleaned.Add(identifier);
                }
            }

            return cleaned;
        }

        private static string NullIfBlank(string value)
        {
            string trimmed = (value ?? "").Trim();
            return trimmed.Length == 0 ? null : trimmed;
        }
    }
}
