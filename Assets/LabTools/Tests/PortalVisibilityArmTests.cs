using System;
using System.Collections;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Portals.Lab.Tests
{
    public sealed class PortalVisibilityArmTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private GameObject host, mainHost, childHost;
        private Component probe;
        private object context;
        private object Field(string name) => probe.GetType().GetField(name, Members).GetValue(probe);
        private void Set(string name, object value) => probe.GetType().GetField(name, Members).SetValue(probe, value);
        private object Call(string method, params object[] args) => probe.GetType().GetMethod(method, Members).Invoke(probe, args);

        [SetUp]
        public void SetUp()
        {
            host = new GameObject("Visibility arm test");
            probe = host.AddComponent(LabSerializationTests.FindType("PortalVisibilityCheck"));
            mainHost = new GameObject("Main");
            Camera main = mainHost.AddComponent<Camera>();
            main.enabled = false;
            Type type = LabSerializationTests.FindType("SandboxProbeContext");
            context = FormatterServices.GetUninitializedObject(type);
            type.GetField("Main", Members).SetValue(context, main);
            Set("context", context);
            Set("roots", Array.CreateInstance(LabSerializationTests.FindType("Portal"), 0));
            Set("cameras", new Camera[0][]);
        }

        [TearDown]
        public void TearDown()
        {
            // Конструктор context не выполнялся; его cleanup здесь не владеет ресурсами.
            Set("context", null);
            UnityEngine.Object.DestroyImmediate(host);
            UnityEngine.Object.DestroyImmediate(mainHost);
            if (childHost != null) UnityEngine.Object.DestroyImmediate(childHost);
        }

        private int Completed() => (int)Field("clock").GetType().GetProperty("Completed").GetValue(Field("clock"));
        private void Complete(int frame) => Field("clock").GetType().GetMethod("Complete").Invoke(Field("clock"), new object[]{frame});

        [Test]
        public void CaptureArmsOnTheRequestedRenderNotOnCoroutineYieldCount()
        {
            var capture = (IEnumerator)Call("CaptureAt", "test", 40, -1, false);
            Assert.That(capture.MoveNext(), Is.True);
            Assert.That(capture.Current, Is.Null, "Вход сначала нормализуется в Update.");
            for (int i = 0; i < 6; i++) Assert.That(capture.MoveNext(), Is.True);
            Assert.That(Field("observing"), Is.Null, "Yields без completed renders не приближают capture.");
            for (int i = 1; i <= 38; i++) Complete(i);
            Assert.That(capture.MoveNext(), Is.True);
            Assert.That(Field("observing"), Is.Null);
            Complete(39);
            Assert.That(capture.MoveNext(), Is.True);
            Assert.That(Field("observing"), Is.Not.Null);
            Assert.That(capture.Current, Is.InstanceOf<IEnumerator>(), "Следующий EndOfFrame принадлежит render 40.");
            // Screenshot coroutine не исполняется: тест не подменяет actual Player pixels.
            (capture as IDisposable)?.Dispose();
        }

        [Test]
        public void MissedRenderBoundaryBlocksInsteadOfCapturingALaterFrame()
        {
            var capture = (IEnumerator)Call("CaptureAt", "test", 1, -1, false);
            Assert.That(capture.MoveNext(), Is.True);
            Complete(1);
            Assert.That(capture.MoveNext(), Is.False);
            string problem = (string)context.GetType().GetField("Problem", Members).GetValue(context);
            Assert.That(problem, Is.Not.Empty);
            Assert.That(Field("observing"), Is.Null);
        }

        [Test]
        public void OnlyCompletedMainCallbacksAdvanceTheArmClock()
        {
            childHost = new GameObject("Virtual or foreign");
            Camera child = childHost.AddComponent<Camera>();
            child.enabled = false;
            Call("OnCameraEnd", default(ScriptableRenderContext), child);
            Assert.That(Completed(), Is.Zero);
            Set("cameras", new[]{new[]{child}});
            Call("OnCameraEnd", default(ScriptableRenderContext), child);
            Assert.That(Completed(), Is.Zero);
            Call("OnCameraEnd", default(ScriptableRenderContext), mainHost.GetComponent<Camera>());
            Assert.That(Completed(), Is.EqualTo(1));
        }

        [TestCase("6000.5.9f1", "17.5.0", true)]
        [TestCase("6000.5.9f2", "17.5.0", false)]
        [TestCase("6000.5.9f1", "17.4.0", false)]
        [TestCase(null, "17.5.0", false)]
        public void RegularAoPreparationRequiresPinnedOwnerApi(string unity, string hdrp, bool expected)
        {
            MethodInfo method = probe.GetType().GetMethod("SupportsAoPreparation", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That((bool)method.Invoke(null, new object[]{unity, hdrp}), Is.EqualTo(expected));
        }
    }
}
