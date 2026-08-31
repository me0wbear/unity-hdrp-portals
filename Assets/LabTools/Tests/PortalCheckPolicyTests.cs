using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Portals.Lab.Tests
{
    public sealed class PortalCheckPolicyTests
    {
        internal static object Call(string type, string method, params object[] args)
        {
            return LabSerializationTests.FindType("Portals.Lab.Validation." + type)
                .GetMethod(method).Invoke(null, args);
        }

        internal static object Field(object value, string field) => value.GetType().GetField(field).GetValue(value);

        private static void Status(object decision, string expected)
        {
            Assert.That(Field(decision, "status").ToString(), Is.EqualTo(expected));
            Assert.That((string)Field(decision, "failureReason"),
                expected == "Passed" ? Is.Empty : Is.Not.Empty);
        }

        private static Dictionary<string, Color> Samples(float delta = 0.0003f) => new Dictionary<string, Color>
        {
            { "farThrough", Color.red }, { "farDirect", Color.blue },
            { "crossBefore", new Color(0.3f, 0.4f, 0.5f) },
            { "crossAfter", new Color(0.3f + delta, 0.4f, 0.5f) }
        };

        [TestCase(0.0003f, "Passed")]
        [TestCase(0.00099f, "Passed")]
        [TestCase(0.0011f, "Failed")]
        [TestCase(float.NaN, "Failed")]
        [TestCase(float.PositiveInfinity, "Failed")]
        public void ColorGatesRawRgbCrossDeltaAndKeepsFarDiagnostic(float delta, string status)
        {
            Status(Call("PortalCheckPolicy", "Color", Samples(delta), 4, false), status);
        }

        [Test]
        public void ColorRejectsMissingCrossSample()
        {
            var samples = Samples();
            samples.Remove("crossAfter");
            Status(Call("PortalCheckPolicy", "Color", samples, 4, false), "Failed");
        }

        [Test]
        public void ColorRejectsIncompleteApproachCapture() =>
            Status(Call("PortalCheckPolicy", "Color", Samples(), 8, false), "Failed");

        [Test]
        public void ColorRejectsCaptureFailure() =>
            Status(Call("PortalCheckPolicy", "Color", Samples(), 4, true), "Failed");

        [Test]
        public void ColorRejectsEmptySamples() =>
            Status(Call("PortalCheckPolicy", "Color", new Dictionary<string, Color>(), 0, false), "Failed");

        [TestCase(0, 2, "Failed")]
        [TestCase(2, 2, "Failed")]
        [TestCase(1, 0, "Failed")]
        [TestCase(1, 4, "Failed")]
        [TestCase(1, 2, "Blocked")]
        public void SeamRequiresExactlyOneCrossingAndAdjacentSamples(int count, int frame, string status)
        {
            Status(Call("PortalCheckPolicy", "Seam", new double[] { double.NaN, 0.01, 0.04, 0.01, 0.01 },
                new double[] { 0.2, 0.2, 0.3, 0.2, 0.2 }, count, frame), status);
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public void SeamRejectsInvalidMetric(double value) =>
            Status(Call("PortalCheckPolicy", "Seam", new double[] { double.NaN, 0.01, value, 0.01, 0.01 },
                new double[] { 0.2, 0.2, 0.3, 0.2, 0.2 }, 1, 2), "Failed");

        [Test]
        public void SeamRejectsNoSamples() => Status(Call("PortalCheckPolicy", "Seam",
            new double[0], new double[0], 1, 2), "Failed");

        internal static object Identity()
        {
            Type type = LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckIdentity");
            object identity = Activator.CreateInstance(type);
            foreach (var pair in new Dictionary<string, string>
            {
                { "check", "Color" }, { "commit", "b50fb09d3c4db024443562dc350ac10f7b4669a2" },
                { "projectPath", Path.GetFullPath(Path.Combine(Application.dataPath, "..")).TrimEnd('\\', '/') },
                { "runId", "task0b-test" },
                { "outputDirectory", Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/task0b-test")) },
                { "sourceDigest", new string('a', 64) }, { "unityVersion", Application.unityVersion },
                { "hdrpVersion", "17.5.0" }
            }) type.GetField(pair.Key).SetValue(identity, pair.Value);
            return identity;
        }

        [TestCase("commit", "short-sha")]
        [TestCase("projectPath", "relative/project")]
        [TestCase("runId", "")]
        [TestCase("outputDirectory", "relative/output")]
        [TestCase("sourceDigest", "")]
        public void IdentityRejectsUnattributableBuild(string field, string value)
        {
            object identity = Identity();
            identity.GetType().GetField(field).SetValue(identity, value);
            Assert.That(identity.GetType().GetMethod("Validate").Invoke(identity, null), Is.Not.EqualTo(""));
        }

        [Test]
        public void RuntimeExpectedIdentityRejectsOldBinaryRunId()
        {
            object identity = Identity();
            var expected = new Dictionary<string, string> { { "PORTAL_CHECK_RUN_ID", "new-run" } };
            Assert.That(identity.GetType().GetMethod("ValidateExpected").Invoke(identity, new object[] { expected }),
                Is.Not.EqualTo(""));
        }

        [Test]
        public void RuntimeCanUseEmbeddedIdentityWithoutEnvironment()
        {
            object identity = Identity();
            Assert.That(identity.GetType().GetMethod("ValidateExpected").Invoke(identity,
                new object[] { new Dictionary<string, string>() }), Is.EqualTo(""));
        }

        [TestCase("PORTAL_CHECK_NAME", "Seam")]
        [TestCase("PORTAL_CHECK_COMMIT", "0000000000000000000000000000000000000000")]
        [TestCase("PORTAL_CHECK_PROJECT", "C:/another/project")]
        [TestCase("PORTAL_CHECK_OUTPUT", "C:/another/output")]
        public void RuntimeRejectsEveryMismatchedExpectedField(string key, string value)
        {
            object identity = Identity();
            Assert.That(identity.GetType().GetMethod("ValidateExpected").Invoke(identity,
                new object[] { new Dictionary<string, string> { { key, value } } }), Is.Not.EqualTo(""));
        }

        [Test]
        public void LegacyCheckCannotBeCertifiedByGenericCompletion()
        {
            object identity = Identity();
            identity.GetType().GetField("check").SetValue(identity, "Ghost");
            object session = Activator.CreateInstance(
                LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckSession"), identity);
            Status(session.GetType().GetMethod("TryComplete").Invoke(session,
                new object[] { "Ghost", "Passed", 8, 0, "" }), "Blocked");
        }

        [TestCase("Color", "Passed", 8, 0, "", "Passed")]
        [TestCase("Seam", "Passed", 8, 0, "", "Failed")]
        [TestCase("Color", "Passed", 0, 0, "", "Failed")]
        [TestCase("Color", "Passed", 8, -1, "", "Failed")]
        [TestCase("Color", "Failed", 8, 0, "", "Failed")]
        [TestCase("Color", "Blocked", 8, 0, "threshold not calibrated", "Blocked")]
        public void CompletionEnforcesContractAndEmitsOnlyOnce(string check, string requested,
            int frames, int crossings, string reason, string expected)
        {
            object session = Activator.CreateInstance(
                LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckSession"), Identity());
            var method = session.GetType().GetMethod("TryComplete");
            object[] args = { check, requested, frames, crossings, reason };
            object result = method.Invoke(session, args);
            Status(result, expected);
            Assert.That(Field(result, "completed"), Is.True);
            Assert.That(method.Invoke(session, args), Is.Null);
        }

        [Test]
        public void PendingRuntimeErrorCannotBecomePassed()
        {
            object session = Activator.CreateInstance(
                LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckSession"), Identity());
            session.GetType().GetMethod("RecordFailure").Invoke(session, new object[] { "capture failed" });
            Status(session.GetType().GetMethod("TryComplete").Invoke(session,
                new object[] { "Color", "Passed", 8, 0, "" }), "Failed");
        }

        [TestCase("Color", true)]
        [TestCase("Seam", true)]
        [TestCase("Ghost", false)]
        [TestCase("", false)]
        public void CertifiedBuildOptionsInvalidateCachedSceneIdentity(string check, bool clean)
        {
            Type type = LabSerializationTests.FindType("PortalCheckBuildIdentity");
            var options = new BuildPlayerOptions { options = BuildOptions.Development };
            var prepared = (BuildPlayerOptions)type.GetMethod("PrepareOptions").Invoke(null,
                new object[] { options, check });
            Assert.That((prepared.options & BuildOptions.CleanBuildCache) != 0, Is.EqualTo(clean));
            Assert.That((prepared.options & BuildOptions.Development) != 0, Is.True);
        }
    }
}
