using System;
using System.Collections.Generic;

namespace Portals.Lab.Validation
{
    public sealed class PortalPerformanceSample
    {
        public int round;
        public string mode;
        public int warmupFrames;
        public int frameSamples;
        public double? frameMedianMs;
        public bool cameraObserved;
        public int mainCameras;
        public int virtualCameras;
        public int aovRequests;
        public long targetPixels;
        public int aovExecutionSamples;
        public double? aovExecutionsMax;
    }

    public static class PortalPerformancePolicy
    {
        public static readonly string[] Modes = { "off", "depth2", "depth0", "depth2-no-aov", "depth0-no-aov", "depth2-divider2", "behind" };

        public static PortalCheckDecision Evaluate(PortalPerformanceSample[] samples, PortalImageDifference[] roundRoi, string fixtureFailure)
        {
            if (!string.IsNullOrEmpty(fixtureFailure)) return Blocked(fixtureFailure);
            if (samples == null || samples.Length != 14 || roundRoi == null || roundRoi.Length != 2)
                return Blocked("Performance requires seven modes in each of two rounds and both ROI comparisons.");
            var keys = new HashSet<string>();
            foreach (PortalPerformanceSample sample in samples)
            {
                if (sample == null || sample.round < 0 || sample.round > 1 || Array.IndexOf(Modes, sample.mode) < 0
                    || !keys.Add(sample.round + "/" + sample.mode) || sample.warmupFrames != 180 || sample.frameSamples != 360
                    || !sample.frameMedianMs.HasValue || !PortalCheckPolicy.Finite(sample.frameMedianMs.Value) || sample.frameMedianMs.Value <= 0
                    || !sample.cameraObserved || sample.mainCameras < 0 || sample.virtualCameras < 0 || sample.aovRequests < 0)
                    return Blocked("Performance modes, frame samples, or camera observations are incomplete.");
            }
            foreach (PortalImageDifference image in roundRoi)
                if (image == null || !image.IsValid(55100)) return Blocked("Performance depth2/depth0 ROI evidence is incomplete.");
            foreach (PortalImageDifference image in roundRoi)
                if (image.redMae != 0 || image.greenMae != 0 || image.blueMae != 0 || image.maxChannelDifference != 0)
                    return Failed("Depth2 and depth0 differ in the fixed 190x290 ROI.");
            bool missingExecutions = false;
            foreach (PortalPerformanceSample sample in samples)
            {
                bool control = sample.mode == "off" || sample.mode == "behind";
                if (!control && sample.mode != "depth2") continue;
                if (sample.mainCameras != 1 || sample.virtualCameras != (control ? 0 : 1) || sample.aovRequests != 0)
                    return Failed("Default requires one main, one virtual camera and no AOV; off/behind require only one main camera.");
                if (sample.aovExecutionSamples <= 0 || !sample.aovExecutionsMax.HasValue
                    || !PortalCheckPolicy.Finite(sample.aovExecutionsMax.Value) || sample.aovExecutionsMax.Value < 0)
                    missingExecutions = true;
                else if (sample.aovExecutionsMax.Value != 0) return Failed("AOV executions remain in default or off/behind control.");
                else if (sample.aovExecutionSamples != 360) missingExecutions = true;
            }
            return missingExecutions ? Blocked("AOV execution counter is unavailable; request/camera counts cannot prove zero executions.")
                : new PortalCheckDecision("Passed", string.Empty);
        }

        private static PortalCheckDecision Blocked(string reason) => new PortalCheckDecision("Blocked", reason);
        private static PortalCheckDecision Failed(string reason) => new PortalCheckDecision("Failed", reason);
    }
}
