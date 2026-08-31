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
        public int completedMainRenders, unityFrame;
        public bool clockValid;
        public double time, unscaledTime;
        public float deltaTime, smoothDeltaTime;
        public PortalVisibilityCameraSample[] cameraMetadata;
    }

    [Serializable]
    public sealed class PortalVisibilityCameraSample
    {
        public string cameraId, targetId;
        public int root, level, completedRenders;
        public bool main, enabled, motionBlur, aoTemporal, dithering, filmGrain;
        public uint historyBefore, historyAfter;
        public float hdrpTime;
        public float[] position, rotation, view, projection, nonJitteredProjection;
    }

    [Serializable]
    public sealed class PortalVisibilityTriple
    {
        public string name;
        public PortalImageDifference referenceRepeat, optimizedVsR1, optimizedVsR2;
    }

    [Serializable]
    public sealed class PortalVisibilityMatchedEvidence
    {
        public int schemaVersion = 2;
        public PortalVisibilitySample[] samples;
        public PortalVisibilityTriple[] triples;
        public PortalImageDifference aShallow, bShallow, parentedPositive;
    }

    // Считает завершённые main renders, а не coroutine yields и разрывы Unity frame count.
    public sealed class PortalVisibilityRenderClock
    {
        private int lastFrame = -1;
        public int Completed { get; private set; }
        public bool Valid { get; private set; } = true;
        public void BeginArm() { Completed = 0; Valid = true; }
        public void Complete(int frame)
        {
            if (frame <= lastFrame) { Valid = false; return; }
            lastFrame = frame;
            Completed++;
        }
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

        public static readonly string[] MatchedModes = {
            "a-reference-r1", "a-visible", "a-reference-r2", "a-shallow",
            "b-reference-r1", "b-visible", "b-reference-r2", "b-shallow",
            "reentry-r1-visible", "reentry-r1-hidden", "reentry-r1-first", "reentry-r1-settled",
            "reentry-o-visible", "reentry-o-hidden", "reentry-o-first", "reentry-o-settled",
            "reentry-r2-visible", "reentry-r2-hidden", "reentry-r2-first", "reentry-r2-settled",
            "cold", "roots", "priority", "recursion", "starved", "return",
            "parented-reference-r1", "parented-visible", "parented-reference-r2", "parented-no-view" };

        // Версия 1 выше сохраняет контракт архивного evidence и его исходных тестов.
        public static PortalCheckDecision EvaluateMatched(PortalVisibilityMatchedEvidence evidence, string problem)
        {
            if (!string.IsNullOrEmpty(problem)) return Decision("Blocked", problem);
            if (evidence?.samples == null || evidence.samples.Length != MatchedModes.Length)
                return Decision("Blocked", "Matched Visibility requires every named control.");
            int[][] expected = { new[]{5}, new[]{5}, new[]{5}, new[]{1}, new[]{5}, new[]{5}, new[]{5}, new[]{1},
                new[]{5}, new[]{0}, new[]{5}, new[]{5}, new[]{5}, new[]{0}, new[]{5}, new[]{5},
                new[]{5}, new[]{0}, new[]{5}, new[]{5}, new[]{0,0,0}, new[]{1,1,1}, new[]{0,0,1},
                new[]{1,1,2}, new[]{0,0,0}, new[]{1,1,1}, new[]{3}, new[]{1}, new[]{3}, new[]{0} };
            int[] ticks = { 40,40,40,40,40,40,40,40,40,44,45,84,40,44,45,84,40,44,45,84,4,14,24,34,38,39,40,40,40,40 };
            for (int i = 0; i < MatchedModes.Length; i++)
            {
                PortalVisibilitySample sample = evidence.samples[i];
                if (sample == null || sample.mode != MatchedModes[i] || sample.virtualCallbacks == null
                    || sample.virtualCallbacks.Length != expected[i].Length)
                    return Decision("Blocked", "Missing, duplicate, or out-of-order matched observation.");
                if (sample.mainCallbacks != 1 || !sample.bindingsValid || !sample.capacityValid || !sample.historyValid)
                    return Decision("Failed", sample.mode + ": callback, binding, capacity, or history invariant failed.");
                for (int root = 0; root < expected[i].Length; root++)
                    if (sample.virtualCallbacks[root] != expected[i][root])
                        return Decision("Failed", sample.mode + ": actual per-root callbacks differ from the contract.");
                if (!sample.clockValid || sample.completedMainRenders != ticks[i])
                    return Decision("Blocked", sample.mode + ": completed-render clock does not match the arm.");
                if (!ValidMetadata(sample))
                    return Decision("Blocked", sample.mode + ": mandatory main/virtual camera metadata is missing or inconsistent.");
            }
            int[][] arms = { new[]{0,1,2}, new[]{4,5,6}, new[]{8,12,16},
                new[]{10,14,18}, new[]{11,15,19}, new[]{26,27,28} };
            foreach (int[] arm in arms)
                if (!MatchedMetadata(evidence.samples[arm[0]], evidence.samples[arm[1]], evidence.samples[arm[2]]))
                    return Decision("Blocked", evidence.samples[arm[1]].mode + ": common active camera histories, render counts, pose or matrices are not matched.");
            string[] names = { "static-a", "static-b", "reentry-visible", "reentry-first", "reentry-settled", "parented" };
            if (evidence.triples == null || evidence.triples.Length != names.Length)
                return Decision("Blocked", "Matched Visibility requires static, trajectory, and parented triples.");
            for (int i = 0; i < names.Length; i++)
            {
                PortalVisibilityTriple triple = evidence.triples[i];
                if (triple == null || triple.name != names[i] || !Valid(triple.referenceRepeat)
                    || !Valid(triple.optimizedVsR1) || !Valid(triple.optimizedVsR2))
                    return Decision("Blocked", "Every triple requires finite 320x320 RGB comparisons against both references.");
            }
            foreach (PortalImageDifference positive in new[]{evidence.aShallow, evidence.bShallow, evidence.parentedPositive})
                if (!Valid(positive) || positive.maxChannelDifference < 16
                    || (positive.redMae + positive.greenMae + positive.blueMae) / 3 < 0.5)
                    return Decision("Blocked", "Depth0 or parented no-view positive control does not demonstrate visible content.");
            string unresolved = null;
            foreach (PortalVisibilityTriple triple in evidence.triples)
            {
                if (!Exact(triple.referenceRepeat))
                {
                    unresolved = unresolved ?? triple.name + ": reference repeat is nonexact; temporal/lifecycle cause remains unresolved.";
                    continue;
                }
                if (!Exact(triple.optimizedVsR1) || !Exact(triple.optimizedVsR2))
                    return Decision("Failed", triple.name + ": optimized pixels differ from repeatable full-prefix references.");
            }
            return unresolved == null ? Decision("Passed", string.Empty) : Decision("Blocked", unresolved);
        }

        private static bool Valid(PortalImageDifference image) => image != null && image.IsValid(102400);
        private static bool ValidMetadata(PortalVisibilitySample sample)
        {
            if (sample.cameraMetadata == null) return false;
            int main = 0;
            int[] levels = new int[sample.virtualCallbacks.Length];
            foreach (PortalVisibilityCameraSample camera in sample.cameraMetadata)
            {
                if (camera == null) return false;
                if (!camera.enabled) continue;
                if (string.IsNullOrEmpty(camera.cameraId) || !Finite(camera.position, 3) || !Finite(camera.rotation, 4)
                    || !Finite(camera.view, 16) || !Finite(camera.projection, 16) || !Finite(camera.nonJitteredProjection, 16)
                    || camera.historyBefore == uint.MaxValue || camera.historyAfter != camera.historyBefore + 1
                    || camera.completedRenders <= 0) return false;
                int sameKey = 0;
                foreach (PortalVisibilityCameraSample other in sample.cameraMetadata)
                    if (other != null && other.enabled && other.main == camera.main
                        && other.root == camera.root && other.level == camera.level) sameKey++;
                if (sameKey != 1) return false;
                if (camera.main)
                {
                    if (camera.root != -1 || camera.level != -1 || camera.completedRenders != sample.completedMainRenders) return false;
                    main++;
                }
                else
                {
                    if (camera.root < 0 || camera.root >= levels.Length || camera.level < 0
                        || camera.level >= sample.virtualCallbacks[camera.root] || string.IsNullOrEmpty(camera.targetId)) return false;
                    levels[camera.root]++;
                }
            }
            if (main != 1) return false;
            for (int root = 0; root < levels.Length; root++)
                if (levels[root] != sample.virtualCallbacks[root]) return false;
            return true;
        }

        private static bool MatchedMetadata(PortalVisibilitySample r1, PortalVisibilitySample optimized, PortalVisibilitySample r2)
        {
            // Сравниваем только общие активные уровни: у parented optimized нет child 1/2.
            foreach (PortalVisibilityCameraSample camera in optimized.cameraMetadata)
            {
                if (!camera.enabled) continue;
                PortalVisibilityCameraSample first = ActiveCamera(r1, camera);
                PortalVisibilityCameraSample repeat = ActiveCamera(r2, camera);
                if (!SameCameraInputs(camera, first) || !SameCameraInputs(camera, repeat)) return false;
            }
            return true;
        }

        private static PortalVisibilityCameraSample ActiveCamera(PortalVisibilitySample sample, PortalVisibilityCameraSample key)
        {
            foreach (PortalVisibilityCameraSample camera in sample.cameraMetadata)
                if (camera.enabled && camera.main == key.main && camera.root == key.root && camera.level == key.level) return camera;
            return null;
        }

        private static bool SameCameraInputs(PortalVisibilityCameraSample a, PortalVisibilityCameraSample b) =>
            b != null && a.historyBefore == b.historyBefore && a.historyAfter == b.historyAfter
            && a.completedRenders == b.completedRenders && Same(a.position, b.position) && Same(a.rotation, b.rotation)
            && Same(a.view, b.view) && Same(a.projection, b.projection) && Same(a.nonJitteredProjection, b.nonJitteredProjection);

        private static bool Finite(float[] values, int length)
        {
            if (values == null || values.Length != length) return false;
            foreach (float value in values) if (!PortalCheckPolicy.Finite(value)) return false;
            return true;
        }
        private static bool Same(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }
        private static bool Exact(PortalImageDifference image) => image.maxChannelDifference == 0
            && image.redMae == 0 && image.greenMae == 0 && image.blueMae == 0;
        private static PortalCheckDecision Decision(string status, string reason) => new PortalCheckDecision(status, reason);
    }
}
