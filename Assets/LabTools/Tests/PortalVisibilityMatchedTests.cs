using System;
using NUnit.Framework;

namespace Portals.Lab.Tests
{
    public sealed class PortalVisibilityMatchedTests
    {
        private static object Make(string type, params object[] fields) => SandboxCheckPolicyTests.Make(type, fields);
        private static object Field(object value, string name) => PortalCheckPolicyTests.Field(value, name);
        private static void Set(object value, string name, object data) => SandboxCheckPolicyTests.Set(value, name, data);
        private static object Difference(double mae = 0, int max = 0) => Make("PortalImageDifference",
            "redMae", mae, "greenMae", mae, "blueMae", mae, "maxChannelDifference", max, "pixelCount", 102400);

        private static object Evidence()
        {
            string[] modes = { "a-reference-r1", "a-visible", "a-reference-r2", "a-shallow",
                "b-reference-r1", "b-visible", "b-reference-r2", "b-shallow",
                "reentry-r1-visible", "reentry-r1-hidden", "reentry-r1-first", "reentry-r1-settled",
                "reentry-o-visible", "reentry-o-hidden", "reentry-o-first", "reentry-o-settled",
                "reentry-r2-visible", "reentry-r2-hidden", "reentry-r2-first", "reentry-r2-settled",
                "cold", "roots", "priority", "recursion", "starved", "return",
                "parented-reference-r1", "parented-visible", "parented-reference-r2", "parented-no-view" };
            int[][] counts = { new[]{5}, new[]{5}, new[]{5}, new[]{1}, new[]{5}, new[]{5}, new[]{5}, new[]{1},
                new[]{5}, new[]{0}, new[]{5}, new[]{5}, new[]{5}, new[]{0}, new[]{5}, new[]{5},
                new[]{5}, new[]{0}, new[]{5}, new[]{5}, new[]{0,0,0}, new[]{1,1,1}, new[]{0,0,1},
                new[]{1,1,2}, new[]{0,0,0}, new[]{1,1,1}, new[]{3}, new[]{1}, new[]{3}, new[]{0} };
            int[] ticks = { 40,40,40,40,40,40,40,40,40,44,45,84,40,44,45,84,40,44,45,84,4,14,24,34,38,39,40,40,40,40 };
            Array samples = Array.CreateInstance(LabSerializationTests.FindType("Portals.Lab.Validation.PortalVisibilitySample"), modes.Length);
            for (int i = 0; i < modes.Length; i++) samples.SetValue(Make("PortalVisibilitySample", "mode", modes[i],
                "mainCallbacks", 1, "virtualCallbacks", counts[i], "bindingsValid", true, "capacityValid", true,
                "historyValid", true, "clockValid", true, "completedMainRenders", ticks[i],
                "cameraMetadata", Cameras(counts[i], ticks[i], i)), i);
            string[] names = { "static-a", "static-b", "reentry-visible", "reentry-first", "reentry-settled", "parented" };
            Array triples = Array.CreateInstance(LabSerializationTests.FindType("Portals.Lab.Validation.PortalVisibilityTriple"), names.Length);
            for (int i = 0; i < names.Length; i++) triples.SetValue(Make("PortalVisibilityTriple", "name", names[i],
                "referenceRepeat", Difference(), "optimizedVsR1", Difference(), "optimizedVsR2", Difference()), i);
            return Make("PortalVisibilityMatchedEvidence", "samples", samples, "triples", triples,
                "aShallow", Difference(0.5, 16), "bShallow", Difference(0.5, 16), "parentedPositive", Difference(0.5, 16));
        }

