// UnityEditor.iOS.Xcode exists only when iOS Build Support is installed. UNITY_IOS is defined while
// iOS is the active build target, which implies the module is there.
#if UNITY_IOS
using System;
using System.Collections.Generic;
using UnityEditor.iOS.Xcode;

namespace IosExportFixes
{
    /// <summary>
    /// Writes <see cref="InfoPlistOptions"/> into the root dictionary of an exported Info.plist.
    /// Only the keys below are written; every other entry is left as it is.
    /// </summary>
    /// <remarks>
    /// The App Store takes the "Languages" of an app from the binary, not from the store metadata:
    /// from its .lproj folders or, for an app that does not use .lproj folders, from
    /// CFBundleLocalizations (Apple Technical Q&amp;A QA1828). Unity's iOS project template has no
    /// CFBundleLocalizations key and no .lproj folder for the application target, so a project
    /// with its own localisation system has to declare its languages in that key.
    /// </remarks>
    public static class InfoPlistPatcher
    {
        public const string LocalizationsKey = "CFBundleLocalizations";
        public const string DevelopmentRegionKey = "CFBundleDevelopmentRegion";
        public const string TrackingUsageKey = "NSUserTrackingUsageDescription";

        /// <returns>The keys that were written, in the order they were written.</returns>
        public static IReadOnlyList<string> Apply(PlistElementDict root, InfoPlistOptions options)
        {
            if (root == null) throw new ArgumentNullException(nameof(root));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var written = new List<string>();

            if (options.DevelopmentRegion != null)
            {
                root.SetString(DevelopmentRegionKey, options.DevelopmentRegion);
                written.Add(DevelopmentRegionKey);
            }

            if (options.UserTrackingUsageDescription != null)
            {
                root.SetString(TrackingUsageKey, options.UserTrackingUsageDescription);
                written.Add(TrackingUsageKey);
            }

            if (options.Localizations.Count > 0)
            {
                // CreateArray replaces whatever is stored under the key, so running the hook again
                // (an "append" build) rewrites the array instead of growing it.
                PlistElementArray localizations = root.CreateArray(LocalizationsKey);
                foreach (string language in options.Localizations)
                {
                    localizations.AddString(language);
                }

                written.Add(LocalizationsKey);
            }

            return written;
        }
    }
}
#endif
