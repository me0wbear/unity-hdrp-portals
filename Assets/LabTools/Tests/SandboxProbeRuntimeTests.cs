using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace Portals.Lab.Tests
{
    public sealed class SandboxProbeRuntimeTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private Camera main, root;
        private Component portal, run, probe;
        private object context;
        private MeshRenderer screen;
        private RenderTexture view, depth;
        private string output;
        private Action<ScriptableRenderContext, Camera> rendererBinding;
        private Matrix4x4 cachedInverse;
        private object volumeManager;
        private bool initializedVolumes;
        private static readonly int InverseId = Shader.PropertyToID("_PortalInverseProjection");
        private static readonly int DepthId = Shader.PropertyToID("_ContentDepth");
        private static readonly int SentinelId = Shader.PropertyToID("_SandboxTestSentinel");

        private static object Field(object target, string name) => target.GetType().GetField(name, Members).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Members).SetValue(target, value);
        private static object Call(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name, Members);
            Assert.That(method, Is.Not.Null, "Required probe behavior: " + name);
            return method.Invoke(target, args);
        }

        private GameObject Host(string name)
        {
            var go = new GameObject(name);
            owned.Add(go);
            return go;
        }

        [SetUp]
        public void SetUp()
        {
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../Logs/task0c-fixture-" + Guid.NewGuid().ToString("N")));
            main = Host("ProbeMain").AddComponent<Camera>();
            main.gameObject.AddComponent(LabSerializationTests.FindType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData"));
            run = Host("ProbeRun").AddComponent(LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckRun"));
            object identity = PortalCheckPolicyTests.Identity();
            Set(identity, "outputDirectory", output);
            Call(run, "SetBuildIdentity", identity);
            // Bypass only Player-only constructor guards. Real probe methods, cameras,
            // property blocks, counters and artifact I/O are exercised below.
            Type type = LabSerializationTests.FindType("SandboxProbeContext");
            context = FormatterServices.GetUninitializedObject(type);
            Set(context, "Run", run);
            Set(context, "Main", main);
            Set(context, "Player", main.transform);
            Set(context, "Portals", Array.CreateInstance(LabSerializationTests.FindType("Portal"), 0));
            Set(context, "Problem", string.Empty);
            foreach (string name in new[] { "disabled", "frozen" })
                Set(context, name, Activator.CreateInstance(type.GetField(name, Members).FieldType));
        }

        private void ParityFixture()
        {
            GameObject host = Host("ProbePortal");
            host.SetActive(false);
            portal = host.AddComponent(LabSerializationTests.FindType("Portal"));
            ((Behaviour)portal).enabled = false; // No global registry or production resource ownership in this fixture.
            host.SetActive(true);
            screen = host.AddComponent<MeshRenderer>();
            Set(portal, "screen", screen);
            Set(portal, "playerCamera", main);
            Array portals = Array.CreateInstance(portal.GetType(), 1);
            portals.SetValue(portal, 0);
            Set(context, "Portals", portals);
            root = Host("RootVirtual").AddComponent<Camera>();
            root.transform.SetParent(host.transform);
            view = new RenderTexture(32, 32, 0); owned.Add(view);
            depth = new RenderTexture(32, 32, 0); owned.Add(depth);
            Assert.That(view.Create(), Is.True);
            Assert.That(depth.Create(), Is.True);
            root.targetTexture = view;
            cachedInverse = GL.GetGPUProjectionMatrix(root.CalculateObliqueMatrix(new Vector4(0, 0, -1, -2)), true).inverse;
            var block = new MaterialPropertyBlock();
            block.SetFloat(SentinelId, 37);
            screen.SetPropertyBlock(block);
            rendererBinding = (renderContext, camera) =>
            {
                if (camera != main) return;
                Call(portal, "SetViewTexture", view);
                Call(portal, "SetContentBuffers", depth, cachedInverse);
            };
            RenderPipelineManager.beginCameraRendering += rendererBinding;
            AddProbe("SandboxParityCheck");
            Set(probe, "mode", "regular-projection");
            Set(probe, "regularProjection", true);
        }

        private void AddProbe(string name)
        {
            probe = Host(name).AddComponent(LabSerializationTests.FindType(name));
            Set(probe, "run", run);
            Set(probe, "context", context);
        }

        // Raise the real pipeline event without rendering a Player or replacing the pipeline.
        private sealed class CameraEvent : RenderPipeline
        {
            public static void Raise(Camera camera) => BeginCameraRendering(default, camera);
            protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras) { }
        }

        private Matrix4x4 BoundInverse()
        {
            var block = new MaterialPropertyBlock();
            screen.GetPropertyBlock(block);
            Assert.That(block.GetTexture(DepthId), Is.SameAs(depth));
            Assert.That(block.GetFloat(SentinelId), Is.EqualTo(37));
            Assert.That(portal.GetType().GetProperty("ViewTexture").GetValue(portal), Is.SameAs(view));
            Assert.That(Field(portal, "writeContentDepth"), Is.True);
            return block.GetMatrix(InverseId);
        }

        private static void MatrixEquals(Matrix4x4 actual, Matrix4x4 expected)
        {
            for (int i = 0; i < 16; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(0.00001f), "matrix element " + i);
        }

        [Test]
        public void MainConsumptionReplacesStaleObliqueInverseAndPreservesTextures()
        {
            ParityFixture();
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            MatrixEquals(BoundInverse(), GL.GetGPUProjectionMatrix(root.projectionMatrix, true).inverse);
        }

        [Test]
        public void RecreatedRendererCallbackStillPrecedesProbeBinding()
        {
            ParityFixture();
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            RenderPipelineManager.beginCameraRendering -= rendererBinding;
            RenderPipelineManager.beginCameraRendering += rendererBinding;
            view = new RenderTexture(48, 48, 0); owned.Add(view);
            Assert.That(view.Create(), Is.True);
            root.targetTexture = null;
            root.enabled = false;
            root = Host("RecreatedRoot").AddComponent<Camera>();
            root.transform.SetParent(portal.transform);
            root.targetTexture = view;
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            MatrixEquals(BoundInverse(), GL.GetGPUProjectionMatrix(root.projectionMatrix, true).inverse);
        }

        [Test]
        public void RegularToObliqueLetsProductionRestoreTheConsumedInverse()
        {
            ParityFixture();
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            MatrixEquals(BoundInverse(), GL.GetGPUProjectionMatrix(root.projectionMatrix, true).inverse);
            Set(probe, "regularProjection", false);
            Set(probe, "mode", "baseline");
            root.projectionMatrix = root.CalculateObliqueMatrix(new Vector4(0, 0, -1, -3));
            cachedInverse = GL.GetGPUProjectionMatrix(root.projectionMatrix, true).inverse;
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            MatrixEquals(BoundInverse(), cachedInverse);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void BaselineAndForeignCamerasDoNotOverrideProduction(bool foreign)
        {
            ParityFixture();
            Set(probe, "regularProjection", foreign);
            Set(probe, "mode", foreign ? "regular-projection" : "baseline");
            rendererBinding(default, main);
            Call(probe, "LateUpdate");
            CameraEvent.Raise(foreign ? root : main);
            MatrixEquals(BoundInverse(), cachedInverse);
        }

        [Test]
        public void DisableRemovesTheProbeCallback()
        {
            ParityFixture();
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            MatrixEquals(BoundInverse(), GL.GetGPUProjectionMatrix(root.projectionMatrix, true).inverse);
            Call(probe, "OnDisable");
            CameraEvent.Raise(main);
            MatrixEquals(BoundInverse(), cachedInverse);
        }

        [Test]
        public void DiagnosticAuditPersistsObservedBindingAndRejectsSubsequentCorruption()
        {
            ParityFixture();
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            Call(probe, "SaveProjectionAudit", "valid", 0);
            Assert.That(File.ReadAllText(Path.Combine(output, "valid-projection-audit.json")), Does.Contain("RootVirtual"));
            Assert.That((string)Field(context, "Problem"), Is.Empty);
            Call(portal, "SetContentBuffers", depth, cachedInverse);
            Call(probe, "SaveProjectionAudit", "corrupt", 0);
            string failure = (string)Field(context, "Problem");
            Assert.That(failure, Is.Not.Empty);
            object decision = LabSerializationTests.FindType("Portals.Lab.Validation.SandboxParityPolicy")
                .GetMethod("Evaluate").Invoke(null, new object[] { null, null, failure });
            Assert.That(PortalCheckPolicyTests.Field(decision, "status"), Is.EqualTo("Blocked"));
        }

        [Test]
        public void DiagnosticAuditRequiresAnObservedMainBinding()
        {
            ParityFixture();
            Call(probe, "SaveProjectionAudit", "unobserved", 0);
            Assert.That((string)Field(context, "Problem"), Is.Not.Empty);
        }

        [Test]
        public void DiagnosticAuditRejectsAnOlderMainBindingAtCapture()
        {
            ParityFixture();
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            Set(Field(probe, "projectionAudit"), "lastBindingFrame", Time.frameCount - 1);
            Call(probe, "SaveProjectionAudit", "older-frame", 0);
            Assert.That((string)Field(context, "Problem"), Is.Not.Empty);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingOrAmbiguousRootBlocksDiagnosticEvidence(bool ambiguous)
        {
            ParityFixture();
            if (ambiguous)
            {
                Camera duplicate = Host("DuplicateTarget").AddComponent<Camera>();
                duplicate.transform.SetParent(portal.transform);
                duplicate.targetTexture = view;
            }
            else root.targetTexture = null;
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            Assert.That((string)Field(context, "Problem"), Is.Not.Empty);
        }

        [Test]
        public void PerformanceSetupPrecedes180WarmupAnd360RetainedSamples()
        {
            Type managerType = LabSerializationTests.FindType("UnityEngine.Rendering.VolumeManager");
            volumeManager = managerType.GetProperty("instance").GetValue(null);
            initializedVolumes = !(bool)managerType.GetProperty("isInitialized").GetValue(volumeManager);
            if (initializedVolumes) Call(volumeManager, "Initialize", null, null);
            AddProbe("PortalPerformanceCheck");
            IEnumerator measure = (IEnumerator)Call(probe, "Measure", 0, "off");
            string counters = Path.Combine(output, "available-counters.txt");
            Assert.That(measure.MoveNext(), Is.True); // release frame
            Assert.That(File.Exists(counters), Is.False);
            Assert.That(measure.MoveNext(), Is.True); // enabled-mode setup frame, before discovery
            Assert.That(File.Exists(counters), Is.False);
            for (int frame = 0; frame < 180; frame++)
            {
                Assert.That(measure.MoveNext(), Is.True, "warmup " + frame);
                Assert.That(File.Exists(counters), Is.True, "discovery must finish before warmup starts");
                Assert.That(Field(run, "capturedFrames"), Is.EqualTo(0));
            }
            for (int frame = 0; frame < 360; frame++)
            {
                Assert.That(measure.MoveNext(), Is.True, "sample interval " + frame);
                Assert.That(Field(run, "capturedFrames"), Is.EqualTo(frame), "retain only after the sampled interval");
            }
            Assert.That(measure.MoveNext(), Is.False);
            Assert.That(Field(run, "capturedFrames"), Is.EqualTo(360));
            string[] rows = File.ReadAllLines(Path.Combine(output, "round0/off-samples.csv"));
            Assert.That(rows, Has.Length.EqualTo(361));
            Assert.That(rows[1], Does.StartWith("0,"));
            Assert.That(rows[360], Does.StartWith("359,"));
            (measure as IDisposable)?.Dispose();
        }

        [TearDown]
        public void TearDown()
        {
            RenderPipelineManager.beginCameraRendering -= rendererBinding;
            // Dispose the probe before its dependencies, without requesting any Player exit.
            if (probe != null) UnityEngine.Object.DestroyImmediate(probe.gameObject);
            if (root != null) root.targetTexture = null;
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) UnityEngine.Object.DestroyImmediate(owned[i]);
            owned.Clear();
            if (initializedVolumes) Call(volumeManager, "Deinitialize");
            initializedVolumes = false;
            if (Directory.Exists(output)) Directory.Delete(output, true);
        }
    }
}