        private static object Triple(object e, int index) => ((Array)Field(e, "triples")).GetValue(index);
        private static object Sample(object e, int index) => ((Array)Field(e, "samples")).GetValue(index);
        private static float[] Identity() => new float[]{1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1};
        private static Array Cameras(int[] counts, int tick, int sample)
        {
            int count = 1;
            foreach (int n in counts) count += n;
            Array rows = Array.CreateInstance(LabSerializationTests.FindType("Portals.Lab.Validation.PortalVisibilityCameraSample"), count);
            object Camera(int root, int level, uint before, int completed) => Make("PortalVisibilityCameraSample",
                "main", root == -1, "enabled", true, "root", root, "level", level,
                "cameraId", root + "/" + level, "targetId", "target", "historyBefore", before,
                "historyAfter", before + 1, "completedRenders", completed,
                "position", new float[]{1,2,3}, "rotation", new float[]{0,0,0,1},
                "view", Identity(), "projection", Identity(), "nonJitteredProjection", Identity());
            rows.SetValue(Camera(-1, -1, (uint)(tick - 1), tick), 0);
            int index = 1;
            for (int root = 0; root < counts.Length; root++)
                for (int level = 0; level < counts[root]; level++)
                {
                    bool first = sample == 10 || sample == 14 || sample == 18;
                    bool settled = sample == 11 || sample == 15 || sample == 19;
                    uint before = first || sample == 25 ? 0u : 39u;
                    rows.SetValue(Camera(root, level, before, first ? 41 : settled ? 80 : 40), index++);
                }
            return rows;
        }
        private static object CameraRow(object e, int sample, int row) => ((Array)Field(Sample(e, sample), "cameraMetadata")).GetValue(row);
        private static string Status(object evidence) => (string)Field(
            LabSerializationTests.FindType("Portals.Lab.Validation.PortalVisibilityPolicy").GetMethod("EvaluateMatched")
                .Invoke(null, new[] { evidence, "" }), "status");

        [Test] public void CompleteMatchedControlsCanPass() => Assert.That(Status(Evidence()), Is.EqualTo("Passed"));

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void NonexactRepeatBlocksInsteadOfCertifyingOrBlamingCulling(int index)
        {
            object e = Evidence();
            Set(Triple(e, index), "referenceRepeat", Difference(1.0 / 102400, 1));
            Set(Triple(e, index), "optimizedVsR1", Difference(1.0 / 102400, 1));
            Assert.That(Status(e), Is.EqualTo("Blocked"));
        }

        [TestCase(0, "optimizedVsR1")] [TestCase(1, "optimizedVsR2")]
        [TestCase(3, "optimizedVsR1")] [TestCase(4, "optimizedVsR2")] [TestCase(5, "optimizedVsR2")]
        public void OneByteAgainstRepeatableReferenceStillFails(int index, string comparison)
        {
            object e = Evidence();
            Set(Triple(e, index), comparison, Difference(1.0 / 102400, 1));
            Assert.That(Status(e), Is.EqualTo("Failed"));
        }

        [Test]
        public void RepeatNoiseDoesNotHideASeparatelyProvenRegression()
        {
            object e = Evidence();
            Set(Triple(e, 0), "referenceRepeat", Difference(0.01, 2));
            Set(Triple(e, 1), "optimizedVsR1", Difference(0.01, 2));
            Assert.That(Status(e), Is.EqualTo("Failed"));
        }

        [TestCase("bindingsValid")] [TestCase("historyValid")] [TestCase("capacityValid")]
        public void RepeatNoiseDoesNotHideLifecycleFailures(string field)
        {
            object e = Evidence();
            Set(Triple(e, 0), "referenceRepeat", Difference(0.01, 2));
            Set(Sample(e, 14), field, false);
            Assert.That(Status(e), Is.EqualTo("Failed"));
        }

