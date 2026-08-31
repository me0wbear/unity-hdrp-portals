using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
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
            int cursor = 0;
            return Drain(recorder, ref cursor, null, out count);
        }

        private static ProfilerRecorder ProductionRecorder(string markerName)
        {
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            foreach (var handle in handles)
            {
                var description = ProfilerRecorderHandle.GetDescription(handle);
                if (description.Name != markerName) continue;
                return (ProfilerRecorder)LabSerializationTests.FindType("PortalPerformanceCheck")
                    .GetMethod("Start", StaticPrivate).Invoke(null, new object[] { description, false });
            }
            Assert.Fail("Созданный native marker должен присутствовать в discovery.");
            return default;
        }

        private static double? Drain(ProfilerRecorder recorder, ref int cursor, List<double> values, out double? count)
        {
            var method = LabSerializationTests.FindType("PortalPerformanceCheck").GetMethod("ReadFresh", StaticPrivate);
            Assert.That(method, Is.Not.Null, "Для непрерывного сбора требуется cursor-aware reader.");
            object[] args = { recorder, true, cursor, null, values };
            var value = (double?)method.Invoke(null, args);
            cursor = (int)args[2];
            count = (double?)args[3];
            return value;
        }

        [UnityTest]
        public IEnumerator ProductionRecorderRetainsFramesUntilConsumption()
        {
            string name = "PortalCounterRetention-" + Guid.NewGuid().ToString("N");
            var marker = new ProfilerMarker(ProfilerCategory.Scripts, name);
            var recorder = ProductionRecorder(name);
            try
            {
                foreach (int scopes in new[] { 1, 1, 3 })
                {
                    for (int i = 0; i < scopes; i++) using (marker.Auto()) { }
                    yield return null;
                }
                for (int i = 0; i < 4; i++) yield return null;
                Assert.That(recorder.IsRunning, Is.True);
                Assert.That(recorder.WrappedAround, Is.False, "Producer не должен затирать непрочитанные кадры.");
                long total = 0;
                for (int i = 0; i < recorder.Count; i++) total += recorder.GetSample(i).Count;
                Assert.That(total, Is.EqualTo(5));
            }
            finally { recorder.Dispose(); }
        }

        [UnityTest]
        public IEnumerator FreshReaderConsumesEveryArrivalOnceAndExcludesPreviousMode()
        {
            var marker = Marker();
            var recorder = ProfilerRecorder.StartNew(marker, 64);
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    using (marker.Auto()) { }
                    yield return null;
                }
                for (int i = 0; i < 4; i++) yield return null;
                var values = new List<double>();
                int cursor = 0;
                Assert.That(Drain(recorder, ref cursor, values, out _), Is.Not.Null);
                Assert.That(values.Count, Is.EqualTo(recorder.Count));
                int consumed = values.Count;
                Assert.That(Drain(recorder, ref cursor, values, out var repeatedCount), Is.Null);
                Assert.That(repeatedCount, Is.Null);
                Assert.That(values.Count, Is.EqualTo(consumed));
                ResetForFrame(ref recorder);
                cursor = 0;
                values.Clear();
                Assert.That(Drain(recorder, ref cursor, values, out _), Is.Null);
                Assert.That(values, Is.Empty, "Граница режима не должна переносить старые данные.");
            }
            finally { recorder.Dispose(); }
        }

        [UnityTest]
        public IEnumerator FreshReaderRejectsOverwrittenHistory()
        {
            var marker = Marker();
            var recorder = ProfilerRecorder.StartNew(marker, 1);
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    using (marker.Auto()) { }
                    yield return null;
                }
                Assert.That(recorder.WrappedAround, Is.True, "Контроль должен действительно переполнить буфер.");
                int cursor = 0;
                var values = new List<double> { 123 };
                Assert.That(Drain(recorder, ref cursor, values, out _), Is.Null);
                Assert.That(cursor, Is.LessThan(0));
                Assert.That(values, Is.Empty, "Неполная история не должна оставаться валидной статистикой.");
            }
            finally { recorder.Dispose(); }
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
