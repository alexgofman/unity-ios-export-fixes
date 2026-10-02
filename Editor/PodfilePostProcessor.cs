using System;
using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;

namespace IosExportFixes
{
    /// <summary>
    /// Runs <see cref="PodfilePatcher"/> on the Podfile of an iOS export, after External Dependency
    /// Manager for Unity (EDM4U) has written it and before EDM4U runs <c>pod install</c>.
    /// </summary>
    public static class PodfilePostProcessor
    {
        /// <summary>
        /// EDM4U writes the Podfile in a post-process step of order 40 and runs <c>pod install</c>
        /// in a step of order 50. A step that edits the Podfile has to sit between the two: any
        /// earlier and EDM4U overwrites the edit, any later and the pods are already installed from
        /// the unedited file.
        /// </summary>
        public const int CallbackOrder = 45;

        [PostProcessBuild(CallbackOrder)]
        public static void OnPostProcessBuild(BuildTarget target, string pathToBuiltProject)
        {
            if (target != BuildTarget.iOS) return;

            IosExportFixesSettings settings = IosExportFixesSettings.Find();
            WarnIfInfoPlistHookIsMissing(settings);

            string path = Path.Combine(pathToBuiltProject, "Podfile");
            if (!File.Exists(path))
            {
                // A project without CocoaPods dependencies has no Podfile, so this is not an error.
                Debug.Log(IosExportFixesSettings.LogPrefix + "No Podfile in " + pathToBuiltProject + "; nothing to patch.");
                return;
            }

            string playerSettingsMinimum = PlayerSettings.iOS.targetOSVersionString;
            PodfilePatchOptions options = settings != null
                ? settings.ToPodfileOptions(playerSettingsMinimum)
                : new PodfilePatchOptions { MinimumDeploymentTarget = playerSettingsMinimum };

            PodfilePatchResult result;
            try
            {
                result = PodfilePatcher.Patch(File.ReadAllText(path), options);
            }
            catch (ArgumentException exception)
            {
                Debug.LogError(IosExportFixesSettings.LogPrefix + "Podfile left unchanged. " + exception.Message, settings);
                return;
            }

            foreach (string warning in result.Warnings)
            {
                Debug.LogWarning(IosExportFixesSettings.LogPrefix + warning, settings);
            }

            if (!result.Changed) return;

            File.WriteAllText(path, result.Podfile);
            Debug.Log(IosExportFixesSettings.LogPrefix + "Podfile patched. " + result.Summary);
        }

        // InfoPlistPostProcessor is inside #if UNITY_IOS. Editor scripts are compiled for the active
        // build target, so a build script that builds iOS while another target is active gets no
        // Info.plist hook at all. Say so instead of leaving the keys out silently.
        private static void WarnIfInfoPlistHookIsMissing(IosExportFixesSettings settings)
        {
#if !UNITY_IOS
            if (settings != null && settings.ToInfoPlistOptions().HasChanges)
            {
                Debug.LogWarning(
                    IosExportFixesSettings.LogPrefix + "The Info.plist keys from the settings were not written, " +
                    "because iOS is not the active build target. Switch the active build target to iOS before building.",
                    settings);
            }
#endif
        }
    }
}
