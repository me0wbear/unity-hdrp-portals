using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Portals.Lab.Tests
{
    public sealed class SandboxCheckPolicyTests
    {
        internal static object Make(string name, params object[] fields)
        {
            object value = Activator.CreateInstance(LabSerializationTests.FindType("Portals.Lab.Validation." + name));
            for (int i = 0; i < fields.Length; i += 2) Set(value, (string)fields[i], fields[i + 1]);
            return value;
        }

        internal static void Set(object value, string field, object content) => value.GetType().GetField(field).SetValue(value, content);
        private static object Field(object value, string field) => PortalCheckPolicyTests.Field(value, field);
        private static object Call(string type, string method, params object[] args)
        {
            var target = LabSerializationTests.FindType("Portals.Lab.Validation." + type).GetMethod(method);
            Assert.That(target, Is.Not.Null, "Required behavior: " + method);
            return target.Invoke(null, args);
        }
        private static void Status(object decision, string expected)
        {
            Assert.That(Field(decision, "status"), Is.EqualTo(expected));
            Assert.That((string)Field(decision, "failureReason"), expected == "Passed" ? Is.Empty : Is.Not.Empty);
        }

        private static object Difference(double r = 0, double g = 0, double b = 0, int max = 0, int pixels = 64000) =>
            Make("PortalImageDifference", "redMae", r, "greenMae", g, "blueMae", b, "maxChannelDifference", max, "pixelCount", pixels);

        [Test]
        public void ImageDifferenceUsesPerPixelAbsoluteRgbInByteUnits()
        {
            var a = new[] { new Color32(10, 20, 30, 0), new Color32(100, 120, 140, 255) };
            var b = new[] { new Color32(12, 19, 34, 255), new Color32(98, 125, 136, 0) };
            object result = Call("PortalImageMetrics", "Compare", a, b);
            Assert.That(Field(result, "redMae"), Is.EqualTo(2));
            Assert.That(Field(result, "greenMae"), Is.EqualTo(3));
            Assert.That(Field(result, "blueMae"), Is.EqualTo(4));
            Assert.That(Field(result, "maxChannelDifference"), Is.EqualTo(5));
        }

        [Test]
        public void AlphaDoesNotAffectImageDifference()
        {
            object result = Call("PortalImageMetrics", "Compare", new[] { new Color32(1, 2, 3, 0) }, new[] { new Color32(1, 2, 3, 255) });
            Assert.That(Field(result, "redMae"), Is.Zero);
            Assert.That(Field(result, "greenMae"), Is.Zero);
            Assert.That(Field(result, "blueMae"), Is.Zero);
            Assert.That(Field(result, "maxChannelDifference"), Is.Zero);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void ImageDifferenceRejectsMissingOrMismatchedPixels(int kind)
        {
            Color32[] a = kind == 0 ? null : kind == 1 ? new Color32[0] : new Color32[2];
            // Разрешение типа до Throws отличает ожидаемое RED от ошибки reflection fixture.
            Type type = LabSerializationTests.FindType("Portals.Lab.Validation.PortalImageMetrics");
            var error = Assert.Throws<TargetInvocationException>(() => type.GetMethod("Compare").Invoke(null, new object[] { a, new Color32[1] }));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        [TestCase(1920, 1080, 865, 420, 190, 290, 370)]
        [TestCase(1280, 720, 480, 260, 320, 200, 260)]
        public void PublishedTopLeftRoiMapsToTextureBottomLeft(int width, int height, int x, int y, int w, int h, int expectedY)
        {
            Assert.That(Call("PortalImageMetrics", "FromTopLeft", width, height, x, y, w, h), Is.EqualTo(new RectInt(x, expectedY, w, h)));
        }

        [TestCase(-1, 0, 1, 1)]
        [TestCase(0, -1, 1, 1)]
        [TestCase(0, 0, 0, 1)]
        [TestCase(0, 0, 1, 0)]
        [TestCase(9, 0, 2, 1)]
        [TestCase(0, 9, 1, 2)]
        [TestCase(int.MaxValue, 0, 2, 1)]
        public void RoiRejectsInvalidBoundsInsteadOfClamping(int x, int y, int w, int h)
        {
            Type type = LabSerializationTests.FindType("Portals.Lab.Validation.PortalImageMetrics");
            var error = Assert.Throws<TargetInvocationException>(() => type.GetMethod("FromTopLeft").Invoke(null, new object[] { 10, 10, x, y, w, h }));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }

        private static Array Parity()
        {
            Type type = LabSerializationTests.FindType("Portals.Lab.Validation.SandboxParitySample");
            Array samples = Array.CreateInstance(type, 8);
            int i = 0;
            foreach (string mode in new[] { "baseline", "ssao-off-both", "ssao-off-virtual-only", "regular-projection" })
                foreach (string aa in new[] { "none", "taa" })
                    samples.SetValue(Make("SandboxParitySample", "mode", mode, "aa", aa, "captureCount", 4,
                        "cameraSettingsValid", true, "comparison", Difference(), "repeat", Difference()), i++);
            return samples;
        }

        private static object Control(int regular = 30, int oblique = 0, bool valid = true) =>
            Make("PortalLeakageControl", "completed", true, "fixtureValid", valid, "regularPixels", regular, "obliquePixels", oblique);

        [TestCase(0.15, 2, "Passed")]
        [TestCase(0.2, 2, "Failed")]
        [TestCase(0.075, 3, "Failed")]
        [TestCase(double.NaN, 0, "Blocked")]
        [TestCase(double.PositiveInfinity, 0, "Blocked")]
        [TestCase(-0.1, 0, "Blocked")]
        public void ParityGatesBothInclusiveByteMaeAndMaximum(double mae, int maximum, string expected)
        {
            Array samples = Parity();
            Set(samples.GetValue(0), "comparison", Difference(mae, 0, 0, maximum));
            Status(Call("SandboxParityPolicy", "Evaluate", samples, Control(), ""), expected);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        public void ParityCannotPassIncompleteRequiredEvidence(int kind)
        {
            Array samples = Parity();
            if (kind == 0) samples.SetValue(null, 7);
            if (kind == 1) Set(samples.GetValue(0), "captureCount", 3);
            if (kind == 2) Set(samples.GetValue(0), "repeat", null);
            if (kind == 3) Set(samples.GetValue(0), "cameraSettingsValid", false);
            Status(Call("SandboxParityPolicy", "Evaluate", samples, Control(), ""), "Blocked");
        }

        [Test]
        public void DiagnosticsCannotRescueFailedBaseline()
        {
            Array samples = Parity();
            Set(samples.GetValue(0), "comparison", Difference(1, 1, 1, 4));
            Status(Call("SandboxParityPolicy", "Evaluate", samples, Control(), ""), "Failed");
        }

        [Test]
        public void TaaAndRegularProjectionDoNotDefineStaticAcceptance()
        {
            Array samples = Parity();
            for (int i = 1; i < 8; i++) Set(samples.GetValue(i), "comparison", Difference(10, 20, 30, 100));
            Status(Call("SandboxParityPolicy", "Evaluate", samples, Control(), ""), "Passed");
        }

        [TestCase(0, 0, true, "Blocked")]
        [TestCase(30, 0, false, "Blocked")]
        [TestCase(30, 0, true, "Passed")]
        [TestCase(30, 1, true, "Failed")]
        public void LeakageRequiresVisiblePositiveControl(int regular, int oblique, bool valid, string expected) =>
            Status(Call("SandboxParityPolicy", "Evaluate", Parity(), Control(regular, oblique, valid), ""), expected);

        internal static Array Performance()
        {
            Array samples = Array.CreateInstance(LabSerializationTests.FindType("Portals.Lab.Validation.PortalPerformanceSample"), 14);
            int i = 0;
            for (int round = 0; round < 2; round++)
                foreach (string mode in new[] { "off", "depth2", "depth0", "depth2-no-aov", "depth0-no-aov", "depth2-divider2", "behind" })
                    samples.SetValue(Make("PortalPerformanceSample", "round", round, "mode", mode, "frameSamples", 360,
                        "warmupFrames", 180, "frameMedianMs", 3.0, "cameraObserved", true, "mainCameras", 1,
                        "virtualCameras", mode == "off" || mode == "behind" ? 0 : 1,
                        "aovRequests", 0, "aovExecutionSamples", 360, "aovExecutionsMax", (double?)0), i++);
            return samples;
        }

        private static Array Roi()
        {
            Array values = Array.CreateInstance(LabSerializationTests.FindType("Portals.Lab.Validation.PortalImageDifference"), 2);
            values.SetValue(Difference(pixels: 55100), 0);
            values.SetValue(Difference(pixels: 55100), 1);
            return values;
        }

        [Test]
        public void CompleteNonredundantPerformanceFixtureCanPass() =>
            Status(Call("PortalPerformancePolicy", "Evaluate", Performance(), Roi(), ""), "Passed");

        [TestCase("virtualCameras", 3)]
        [TestCase("aovRequests", 1)]
        public void DefaultRedundantWorkFailsDespiteIdenticalRoi(string field, int value)
        {
            Array samples = Performance();
            Set(samples.GetValue(1), field, value);
            Status(Call("PortalPerformancePolicy", "Evaluate", samples, Roi(), ""), "Failed");
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        public void IncompletePerformanceCannotPass(int kind)
        {
            Array samples = Performance();
            if (kind == 0) samples.SetValue(null, 13);
            if (kind == 1) samples.SetValue(samples.GetValue(0), 7);
            if (kind == 2) Set(samples.GetValue(8), "frameSamples", 359);
            if (kind == 3) Set(samples.GetValue(8), "cameraObserved", false);
            if (kind == 4) Set(samples.GetValue(8), "warmupFrames", 179);
            Status(Call("PortalPerformancePolicy", "Evaluate", samples, Roi(), ""), "Blocked");
        }

        [TestCase(0)]
        [TestCase(6)]
        public void OffAndBehindRequireOnlyMainCamera(int index)
        {
            Array samples = Performance();
            Set(samples.GetValue(index), "virtualCameras", 1);
            Status(Call("PortalPerformancePolicy", "Evaluate", samples, Roi(), ""), "Failed");
        }

        [TestCase(null, 0, "Blocked")]
        [TestCase(0.0, 0, "Blocked")]
        [TestCase(0.0, 1, "Blocked")]
        [TestCase(0.0, 359, "Blocked")]
        [TestCase(0.0, 360, "Passed")]
        [TestCase(1.0, 360, "Failed")]
        [TestCase(1.0, 1, "Failed")]
        public void ExecutionEvidenceIsNotInferredFromRequestsOrCallbacks(double? maximum, int count, string expected)
        {
            Array samples = Performance();
            Set(samples.GetValue(1), "aovExecutionsMax", maximum);
            Set(samples.GetValue(1), "aovExecutionSamples", count);
            Status(Call("PortalPerformancePolicy", "Evaluate", samples, Roi(), ""), expected);
        }

        [Test]
        public void PerformanceRejectsChangedRoiEvenWhenCostIsIdeal()
        {
            Array roi = Roi();
            roi.SetValue(Difference(1.0 / 55100, 0, 0, 1, 55100), 1);
            Status(Call("PortalPerformancePolicy", "Evaluate", Performance(), roi, ""), "Failed");
        }

        [Test]
        public void PercentilesPreserveArchivedIndexAndInput()
        {
            double[] values = { 4, 1, 3, 2 };
            Assert.That(Call("PortalPerformanceMetrics", "Percentile", values, 0.5), Is.EqualTo(3));
            Assert.That(Call("PortalPerformanceMetrics", "Percentile", values, 0.95), Is.EqualTo(4));
            Assert.That(values, Is.EqualTo(new double[] { 4, 1, 3, 2 }));
            Assert.That(Call("PortalPerformanceMetrics", "Percentile", new double[0], 0.5), Is.Null);
        }

        [Test]
        public void CsvUsesInvariantUnitsAndLiteralNull()
        {
            CultureInfo original = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
                Assert.That(Call("PortalPerformanceMetrics", "Format", (double?)1.25), Is.EqualTo("1.2500"));
                Assert.That(Call("PortalPerformanceMetrics", "Format", new object[] { null }), Is.EqualTo("null"));
                Assert.That(Call("PortalPerformanceMetrics", "Format", (double?)double.NaN), Is.EqualTo("null"));
                Assert.That(Call("PortalPerformanceMetrics", "NanosecondsToMilliseconds", 1500000L), Is.EqualTo(1.5));
            }
            finally { CultureInfo.CurrentCulture = original; }
        }

        [TestCase(100UL, 100UL, false)]
        [TestCase(101UL, 100UL, true)]
        [TestCase(0UL, 100UL, false)]
        public void RepeatedTimingTimestampIsNotAnotherSample(ulong timestamp, ulong previous, bool expected) =>
            Assert.That(Call("PortalPerformanceMetrics", "IsNewTimestamp", timestamp, previous), Is.EqualTo(expected));

        [TestCase(6.0, 3.0, 2.0)]
        [TestCase(6.0, 0.0, null)]
        [TestCase(6.0, double.NaN, null)]
        public void CostRatioDoesNotInventMissingBaseline(double numerator, double denominator, double? expected) =>
            Assert.That(Call("PortalPerformanceMetrics", "Ratio", (double?)numerator, (double?)denominator), Is.EqualTo(expected));

        [Test]
        public void LeakagePixelsExcludeExistingMagentaAndBlackPositiveControl()
        {
            var background = new[] { new Color32(255, 0, 255, 255), new Color32(0, 0, 0, 255), new Color32(0, 0, 0, 255) };
            var marker = new[] { new Color32(255, 0, 255, 255), new Color32(220, 40, 220, 255), new Color32(40, 40, 40, 255) };
            Assert.That(Call("PortalImageMetrics", "CountNewMagenta", marker, background), Is.EqualTo(1));
            Assert.That(Call("PortalImageMetrics", "CountNewMagenta", background, background), Is.Zero);
        }

        [TestCase(1)]
        [TestCase(-1)]
        public void LeakageMarkerIsBetweenMappedEyeAndExitRegardlessOfNormalSign(int sign)
        {
            Assert.That(Call("PortalImageMetrics", "LeakageMarkerPosition", new Vector3(0, 1.6f, 7), Vector3.back,
                new Vector3(0, 1.5f, 6), Vector3.forward * sign), Is.EqualTo(new Vector3(0, 1.6f, 6.5f)));
        }

        [Test]
        public void LeakageMarkerRejectsPlaneBehindMappedEye()
        {
            Type type = LabSerializationTests.FindType("Portals.Lab.Validation.PortalImageMetrics");
            var method = type.GetMethod("LeakageMarkerPosition");
            Assert.That(method, Is.Not.Null, "Required leakage geometry behavior.");
            var error = Assert.Throws<TargetInvocationException>(() => method.Invoke(null,
                new object[] { new Vector3(0, 0, 7), Vector3.forward, new Vector3(0, 0, 6), Vector3.forward }));
            Assert.That(error.InnerException, Is.InstanceOf<ArgumentException>());
        }
    }
}
