using System.Collections.Generic;

namespace IosExportFixes
{
    /// <summary>
    /// Input of <see cref="PodfilePatcher.Patch"/>. Plain data with no Unity types, so the patcher can
    /// be compiled and tested outside the editor.
    /// </summary>
    public sealed class PodfilePatchOptions
    {
        /// <summary>Name Unity gives the application target of an iOS export.</summary>
        public const string DefaultAppTargetName = "Unity-iPhone";

        /// <summary>
        /// Lowest iOS version a pod target may keep, for example <c>15.0</c>. Pod targets below it are
        /// raised to it; pod targets above it keep their own value. Null or blank switches the
        /// deployment-target fix off.
        /// </summary>
        public string MinimumDeploymentTarget { get; set; }

        /// <summary>
        /// Application target of the exported Xcode project. Blank means
        /// <see cref="DefaultAppTargetName"/>.
        /// </summary>
        public string AppTargetName { get; set; } = DefaultAppTargetName;

        /// <summary>
        /// Dynamic-framework pods the application target has to embed. Empty by default. Entries
        /// for the same pod are merged.
        /// </summary>
        public List<EmbeddedPodRule> EmbeddedPods { get; } = new List<EmbeddedPodRule>();

        /// <summary>
        /// Builds the options from the values of a settings object.
        /// </summary>
        /// <param name="raiseDeploymentTargets">False switches the deployment-target fix off.</param>
        /// <param name="minimumOverride">The minimum to use; blank means <paramref name="defaultMinimum"/>.</param>
        /// <param name="defaultMinimum">The app's own minimum iOS version, from Player Settings.</param>
        /// <param name="appTargetName">Application target name; blank means the Unity default.</param>
        /// <param name="embeddedPods">Embed rules; null means none.</param>
        public static PodfilePatchOptions Create(
            bool raiseDeploymentTargets,
            string minimumOverride,
            string defaultMinimum,
            string appTargetName,
            IEnumerable<EmbeddedPodRule> embeddedPods)
        {
            var options = new PodfilePatchOptions { AppTargetName = appTargetName };
            if (raiseDeploymentTargets)
            {
                options.MinimumDeploymentTarget = string.IsNullOrWhiteSpace(minimumOverride)
                    ? defaultMinimum
                    : minimumOverride;
            }

            if (embeddedPods != null)
            {
                options.EmbeddedPods.AddRange(embeddedPods);
            }

            return options;
        }
    }
}
