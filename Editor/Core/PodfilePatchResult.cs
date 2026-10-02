using System.Collections.Generic;

namespace IosExportFixes
{
    /// <summary>What <see cref="PodfilePatcher.Patch"/> did about the <c>post_install</c> hook.</summary>
    public enum PostInstallChange
    {
        /// <summary>No minimum deployment target was requested.</summary>
        None,

        /// <summary>The Podfile had no <c>post_install</c> hook, so one was appended.</summary>
        HookAppended,

        /// <summary>The block was inserted at the top of the hook the Podfile already had.</summary>
        MergedIntoExistingHook,

        /// <summary>The Podfile already carries the block from an earlier run; it was left as it is.</summary>
        AlreadyPresent,

        /// <summary>
        /// The Podfile has a <c>post_install</c> hook in a form the patcher does not edit. Nothing was
        /// added, because CocoaPods rejects a Podfile that registers two hooks.
        /// </summary>
        SkippedUnsupportedHook,
    }

    /// <summary>Outcome of <see cref="PodfilePatcher.Patch"/>.</summary>
    public sealed class PodfilePatchResult
    {
        internal PodfilePatchResult(
            string podfile,
            bool changed,
            PostInstallChange postInstall,
            IReadOnlyList<string> embeddedPods,
            IReadOnlyList<string> warnings,
            string summary)
        {
            Podfile = podfile;
            Changed = changed;
            PostInstall = postInstall;
            EmbeddedPods = embeddedPods;
            Warnings = warnings;
            Summary = summary;
        }

        /// <summary>The Podfile text to write back. The input object itself when nothing changed.</summary>
        public string Podfile { get; }

        /// <summary>True when <see cref="Podfile"/> differs from the input.</summary>
        public bool Changed { get; }

        public PostInstallChange PostInstall { get; }

        /// <summary>Pods that were added to the application target in this call, in rule order.</summary>
        public IReadOnlyList<string> EmbeddedPods { get; }

        /// <summary>Things the caller should surface, such as a hook that could not be extended.</summary>
        public IReadOnlyList<string> Warnings { get; }

        /// <summary>One or two sentences on what was changed, for a log line. Empty when nothing was.</summary>
        public string Summary { get; }
    }
}
