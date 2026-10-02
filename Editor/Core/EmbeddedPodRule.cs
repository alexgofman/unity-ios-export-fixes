using System;
using System.Collections.Generic;

namespace IosExportFixes
{
    /// <summary>
    /// A pod that ships as a dynamic framework and therefore has to be embedded by the application
    /// target. The rule applies only when the Podfile already declares <see cref="pod"/> or one of
    /// the pods in <see cref="requiredBy"/>, so it does nothing in builds that do not use that SDK.
    /// </summary>
    /// <remarks>
    /// Public fields with lower-case names because Unity serialises this type inside the settings
    /// asset. The class itself has no Unity dependency.
    /// </remarks>
    [Serializable]
    public sealed class EmbeddedPodRule
    {
        /// <summary>Name of the dynamic-framework pod, for example <c>FBAudienceNetwork</c>.</summary>
        public string pod = "";

        /// <summary>
        /// Pods that pull <see cref="pod"/> in as a dependency, for example a mediation adapter.
        /// A subspec of a listed pod counts as that pod.
        /// </summary>
        public List<string> requiredBy = new List<string>();

        public EmbeddedPodRule()
        {
        }

        public EmbeddedPodRule(string pod, params string[] requiredBy)
        {
            this.pod = pod;
            if (requiredBy != null)
            {
                this.requiredBy.AddRange(requiredBy);
            }
        }
    }
}
