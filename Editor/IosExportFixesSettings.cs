using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace IosExportFixes
{
    /// <summary>
    /// Project settings for both build hooks. Create one asset with
    /// Assets &gt; Create &gt; iOS Export Fixes &gt; Settings and keep it in an Editor folder.
    /// </summary>
    /// <remarks>
    /// Without an asset the Podfile hook still raises pod deployment targets to the minimum iOS
    /// version from Player Settings, because that fix needs no project-specific values. Embedding
    /// pods and writing Info.plist keys only happen when an asset says what to write.
    /// </remarks>
    [CreateAssetMenu(fileName = "IosExportFixesSettings", menuName = "iOS Export Fixes/Settings")]
    public sealed class IosExportFixesSettings : ScriptableObject
    {
        internal const string LogPrefix = "[iOS Export Fixes] ";

        [Header("Podfile")]
        [Tooltip("Raise every pod target below the minimum iOS version to that version.")]
        public bool raisePodDeploymentTargets = true;

        [Tooltip("Minimum iOS version for pods, for example 15.0. Leave empty to use " +
                 "Player Settings > Target minimum iOS Version.")]
        public string minimumDeploymentTarget = "";

        [Tooltip("Application target of the exported Xcode project.")]
        public string appTargetName = PodfilePatchOptions.DefaultAppTargetName;

        [Tooltip("Pods that ship as dynamic frameworks. When the Podfile uses one, it is also declared " +
                 "on the application target so that CocoaPods embeds it in the app.")]
        public List<EmbeddedPodRule> embeddedDynamicPods = new List<EmbeddedPodRule>();

        [Header("Info.plist")]
        [Tooltip("Language identifiers written to CFBundleLocalizations, for example en, pt-BR, zh-Hans. " +
                 "Leave empty to keep the key as exported.")]
        public List<string> localizations = new List<string>();

        [Tooltip("Value for CFBundleDevelopmentRegion. Leave empty to keep the exported value.")]
        public string developmentRegion = "";

        [Tooltip("Text of the App Tracking Transparency prompt (NSUserTrackingUsageDescription). " +
                 "Leave empty to keep the key as exported.")]
        [TextArea(2, 4)]
        public string userTrackingUsageDescription = "";

        /// <param name="playerSettingsMinimum">
        /// Minimum iOS version from Player Settings, used when <see cref="minimumDeploymentTarget"/> is empty.
        /// </param>
        public PodfilePatchOptions ToPodfileOptions(string playerSettingsMinimum)
        {
            return PodfilePatchOptions.Create(
                raisePodDeploymentTargets, minimumDeploymentTarget, playerSettingsMinimum, appTargetName, embeddedDynamicPods);
        }

        public InfoPlistOptions ToInfoPlistOptions()
        {
            return new InfoPlistOptions(localizations, developmentRegion, userTrackingUsageDescription);
        }

        /// <summary>
        /// The settings asset of the project, or null when there is none. With several assets the one
        /// whose path sorts first is used, so the choice does not depend on import order.
        /// </summary>
        public static IosExportFixesSettings Find()
        {
            string[] guids = AssetDatabase.FindAssets("t:" + nameof(IosExportFixesSettings));
            if (guids.Length == 0) return null;

            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            }

            Array.Sort(paths, StringComparer.Ordinal);
            if (paths.Length > 1)
            {
                Debug.LogWarning(LogPrefix + paths.Length + " settings assets found; using " + paths[0] + ".");
            }

            return AssetDatabase.LoadAssetAtPath<IosExportFixesSettings>(paths[0]);
        }
    }
}
