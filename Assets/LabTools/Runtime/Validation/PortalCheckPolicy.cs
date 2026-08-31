using System.Collections.Generic;
using UnityEngine;

namespace Portals.Lab.Validation
{
    public sealed class PortalCheckDecision
    {
        public string status;
        public string failureReason;

        public PortalCheckDecision(string status, string reason)
        {
            this.status = status;
            failureReason = reason;
        }
    }

    public static class PortalCheckPolicy
    {
        // Порог относится к нормализованному raw RGB из Texture2D.GetPixels, не к HDR radiance.
        public const float ColorCrossTolerance = 0.001f;
        public const float SeamStepSeconds = 1f / 60f;

        public static PortalCheckDecision Color(IDictionary<string, UnityEngine.Color> samples,
            int expectedSamples, bool captureFailed)
        {
            if (captureFailed || samples == null || expectedSamples < 4 || samples.Count != expectedSamples)
                return Failed("Color capture set is incomplete.");
            foreach (UnityEngine.Color sample in samples.Values)
                if (!Finite(sample.r) || !Finite(sample.g) || !Finite(sample.b))
                    return Failed("Color capture contains a non-finite RGB metric.");
            if (!samples.ContainsKey("farThrough") || !samples.ContainsKey("farDirect")
                || !samples.TryGetValue("crossBefore", out UnityEngine.Color before)
                || !samples.TryGetValue("crossAfter", out UnityEngine.Color after))
                return Failed("Required Color captures are missing.");
            float delta = Mathf.Max(Mathf.Abs(before.r - after.r),
                Mathf.Max(Mathf.Abs(before.g - after.g), Mathf.Abs(before.b - after.b)));
            return delta <= ColorCrossTolerance ? new PortalCheckDecision("Passed", string.Empty)
                : Failed("Color cross max raw RGB mean delta exceeds 0.001.");
        }

        public static PortalCheckDecision Seam(IList<double> differences, IList<double> luminances,
            int crossingCount, int crossingFrame)
        {
            if (crossingCount != 1) return Failed("Seam requires exactly one Teleported event.");
            if (differences == null || luminances == null || differences.Count != luminances.Count
                || crossingFrame < 1 || crossingFrame + 2 >= differences.Count)
                return Failed("Seam requires pre-cross and at least two post-cross samples.");
            for (int i = 0; i < differences.Count; i++)
                if (!Finite(luminances[i]) || (i > 0 && (!Finite(differences[i]) || differences[i] < 0)))
                    return Failed("Seam contains missing or non-finite metrics.");
            return new PortalCheckDecision("Blocked", "Seam visual threshold not calibrated.");
        }

        public static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        private static PortalCheckDecision Failed(string reason) => new PortalCheckDecision("Failed", reason);
    }
}
