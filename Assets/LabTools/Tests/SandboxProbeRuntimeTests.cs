using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace Portals.Lab.Tests
{
    public sealed class SandboxProbeRuntimeTests
    {
        private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        private Camera main, root;
        private Component portal, paired, system, run, probe;
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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            if (!Application.isBatchMode) Assert.Ignore("Requires an isolated batchmode Editor.");
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            yield return new EnterPlayMode();
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
            main.transform.SetPositionAndRotation(new Vector3(0, 0, 2), Quaternion.Euler(0, 180, 0));
            portal = CreatePortal("ProbePortal", Vector3.zero);
            paired = CreatePortal("PairedExit", new Vector3(40, 0, 0));
            Set(portal, "exitPortal", paired);
            Set(paired, "exitPortal", portal);
            portal.gameObject.SetActive(true);
            paired.gameObject.SetActive(true);
            system = GameObject.Find("PortalSystem").GetComponent(LabSerializationTests.FindType("PortalSystem"));
            Call(system, "LateUpdate");
            Assert.That(HasContentBuffers(portal), Is.True, "Entrance must be a real composite contributor.");
            Assert.That(HasContentBuffers(paired), Is.False, "Offscreen paired exit must not own active content.");
            screen = (MeshRenderer)Field(portal, "screen");
            ContextPortals(portal);
            root = Array.Find(portal.GetComponentsInChildren<Camera>(true), camera => camera.name.EndsWith("_Camera_0"));
            Assert.That(root, Is.Not.Null);
            view = root.targetTexture;
            CameraEvent.Raise(main);
            var block = new MaterialPropertyBlock();
            screen.GetPropertyBlock(block);
            depth = (RenderTexture)block.GetTexture(DepthId);
            Assert.That(depth, Is.Not.Null);
            cachedInverse = block.GetMatrix(InverseId);
            block.SetFloat(SentinelId, 37);
            screen.SetPropertyBlock(block);
            // A controllable producer exercises callback re-registration and stale inverse repair.
            // Contributor membership and resource allocation still come from the real PortalSystem.
            rendererBinding = (renderContext, camera) =>
            {
                if (camera != main) return;
                Call(portal, "SetViewTexture", view);
                Call(portal, "SetContentBuffers", depth, cachedInverse);
            };
            RenderPipelineManager.beginCameraRendering += rendererBinding;
            AddProbe("SandboxParityCheck");
            Set(probe, "entrance", portal);
            Set(probe, "mode", "regular-projection");
            Set(probe, "regularProjection", true);
        }

        private Component CreatePortal(string name, Vector3 position)
        {
            GameObject host = Host(name);
            host.SetActive(false);
            host.transform.position = position;
            Component result = host.AddComponent(LabSerializationTests.FindType("Portal"));
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.SetParent(host.transform, false);
            UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
            var renderer = quad.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/portal/PortalScreenMat.mat");
            Set(result, "screen", renderer);
            Set(result, "playerCamera", main);
            Set(result, "resolutionDivider", 64);
            return result;
        }

        private static bool HasContentBuffers(Component candidate) => (bool)LabSerializationTests.FindType("PortalSystem")
            .GetMethod("HasContentBuffers").Invoke(null, new object[] { candidate });

        private void ContextPortals(params Component[] candidates)
        {
            Array portals = Array.CreateInstance(LabSerializationTests.FindType("Portal"), candidates.Length);
            for (int i = 0; i < candidates.Length; i++) portals.SetValue(candidates[i], i);
            Set(context, "Portals", portals);
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
            CameraEvent.Raise(foreign ? Host("ForeignCamera").AddComponent<Camera>() : main);
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
            Assert.That(File.ReadAllText(Path.Combine(output, "valid-projection-audit.json")), Does.Contain(root.name));
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

        [TestCase(false)]
        [TestCase(true)]
        public void PairedOffscreenExitDoesNotInvalidateTheEntranceBinding(bool previouslyAllocated)
        {
            ParityFixture();
            ContextPortals(portal, paired);
            if (previouslyAllocated)
            {
                Set(paired, "cullWhenOffscreen", false);
                Call(system, "LateUpdate");
                Assert.That(HasContentBuffers(paired), Is.True);
                Set(paired, "cullWhenOffscreen", true);
                Call(system, "LateUpdate");
            }
            Assert.That(HasContentBuffers(paired), Is.False);
            Call(probe, "LateUpdate");
            CameraEvent.Raise(root); // Production gives the exit a borrowed recursion texture.
            Assert.That(paired.GetType().GetProperty("ViewTexture").GetValue(paired), Is.Not.Null);
            CameraEvent.Raise(main);
            Call(probe, "SaveProjectionAudit", "paired", 0);
            MatrixEquals(BoundInverse(), GL.GetGPUProjectionMatrix(root.projectionMatrix, true).inverse);
            Assert.That((string)Field(context, "Problem"), Is.Empty);
            Assert.That(Field(Field(probe, "projectionAudit"), "mainBindings"), Is.EqualTo(1));
        }

        [Test]
        public void ReactivatedPairedContributorWithMissingRootStillBlocks()
        {
            ParityFixture();
            ContextPortals(portal, paired);
            Set(paired, "cullWhenOffscreen", false);
            Call(system, "LateUpdate");
            Set(paired, "cullWhenOffscreen", true);
            Call(system, "LateUpdate");
            Assert.That(HasContentBuffers(paired), Is.False);
            Set(paired, "cullWhenOffscreen", false);
            Call(system, "LateUpdate");
            Assert.That(HasContentBuffers(paired), Is.True);
            Camera pairedRoot = Array.Find(paired.GetComponentsInChildren<Camera>(true), camera => camera.name.EndsWith("_Camera_0"));
            pairedRoot.targetTexture = null;
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            Assert.That((string)Field(context, "Problem"), Is.Not.Empty);
        }

        [Test]
        public void AnotherContributorsBindingCannotSubstituteForTheRequiredEntrance()
        {
            ParityFixture();
            Set(probe, "entrance", paired);
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            Assert.That(Field(Field(probe, "projectionAudit"), "mainBindings"), Is.EqualTo(1));
            Call(probe, "SaveProjectionAudit", "wrong-entrance", 0);
            Assert.That((string)Field(context, "Problem"), Is.Not.Empty);
        }

        [Test]
        public void OtherContributorCannotSatisfyTheRequiredEntrancesNewBindingCount()
        {
            ParityFixture();
            ContextPortals(portal, paired);
            Set(paired, "cullWhenOffscreen", false);
            Call(system, "LateUpdate");
            Call(probe, "LateUpdate");
            CameraEvent.Raise(main);
            Assert.That((string)Field(context, "Problem"), Is.Empty);
            // One required-entrance binding already existed before the capture interval.
            // The other portal's binding must not make that count look fresh.
            Call(probe, "SaveProjectionAudit", "no-new-entrance-binding", 1);
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

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            RenderPipelineManager.beginCameraRendering -= rendererBinding;
            // Dispose the probe before its dependencies, without requesting any Player exit.
            if (probe != null) UnityEngine.Object.DestroyImmediate(probe.gameObject);
            if (root != null) root.targetTexture = null;
            for (int i = owned.Count - 1; i >= 0; i--)
                if (owned[i] != null) UnityEngine.Object.Destroy(owned[i]);
            owned.Clear();
            // Production resources use deferred destruction; exercise their real Play Mode lifecycle.
            yield return null;
            if (initializedVolumes) Call(volumeManager, "Deinitialize");
            initializedVolumes = false;
            if (Directory.Exists(output)) Directory.Delete(output, true);
            yield return new ExitPlayMode();
        }
    }
}
