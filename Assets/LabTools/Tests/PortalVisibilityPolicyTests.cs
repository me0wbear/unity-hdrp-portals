using System;
using NUnit.Framework;

namespace Portals.Lab.Tests
{
    public sealed class PortalVisibilityPolicyTests
    {
        private static object Make(string type, params object[] fields) => SandboxCheckPolicyTests.Make(type, fields);
        private static object Difference(double mae = 0, int max = 0) => Make("PortalImageDifference",
            "redMae", mae, "greenMae", mae, "blueMae", mae, "maxChannelDifference", max, "pixelCount", 102400);

        private static object Evidence()
        {
            string[] modes = { "a-reference", "a-shallow", "a-visible", "hidden", "reentry-first", "reentry-settled",
                "b-reference", "b-shallow", "b-visible", "cold", "roots", "priority", "recursion", "starved", "return" };
            int[][] counts = { new[]{5}, new[]{1}, new[]{5}, new[]{0}, new[]{5}, new[]{5}, new[]{5}, new[]{1}, new[]{5},
                new[]{0,0,0}, new[]{1,1,1}, new[]{0,0,1}, new[]{1,1,2}, new[]{0,0,0}, new[]{1,1,1} };
            Array samples = Array.CreateInstance(LabSerializationTests.FindType("Portals.Lab.Validation.PortalVisibilitySample"), modes.Length);
            for (int i = 0; i < modes.Length; i++) samples.SetValue(Make("PortalVisibilitySample", "mode", modes[i],
                "mainCallbacks", 1, "virtualCallbacks", counts[i], "bindingsValid", true, "capacityValid", true, "historyValid", true), i);
            return Make("PortalVisibilityEvidence", "samples", samples, "aReference", Difference(), "bReference", Difference(),
                "aShallow", Difference(1, 32), "bShallow", Difference(1, 32), "reentrySettled", Difference());
        }

        private static string Status(object evidence) => (string)PortalCheckPolicyTests.Field(
            LabSerializationTests.FindType("Portals.Lab.Validation.PortalVisibilityPolicy").GetMethod("Evaluate")
                .Invoke(null, new[] { evidence, "" }), "status");

        [Test] public void CompleteIndependentEvidenceCanPass() => Assert.That(Status(Evidence()), Is.EqualTo("Passed"));

        [TestCase("bindingsValid")]
        [TestCase("capacityValid")]
        [TestCase("historyValid")]
        public void LifecycleViolationsFail(string field)
        {
            object evidence = Evidence();
            var samples = (Array)PortalCheckPolicyTests.Field(evidence, "samples");
            SandboxCheckPolicyTests.Set(samples.GetValue(4), field, false);
            Assert.That(Status(evidence), Is.EqualTo("Failed"));
        }

        [Test] public void GreedyBudgetFailsDespiteCorrectTotal()
        {
            object evidence = Evidence();
            var samples = (Array)PortalCheckPolicyTests.Field(evidence, "samples");
            SandboxCheckPolicyTests.Set(samples.GetValue(10), "virtualCallbacks", new[]{3,0,0});
            Assert.That(Status(evidence), Is.EqualTo("Failed"));
        }

        [Test] public void MainCallbackIsRequired()
        {
            object evidence = Evidence();
            var samples = (Array)PortalCheckPolicyTests.Field(evidence, "samples");
            SandboxCheckPolicyTests.Set(samples.GetValue(9), "mainCallbacks", 0);
            Assert.That(Status(evidence), Is.EqualTo("Failed"));
        }

        [TestCase("aReference")]
        [TestCase("bReference")]
        [TestCase("reentrySettled")]
        public void ChangedReferencePixelsFail(string field)
        {
            object evidence = Evidence();
            SandboxCheckPolicyTests.Set(evidence, field, Difference(1.0 / 102400, 1));
            Assert.That(Status(evidence), Is.EqualTo("Failed"));
        }

        [TestCase("aShallow")]
        [TestCase("bShallow")]
        public void InvisibleDepthControlBlocksCertification(string field)
        {
            object evidence = Evidence();
            SandboxCheckPolicyTests.Set(evidence, field, Difference());
            Assert.That(Status(evidence), Is.EqualTo("Blocked"));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void MissingDuplicateOrInvalidEvidenceCannotPass(int kind)
        {
            object evidence = Evidence();
            var samples = (Array)PortalCheckPolicyTests.Field(evidence, "samples");
            if (kind == 0) samples.SetValue(null, 14);
            if (kind == 1) samples.SetValue(samples.GetValue(0), 14);
            if (kind == 2) SandboxCheckPolicyTests.Set(evidence, "aReference", Difference(double.NaN));
            Assert.That(Status(evidence), Is.EqualTo("Blocked"));
        }
    }
}
