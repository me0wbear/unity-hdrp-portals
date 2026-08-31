using System;

namespace Portals.Lab.Validation
{
    [Serializable]
    public sealed class PortalVisibilitySample
    {
        public string mode;
        public int mainCallbacks;
        public int[] virtualCallbacks;
        public bool bindingsValid, capacityValid, historyValid;
        public string[] cameraState;
    }

    [Serializable]
    public sealed class PortalVisibilityEvidence
    {
        public PortalVisibilitySample[] samples;
        public PortalImageDifference aReference, bReference, aShallow, bShallow, reentrySettled;
    }

    public static class PortalVisibilityPolicy
    {
        public static readonly string[] Modes = { "a-reference", "a-shallow", "a-visible", "hidden", "reentry-first",
            "reentry-settled", "b-reference", "b-shallow", "b-visible", "cold", "roots", "priority", "recursion", "starved", "return" };

        public static PortalCheckDecision Evaluate(PortalVisibilityEvidence evidence, string problem)
        {
            if (!string.IsNullOrEmpty(problem)) return Decision("Blocked", problem);
            if (evidence?.samples == null || evidence.samples.Length != Modes.Length)
                return Decision("Blocked", "Visibility requires every named control.");
            int[][] expected = { new[]{5}, new[]{1}, new[]{5}, new[]{0}, new[]{5}, new[]{5}, new[]{5}, new[]{1}, new[]{5},
                new[]{0,0,0}, new[]{1,1,1}, new[]{0,0,1}, new[]{1,1,2}, new[]{0,0,0}, new[]{1,1,1} };
            for (int i = 0; i < Modes.Length; i++)
            {
                PortalVisibilitySample sample = evidence.samples[i];
                if (sample == null || sample.mode != Modes[i] || sample.virtualCallbacks == null
                    || sample.virtualCallbacks.Length != expected[i].Length)
                    return Decision("Blocked", "Missing, duplicate, or out-of-order visibility observation.");
                if (sample.mainCallbacks != 1 || !sample.bindingsValid || !sample.capacityValid || !sample.historyValid)
                    return Decision("Failed", sample.mode + ": callback, binding, capacity, or history invariant failed.");
                for (int root = 0; root < expected[i].Length; root++)
                    if (sample.virtualCallbacks[root] != expected[i][root])
                        return Decision("Failed", sample.mode + ": actual per-root virtual callbacks differ from the contract.");
            }
            var images = new[]{ evidence.aReference, evidence.bReference, evidence.aShallow,
                evidence.bShallow, evidence.reentrySettled };
            foreach (PortalImageDifference image in images)
                if (image == null || !image.IsValid(102400))
                    return Decision("Blocked", "Visibility requires all finite 320x320 RGB comparisons.");
            if (evidence.aReference.maxChannelDifference != 0 || evidence.bReference.maxChannelDifference != 0
                || evidence.reentrySettled.maxChannelDifference != 0)
                return Decision("Failed", "Optimized or settled reentry pixels differ from the full-prefix reference.");
            foreach (PortalImageDifference image in new[]{ evidence.aShallow, evidence.bShallow })
                if (image.maxChannelDifference < 16 || (image.redMae + image.greenMae + image.blueMae) / 3 < 0.5)
                    return Decision("Blocked", "Depth0 does not demonstrate visible recursive content in both openings.");
            return Decision("Passed", string.Empty);
        }

        private static PortalCheckDecision Decision(string status, string reason) => new PortalCheckDecision(status, reason);
    }
}