        [TestCase("aShallow", 0.49, 16)] [TestCase("bShallow", 0.5, 15)]
        [TestCase("parentedPositive", 0.0, 0)]
        public void PositiveControlsRetainBothThresholds(string field, double mae, int max)
        {
            object e = Evidence(); Set(e, field, Difference(mae, max));
            Assert.That(Status(e), Is.EqualTo("Blocked"));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void MissingInvalidOrMismatchedArmsCannotPass(int kind)
        {
            object e = Evidence();
            if (kind == 0) Set(Triple(e, 0), "optimizedVsR2", null);
            if (kind == 1) Set(Triple(e, 4), "referenceRepeat", Difference(double.NaN));
            if (kind == 2) Set(Sample(e, 1), "completedMainRenders", 41);
            if (kind == 3) Set(Sample(e, 14), "clockValid", false);
            if (kind == 4) Set(Sample(e, 18), "mode", "reentry-r1-first");
            if (kind == 5) Set(Triple(e, 5), "name", "static-a");
            Assert.That(Status(e), Is.EqualTo("Blocked"));
        }

        [TestCase(21, 3, 0, 0)] [TestCase(22, 1, 0, 0)]
        public void MatchedBudgetControlsStillRequirePerRootAllocation(int sample, int a, int b, int c)
        {
            object e = Evidence(); Set(Sample(e, sample), "virtualCallbacks", new[]{a,b,c});
            Assert.That(Status(e), Is.EqualTo("Failed"));
        }

        [Test]
        public void ParentedControlMustActuallyReduceThreeViewsToOne()
        {
            object e = Evidence(); Set(Sample(e, 27), "virtualCallbacks", new[]{3});
            Assert.That(Status(e), Is.EqualTo("Failed"));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)] [TestCase(4)] [TestCase(5)]
        public void MissingOrMismatchedMandatoryCameraMetadataBlocks(int kind)
        {
            object e = Evidence();
            if (kind == 0) Set(Sample(e, 1), "cameraMetadata", null);
            if (kind == 1) Set(CameraRow(e, 1, 1), "historyBefore", 38u);
            if (kind == 2) Set(CameraRow(e, 1, 0), "historyAfter", 41u);
            if (kind == 3) Set(CameraRow(e, 14, 1), "completedRenders", 40);
            if (kind == 4) Set(CameraRow(e, 1, 0), "position", new float[]{1,2,float.NaN});
            if (kind == 5) Set(CameraRow(e, 1, 1), "projection", new float[16]);
            Assert.That(Status(e), Is.EqualTo("Blocked"));
        }

        [Test]
        public void EqualCompletedCountsDoNotExcuseDifferentHistoryEpochs()
        {
            object e = Evidence();
            Set(CameraRow(e, 1, 1), "historyBefore", 79u);
            Set(CameraRow(e, 1, 1), "historyAfter", 80u);
            Assert.That(Status(e), Is.EqualTo("Blocked"));
        }

        [Test]
        public void AbsoluteTimeAndUnconsumedParentedChildrenAreNotMatchedInputs()
        {
            object e = Evidence();
            Set(Sample(e, 1), "unityFrame", 900);
            Set(Sample(e, 1), "time", 30d);
            Set(CameraRow(e, 26, 2), "historyBefore", 79u);
            Set(CameraRow(e, 26, 2), "historyAfter", 80u);
            Assert.That(Status(e), Is.EqualTo("Passed"));
        }

        [Test]
        public void ClockCountsCompletedEventsNotFrameGapsAndRejectsDuplicates()
        {
            Type type = LabSerializationTests.FindType("Portals.Lab.Validation.PortalVisibilityRenderClock");
            object clock = Activator.CreateInstance(type);
            void Complete(int frame) => type.GetMethod("Complete").Invoke(clock, new object[]{frame});
            object Value(string name) => type.GetProperty(name).GetValue(clock);
            Complete(100); Complete(107);
            Assert.That(Value("Completed"), Is.EqualTo(2));
            Complete(107);
            Assert.That(Value("Completed"), Is.EqualTo(2));
            Assert.That(Value("Valid"), Is.EqualTo(false));
            type.GetMethod("BeginArm").Invoke(clock, null);
            Assert.That(Value("Completed"), Is.EqualTo(0));
            Complete(110);
            Assert.That(Value("Completed"), Is.EqualTo(1));
            Assert.That(Value("Valid"), Is.EqualTo(true));
        }
    }
}
