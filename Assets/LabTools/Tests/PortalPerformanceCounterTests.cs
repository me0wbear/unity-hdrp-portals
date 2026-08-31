using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;

namespace Portals.Lab.Tests
{
    public sealed class PortalPerformanceCounterTests
    {
        private const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        private GameObject cameraHost;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Assert.That(Application.isBatchMode, Is.True, "Fixture разрешён только в изолированном batchmode Editor.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
            cameraHost = new GameObject("CounterControlCamera");
            cameraHost.AddComponent<Camera>();
            cameraHost.AddComponent(LabSerializationTests.FindType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData"));
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (cameraHost != null) UnityEngine.Object.Destroy(cameraHost);
            yield return null;
            yield return new ExitPlayMode();
        }

        private static ProfilerMarker Marker() => new ProfilerMarker(ProfilerCategory.Scripts,
            "PortalCounterContract-" + Guid.NewGuid().ToString("N"));

        private static void ResetForFrame(ref ProfilerRecorder recorder)
        {
            object[] args = { recorder };
            LabSerializationTests.FindType("PortalPerformanceCheck").GetMethod("Reset", StaticPrivate).Invoke(null, args);
            recorder = (ProfilerRecorder)args[0];
        }

        private static double? Read(ProfilerRecorder recorder, out double? count)
        {
            object[] args = { recorder, true, null };
            var value = (double?)LabSerializationTests.FindType("PortalPerformanceCheck")
                .GetMethod("Read", StaticPrivate).Invoke(null, args);
            count = (double?)args[2];
            return value;
        }

        [Test]
        public void NativeResetStopsCollectionAndStartRestoresIt()
        {
            var marker = Marker();
            var recorder = ProfilerRecorder.StartNew(marker, 16);
            try
            {
                Assert.That(recorder.Valid, Is.True);
                Assert.That(recorder.IsRunning, Is.True);
                recorder.Reset();
                Assert.That(recorder.Valid, Is.True);
                Assert.That(recorder.IsRunning, Is.False);
                Assert.That(recorder.Count, Is.Zero);
                recorder.Start();
                Assert.That(recorder.IsRunning, Is.True);
            }
            finally { recorder.Dispose(); }
        }

        [Test]
        public void ProbeFrameResetMustLeaveNativeRecorderRunning()
        {
            var marker = Marker();
            var recorder = ProfilerRecorder.StartNew(marker, 16);
            try
            {
                ResetForFrame(ref recorder);
                Assert.That(recorder.Valid, Is.True);
                Assert.That(recorder.IsRunning, Is.True, "Подготовка окна не должна останавливать сбор.");
            }
            finally { recorder.Dispose(); }
        }

        [UnityTest]
        public IEnumerator NativeCpuMarkerControlCollectsAcrossRealFrames()
        {
            var marker = Marker();
            var recorder = ProfilerRecorder.StartNew(marker, 64);
            try
            {
                yield return null;
                using (marker.Auto()) { }
                yield return null;
                for (int i = 0; i < 3; i++) using (marker.Auto()) { }
                for (int i = 0; i < 4; i++) yield return null;
                long total = 0;
                bool one = false, three = false;
                for (int i = 0; i < recorder.Count; i++)
                {
                    var sample = recorder.GetSample(i);
                    TestContext.Progress.WriteLine("native sample=" + i + " count=" + sample.Count + " ns=" + sample.Value);
                    total += sample.Count;
                    one |= sample.Count == 1;
                    three |= sample.Count == 3;
                }
                Assert.That(total, Is.EqualTo(4), "Контрольный recorder должен увидеть реальные marker scopes.");
                Assert.That(one && three, Is.True, "Разные кадры не должны сливаться в один scope count.");
            }
            finally { recorder.Dispose(); }
        }

        [UnityTest]
        public IEnumerator ProbeReadsFreshCpuScopesIncludingQuietFrames()
        {
            var marker = Marker();
            var recorder = ProfilerRecorder.StartNew(marker, 16);
            try
            {
                yield return null;
                for (int mode = 0; mode < 2; mode++)
                foreach (int expected in new[] { 1, 0, 3, 1 })
                {
                    ResetForFrame(ref recorder);
                    Assert.That(recorder.IsRunning, Is.True, "После reset сбор должен продолжаться.");
                    Assert.That(Read(recorder, out _), Is.Null, "Новый интервал не должен читать sample предыдущего кадра или режима.");
                    for (int i = 0; i < expected; i++) using (marker.Auto()) { }
                    yield return null;
                    double? value = Read(recorder, out double? count);
                    TestContext.Progress.WriteLine("expected=" + expected + " count=" + count + " value=" + value);
                    if (expected == 0)
                        Assert.That(count, Is.Null.Or.EqualTo(0), "Тихий кадр не должен повторять старый positive sample.");
                    else
                    {
                        Assert.That(value.HasValue, Is.True, "Должен поступить свежий native CPU sample.");
                        Assert.That(count, Is.EqualTo(expected));
                    }
                }
            }
            finally { recorder.Dispose(); }
        }

    }
}
