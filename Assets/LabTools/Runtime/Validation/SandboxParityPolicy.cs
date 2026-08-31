using System;
using System.Collections.Generic;

namespace Portals.Lab.Validation
{
    [Serializable]
    public sealed class SandboxParitySample
    {
        public string mode;
        public string aa;
        public int captureCount;
        public bool cameraSettingsValid;
        public PortalImageDifference comparison;
        public PortalImageDifference repeat;
    }

    [Serializable]
    public sealed class PortalLeakageControl
    {
        public bool completed;
        public bool fixtureValid;
        public int regularPixels;
        public int obliquePixels;
        public string reason;
    }

    public static class SandboxParityPolicy
    {
        public static readonly string[] Modes = { "baseline", "ssao-off-both", "ssao-off-virtual-only", "regular-projection" };

        public static PortalCheckDecision Evaluate(SandboxParitySample[] samples, PortalLeakageControl control, string fixtureFailure)
        {
            if (!string.IsNullOrEmpty(fixtureFailure)) return Blocked(fixtureFailure);
            if (samples == null || samples.Length != 8) return Blocked("Parity requires all four modes with None and TAA.");
            var keys = new HashSet<string>();
            SandboxParitySample baseline = null;
            foreach (SandboxParitySample sample in samples)
            {
                if (sample == null || Array.IndexOf(Modes, sample.mode) < 0 || (sample.aa != "none" && sample.aa != "taa")
                    || !keys.Add(sample.mode + "/" + sample.aa) || sample.captureCount != 4 || !sample.cameraSettingsValid
                    || sample.comparison == null || !sample.comparison.IsValid(64000)
                    || sample.repeat == null || !sample.repeat.IsValid(64000))
                    return Blocked("Parity capture, effective camera settings, or RGB evidence is incomplete.");
                if (sample.mode == "baseline" && sample.aa == "none") baseline = sample;
            }
            if (control == null || !control.completed || !control.fixtureValid || control.regularPixels <= 0 || control.obliquePixels < 0)
                return Blocked("Leakage fixture lacks a demonstrated visible regular-projection positive control.");
            if (control.obliquePixels > 0) return new PortalCheckDecision("Failed", "Oblique projection exposes the exit leakage marker.");
            PortalImageDifference image = baseline.comparison;
            if (image.redMae > 0.15 || image.greenMae > 0.15 || image.blueMae > 0.15 || image.maxChannelDifference > 2)
                return new PortalCheckDecision("Failed", "Baseline None RGB MAE exceeds 0.15 or maximum channel difference exceeds 2 (8-bit units).");
            // TAA и отключение эффектов остаются диагностикой, включая идеальные совпадения.
            return new PortalCheckDecision("Passed", string.Empty);
        }

        private static PortalCheckDecision Blocked(string reason) => new PortalCheckDecision("Blocked", reason);
    }
}
