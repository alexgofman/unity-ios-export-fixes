// UnityEditor.iOS.Xcode exists only when iOS Build Support is installed. UNITY_IOS is defined while
// iOS is the active build target, which implies the module is there.
#if UNITY_IOS
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.iOS.Xcode;
using UnityEngine;

namespace IosExportFixes
{
    /// <summary>
    /// Writes the Info.plist keys from the settings asset into an iOS export: the list of languages
    /// (CFBundleLocalizations), the development region and the App Tracking Transparency text.
    /// Does nothing when there is no settings asset or all three values are empty.
    /// </summary>
    public static class InfoPlistPostProcessor
    {
        /// <summary>
        /// Late in the sequence, so that another package's hook which rewrites Info.plist earlier
        /// does not drop these keys. A hook with a higher order can still overwrite them.
        /// </summary>
        public const int CallbackOrder = 999;

        [PostProcessBuild(CallbackOrder)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            IosExportFixesSettings settings = IosExportFixesSettings.Find();
            if (settings == null) return;

            InfoPlistOptions options = settings.ToInfoPlistOptions();
            if (!options.HasChanges) return;

            string path = Path.Combine(pathToBuiltProject, "Info.plist");
            if (!File.Exists(path))
            {
                Debug.LogError(IosExportFixesSettings.LogPrefix + "Info.plist not found at " + path + "; no keys written.", settings);
                return;
            }

            var plist = new PlistDocument();
            plist.ReadFromFile(path);
            IReadOnlyList<string> written = InfoPlistPatcher.Apply(plist.root, options);
            plist.WriteToFile(path);

            Debug.Log(IosExportFixesSettings.LogPrefix + "Info.plist keys written: " + string.Join(", ", written) + ".");
        }
    }
}
#endif
