using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

public sealed class PortalSchedulingTests
{
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    private Camera viewer;
    private Portal a, b;
    private readonly List<GameObject> owned = new List<GameObject>();
    private int previousBudget;
    private delegate int PlanCall(Camera camera, int ceiling, out float coverage);

    private static PlanCall Planner(PortalRenderer renderer) =>
        (PlanCall)typeof(PortalRenderer).GetMethod("Plan", Hidden)
            .CreateDelegate(typeof(PlanCall), renderer);

    [UnitySetUp]
    public IEnumerator SetUp()
    {
        if (!Application.isBatchMode) Assert.Ignore("Requires an isolated batchmode Editor.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        yield return new EnterPlayMode();
        previousBudget = PortalSystem.Budget;
        PortalSystem.Budget = 8;
        viewer = Host("Visibility Viewer").AddComponent<Camera>();
        viewer.nearClipPlane = 0.05f;
        viewer.fieldOfView = 60;
        viewer.aspect = 1;
        viewer.gameObject.AddComponent(FindType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData"));
        a = End("Entrance", new Vector3(0, 0, 2), Quaternion.Euler(0, 180, 0));
        b = End("Exit", new Vector3(30, 0, 2), Quaternion.identity);
        a.exitPortal = b; b.exitPortal = a;
        a.gameObject.SetActive(true); b.gameObject.SetActive(true);
    }

    private GameObject Host(string name) { var go = new GameObject(name); owned.Add(go); return go; }
    private Portal End(string name, Vector3 position, Quaternion rotation)
    {
        GameObject go = Host(name); go.SetActive(false);
        go.transform.SetPositionAndRotation(position, rotation);
        Portal portal = go.AddComponent<Portal>();
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.transform.SetParent(go.transform, false);
        Object.Destroy(quad.GetComponent<Collider>());
        portal.screen = quad.GetComponent<MeshRenderer>();
        portal.screen.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/portal/PortalScreenMat.mat");
        portal.playerCamera = viewer;
        portal.resolutionDivider = 32;
        return portal;
    }
    private static Type FindType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        { Type type = assembly.GetType(name); if (type != null) return type; }
        throw new InvalidOperationException(name);
    }
    private static Dictionary<Portal, PortalRenderer> Renderers =>
        (Dictionary<Portal, PortalRenderer>)typeof(PortalSystem).GetField("Renderers", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static void Tick() => typeof(PortalSystem).GetMethod("LateUpdate", Hidden)
        .Invoke(Object.FindFirstObjectByType<PortalSystem>(), null);
    private static void Begin(Camera camera)
    {
        foreach (PortalRenderer renderer in Renderers.Values)
            typeof(PortalRenderer).GetMethod("OnBeginCameraRendering", Hidden).Invoke(renderer,
                new object[] { default(ScriptableRenderContext), camera });
    }
    private void FaceToFace() => b.transform.position = new Vector3(0, 0, -2);
    private static int EnabledCameras(Portal portal)
    {
        int count = 0;
        foreach (Camera camera in portal.GetComponentsInChildren<Camera>(true)) if (camera.enabled) count++;
        return count;
    }

    [Test]
    public void SideBySidePairRequestsOnlyOneUsefulLevel()
    {
        Tick();
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(1));
        Assert.That(a.GetComponentsInChildren<Camera>(true), Has.Length.EqualTo(1));
        Assert.That(Renderers[b].LevelCount, Is.Zero);
        Assert.That(a.recursionDepth, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WarmPlannerDoesNotAllocateManagedMemory(bool recursive)
    {
        if (recursive) FaceToFace();
        PlanCall plan = Planner(Renderers[a]);
        for (int i = 0; i < 256; i++) plan(viewer, 3, out _);
        // Такой же синхронный recorder используют установленные Core/Collections tests.
        // Положительный контроль обязателен: неподдержанный счётчик не доказывает ноль.
        UnityEngine.Profiling.Recorder recorder = UnityEngine.Profiling.Recorder.Get("GC.Alloc");
        Assert.That(recorder.isValid, Is.True);
        recorder.FilterToCurrentThread();
        try
        {
            recorder.enabled = false;
            recorder.enabled = true;
            byte[] control = new byte[128];
            recorder.enabled = false;
            int positive = recorder.sampleBlockCount;
            GC.KeepAlive(control);
            Debug.Log($"[PortalPlannerAllocationControl] samples={positive}");
            Assert.That(positive, Is.GreaterThan(0), "GC.Alloc must detect a known managed allocation.");

            recorder.enabled = true;
            int total = 0;
            for (int i = 0; i < 512; i++) total += plan(viewer, 3, out _);
            recorder.enabled = false;
            int allocated = recorder.sampleBlockCount;
            Debug.Log($"[PortalPlannerAllocation] recursive={recursive} calls=512 allocations={allocated}");
            Assert.That(total, Is.EqualTo(512 * (recursive ? 3 : 1)));
            Assert.That(allocated, Is.Zero, "Warmed planning must not allocate strings or scratch buffers.");
        }
        finally { recorder.enabled = false; }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PlannerReclassifiesRuntimeShaderReplacement(bool exitScreen)
    {
        Portal subject = exitScreen ? b : a;
        Material original = subject.screen.sharedMaterial;
        Material replacement = new Material(original);
        try
        {
            subject.screen.sharedMaterial = replacement;
            PlanCall plan = Planner(Renderers[a]);
            Assert.That(plan(viewer, 3, out _), Is.EqualTo(1));
            Shader unsupported = Shader.Find("HDRP/Unlit");
            Assert.That(unsupported, Is.Not.Null, "The unsupported-shader control must exist.");
            replacement.shader = unsupported;
            Assert.That(plan(viewer, 3, out _), Is.EqualTo(3), "Changed shader must retain the conservative prefix.");
            replacement.shader = original.shader;
            Assert.That(plan(viewer, 3, out _), Is.EqualTo(1), "Restored shader must be recognized.");
        }
        finally
        {
            subject.screen.sharedMaterial = original;
            Object.Destroy(replacement);
        }
    }

    [TestCase(0f, 0f, 0f, false)]
    [TestCase(17f, 31f, 7f, false)]
    [TestCase(-12f, 68f, 4f, false)]
    [TestCase(17f, 31f, 7f, true)]
    public void OrdinaryPoseRetainsOneUsefulLevel(float pitch, float yaw, float roll, bool parented)
    {
        // Жёсткий перенос всего fixture не меняет взаимную видимость его проёмов.
        Vector3 translation = new Vector3(13.25f, -4.5f, 27.75f);
        Quaternion rotation = Quaternion.Euler(pitch, yaw, roll);
        viewer.transform.SetPositionAndRotation(translation, rotation);
        if (parented)
        {
            Transform parent = Host("Ordinary Camera Parent").transform;
            parent.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(11, 23, 5));
            viewer.transform.SetParent(parent, true);
        }
        a.transform.SetPositionAndRotation(translation + rotation * new Vector3(0, 0, 2),
            rotation * Quaternion.Euler(0, 180, 0));
        b.transform.SetPositionAndRotation(translation + rotation * new Vector3(30, 0, 2), rotation);
        int demand = Planner(Renderers[a])(viewer, 3, out _);
        Tick();
        Assert.That(demand, Is.EqualTo(1), "An ordinary rigidly moved fixture still has one useful level.");
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(demand));
        Assert.That(EnabledCameras(a), Is.EqualTo(1));
        Assert.That(Renderers[b].LevelCount, Is.Zero);
        // Та же поза с реальной рекурсией обязана сохранить дочерние виды.
        b.transform.position = translation + rotation * new Vector3(0, 0, -2);
        Tick();
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(3));
        Assert.That(EnabledCameras(a), Is.EqualTo(3));
        foreach (Camera camera in a.GetComponentsInChildren<Camera>(true))
        {
            if (!camera.enabled) continue;
            Begin(camera);
            Assert.That(a.ViewTexture, Is.SameAs(b.ViewTexture));
            Assert.That(a.ViewTexture, Is.Not.SameAs(camera.targetTexture));
        }
    }

    [TestCase(0f)]
    [TestCase(300f)]
    public void CameraRelativeViewUsesLinearPartAndTransformEye(float rawTranslation)
    {
        viewer.transform.position = new Vector3(7, 2, -3);
        a.transform.position = viewer.transform.position + new Vector3(0, 0, 2);
        Matrix4x4 view = Matrix4x4.Scale(new Vector3(2, 1, -1));
        view.SetColumn(3, new Vector4(rawTranslation, -rawTranslation, rawTranslation, 1));
        viewer.worldToCameraMatrix = view;
        Assert.That(Planner(Renderers[a])(viewer, 1, out float coverage), Is.EqualTo(1));
        // Проём 1x1 при z=2: удвоенная X-строка удваивает площадь покрытия.
        float expected = 2 * Mathf.Pow(0.5f / (2 * Mathf.Tan(30 * Mathf.Deg2Rad)), 2);
        Assert.That(coverage, Is.EqualTo(expected).Within(1e-5));
        a.transform.rotation = Quaternion.identity;
        Assert.That(Planner(Renderers[a])(viewer, 1, out _), Is.Zero,
            "Facing follows the transform eye used by HDRP camera-relative rendering.");
    }

    [TestCase(15f, 1)]
    [TestCase(19.9f, 1)]
    [TestCase(20f, 1)]
    [TestCase(20.1f, 1)]
    [TestCase(30f, 0)]
    public void CameraRelativeFarPlaneUsesUnnormalizedViewRow(float distance, int expected)
    {
        viewer.farClipPlane = 10;
        Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -0.5f));
        view.m23 = 300;
        viewer.worldToCameraMatrix = view;
        a.transform.SetPositionAndRotation(new Vector3(0, 0, distance), Quaternion.Euler(0, 135, 0));
        Assert.That(Planner(Renderers[a])(viewer, 1, out _), Is.EqualTo(expected));
    }

    [TestCase(0f, 1)]
    [TestCase(1.0773503f, 1)]
    [TestCase(1.09f, 0)]
    public void CameraRelativeLinearViewRetainsViewportEdge(float horizontal, int expected)
    {
        Matrix4x4 view = Matrix4x4.Scale(new Vector3(2, 1, -1));
        view.m03 = 300;
        viewer.worldToCameraMatrix = view;
        a.transform.position = new Vector3(horizontal, 0, 2);
        Assert.That(Planner(Renderers[a])(viewer, 1, out _), Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    public void UnsupportedViewDoesNotClaimEmptyCoverage(int scenario)
    {
        Matrix4x4 view = viewer.worldToCameraMatrix;
        if (scenario == 0) view.m00 = 0;
        if (scenario == 1) view.m00 = 1e-8f;
        if (scenario == 2) view.m00 = -1;
        if (scenario == 3) view.m30 = 0.1f;
        viewer.worldToCameraMatrix = view;
        Assert.That(Planner(Renderers[a])(viewer, 3, out _), Is.EqualTo(3));
    }

    [Test]
    public void NonfiniteRelativeViewIsUnsupportedBeforeCameraAssignment()
    {
        Matrix4x4 view = viewer.worldToCameraMatrix;
        view.m01 = float.NaN;
        MethodInfo supported = typeof(PortalRenderer).GetMethod("SupportedRelativeView",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.That((bool)supported.Invoke(null, new object[] { view }), Is.False);
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void UnsupportedRenderingConventionRetainsPrefix(int scenario)
    {
        Component data = viewer.GetComponent(FindType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData"));
        FieldInfo inverted = data.GetType().GetField("invertFaceCulling");
        FieldInfo relative = FindType("UnityEngine.Rendering.HighDefinition.ShaderConfig")
            .GetField("s_CameraRelativeRendering", BindingFlags.Public | BindingFlags.Static);
        bool previousGl = GL.invertCulling, previousCamera = (bool)inverted.GetValue(data);
        int previousRelative = (int)relative.GetValue(null);
        try
        {
            if (scenario == 0) GL.invertCulling = true;
            if (scenario == 1) inverted.SetValue(data, true);
            if (scenario == 2) relative.SetValue(null, 0);
            Assert.That(Planner(Renderers[a])(viewer, 3, out _), Is.EqualTo(3));
        }
        finally
        {
            GL.invertCulling = previousGl;
            inverted.SetValue(data, previousCamera);
            relative.SetValue(null, previousRelative);
        }
    }

    [TestCase(15f, 1)]
    [TestCase(30f, 0)]
    public void CameraRelativeFarDepthFollowsViewNotTransformForward(float distance, int expected)
    {
        viewer.farClipPlane = 10;
        Quaternion direction = Quaternion.Euler(0, 90, 0);
        Matrix4x4 view = Matrix4x4.Scale(new Vector3(1, 1, -0.5f))
            * Matrix4x4.Rotate(direction).transpose;
        view.SetColumn(3, new Vector4(300, 400, 500, 1));
        viewer.worldToCameraMatrix = view;
        a.transform.SetPositionAndRotation(direction * new Vector3(0, 0, distance),
            direction * Quaternion.Euler(0, 180, 0));
        Assert.That(Planner(Renderers[a])(viewer, 1, out _), Is.EqualTo(expected));
    }

    [TestCase(0, 1)]
    [TestCase(1, 2)]
    [TestCase(2, 3)]
    public void FaceToFaceKeepsRequestedPrefixAndNeverSamplesActiveTarget(int depth, int levels)
    {
        FaceToFace(); a.recursionDepth = depth; Tick();
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(levels));
        Camera[] cameras = a.GetComponentsInChildren<Camera>(true);
        for (int i = 0; i < cameras.Length; i++)
        {
            Begin(cameras[i]);
            Assert.That(a.ViewTexture, Is.SameAs(b.ViewTexture));
            Assert.That(a.ViewTexture, Is.Not.SameAs(cameras[i].targetTexture));
            if (i + 1 < levels)
            {
                Assert.That(a.ViewTexture, Is.SameAs(cameras[i + 1].targetTexture));
                Assert.That(cameras[i + 1].depth, Is.LessThan(cameras[i].depth));
            }
            else Assert.That(a.ViewTexture, Is.Null);
        }
        Begin(viewer);
        Assert.That(a.ViewTexture, Is.SameAs(cameras[0].targetTexture));
    }

    [Test]
    public void SchedulerReservesEveryVisibleRootBeforeRecursion()
    {
        a.cullWhenOffscreen = false; b.cullWhenOffscreen = false;
        PortalSystem.Budget = 2; Tick();
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(1));
        Assert.That(Renderers[b].LevelCount, Is.EqualTo(1));
    }

    [TestCase(0, false)]
    [TestCase(1, false)]
    [TestCase(0, true)]
    [TestCase(1, true)]
    public void LargeDepthPlanningIsBoundedByAvailableBudget(int budget, bool optOut)
    {
        FaceToFace();
        // Конечная видимая цепочка делает RED безопасным; opt-out также проверяет int.MaxValue.
        int depth = optOut ? int.MaxValue : 16384;
        a.recursionDepth = depth;
        a.cullWhenOffscreen = !optOut;
        PortalSystem.Budget = budget;
        Tick();
        var system = Object.FindFirstObjectByType<PortalSystem>();
        var wanted = (int[])typeof(PortalSystem).GetField("_wanted", Hidden).GetValue(system);
        Assert.That(wanted[0], Is.LessThanOrEqualTo(budget), "Plan must not visit levels that cannot receive budget.");
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(budget));
        Assert.That(a.GetComponentsInChildren<Camera>(true), Has.Length.EqualTo(budget));
        Assert.That(a.recursionDepth, Is.EqualTo(depth));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void StarvedRootBindsFallbackAndNoDepthEvenWithoutPriorAllocation(bool allocated)
    {
        if (allocated) { Tick(); Begin(viewer); Assert.That(PortalSystem.HasContentBuffers(a), Is.True); }
        PortalSystem.Budget = 0; Tick();
        a.SetViewTexture(Texture2D.whiteTexture);
        b.SetViewTexture(Texture2D.whiteTexture);
        Camera foreign = Host("Foreign Virtual").AddComponent<Camera>();
        Begin(foreign);
        Assert.That(a.ViewTexture, Is.SameAs(Texture2D.whiteTexture));
        Assert.That(b.ViewTexture, Is.SameAs(Texture2D.whiteTexture));
        Begin(viewer);
        Assert.That(a.ViewTexture, Is.Null);
        Assert.That(PortalSystem.HasContentBuffers(a), Is.False);
        Assert.That(EnabledCameras(a), Is.Zero);
        var block = new MaterialPropertyBlock(); a.screen.GetPropertyBlock(block);
        Assert.That(block.GetFloat("_HasTexture"), Is.Zero);
        Assert.That(block.GetTexture("_ContentDepth"), Is.SameAs(Texture2D.blackTexture));
        if (!allocated) Assert.That(a.GetComponentsInChildren<Camera>(true), Is.Empty);
    }

    [Test]
    public void InactiveExitTransformStillSupportsRootRendering()
    {
        b.gameObject.SetActive(false); Tick();
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(1));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DisplacedEitherScreenRetainsFullPrefixBeforePhysicalRejection(bool own)
    {
        (own ? a : b).screen.transform.localPosition = new Vector3(0, 0, -0.2f);
        Renderers[a].Render(viewer, 3);
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(3));
    }

    [TestCase(0.3f)]
    [TestCase(0.000003f)]
    public void CustomProjectionRetainsConservativePrefix(float shift)
    {
        Matrix4x4 projection = viewer.projectionMatrix; projection.m02 = shift;
        viewer.projectionMatrix = projection;
        Renderers[a].Render(viewer, 3);
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(3));
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    public void TaaDoesNotAllocateBackFacingBehindEyeOrDistantOffscreenRoots(int scenario)
    {
        Component data = viewer.GetComponent(FindType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData"));
        FieldInfo aa = data.GetType().GetField("antialiasing");
        aa.SetValue(data, Enum.Parse(aa.FieldType, "TemporalAntialiasing"));
        if (scenario == 0) a.transform.rotation = Quaternion.identity;
        if (scenario == 1) a.transform.SetPositionAndRotation(new Vector3(0, 0, -2), Quaternion.identity);
        if (scenario == 2) a.transform.position = new Vector3(100, 0, 2);
        Tick();
        Assert.That(Renderers[a].LevelCount, Is.Zero);
        Assert.That(Renderers[b].LevelCount, Is.Zero);
        Assert.That(a.GetComponentsInChildren<Camera>(true), Is.Empty);
        Assert.That(b.GetComponentsInChildren<Camera>(true), Is.Empty);
    }

    [Test]
    public void VisibleTaaRootRetainsItsConservativeChildPrefix()
    {
        Component data = viewer.GetComponent(FindType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData"));
        FieldInfo aa = data.GetType().GetField("antialiasing");
        aa.SetValue(data, Enum.Parse(aa.FieldType, "TemporalAntialiasing"));
        Tick();
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(3));
        Assert.That(Renderers[b].LevelCount, Is.Zero);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RootWhollyBeyondFarPlaneDoesNotAllocate(bool taa)
    {
        if (taa)
        {
            Component data = viewer.GetComponent(FindType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData"));
            FieldInfo aa = data.GetType().GetField("antialiasing");
            aa.SetValue(data, Enum.Parse(aa.FieldType, "TemporalAntialiasing"));
        }
        a.transform.position = new Vector3(0, 0, viewer.farClipPlane + 10);
        Tick();
        Assert.That(Renderers[a].LevelCount, Is.Zero);
        Assert.That(a.GetComponentsInChildren<Camera>(true), Is.Empty);
    }

    [TestCase(-0.1f)]
    [TestCase(0f)]
    [TestCase(0.1f)]
    public void PhysicalRootTouchingOrStraddlingFarPlaneIsRetained(float offset)
    {
        viewer.farClipPlane = 10;
        a.transform.SetPositionAndRotation(new Vector3(0, 0, 10 + offset), Quaternion.Euler(0, 135, 0));
        Tick();
        Assert.That(Renderers[a].LevelCount, Is.GreaterThanOrEqualTo(1));
    }

    [Test]
    public void HdrpDynamicResolutionKeepsVisibleChildPrefix()
    {
        Component data = viewer.GetComponent(FindType("UnityEngine.Rendering.HighDefinition.HDAdditionalCameraData"));
        data.GetType().GetField("allowDynamicResolution").SetValue(data, true);
        Tick();
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(3));
        Assert.That(Renderers[b].LevelCount, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(1)]
    public void MaximumSerializedDepthWithVisibleRecursionOnlyPlansBudget(int budget)
    {
        FaceToFace(); a.recursionDepth = int.MaxValue;
        PortalSystem.Budget = budget;
        Tick();
        Assert.That(Renderers[a].LevelCount, Is.EqualTo(budget));
        Assert.That(a.recursionDepth, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void DisplacedChildDoesNotMakeAnInvisibleRootVisible()
    {
        a.transform.position = new Vector3(100, 0, 2);
        b.screen.transform.localPosition = new Vector3(0, 0, -0.2f);
        Renderers[a].Render(viewer, 3);
        Assert.That(Renderers[a].LevelCount, Is.Zero);
    }

    [Test]
    public void ReentryReusesCapacityAndResetsHdrpHistoryAfterNewPose()
    {
        FaceToFace(); Tick();
        Camera[] before = a.GetComponentsInChildren<Camera>(true);
        Type hd = FindType("UnityEngine.Rendering.HighDefinition.HDCamera");
        object history = hd.GetMethod("GetOrCreate", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { before[1], 0 });
        FieldInfo valid = hd.GetField("colorPyramidHistoryIsValid"); valid.SetValue(history, true);
        Renderers[a].Render(viewer, 1);
        Assert.That(before[1].enabled, Is.False);
        Renderers[a].Render(viewer, 0);
        viewer.transform.position = new Vector3(0.1f, 0, 0);
        Renderers[a].Render(viewer, 3);
        CollectionAssert.AreEqual(before, a.GetComponentsInChildren<Camera>(true));
        Assert.That((bool)valid.GetValue(history), Is.False);
        Assert.That(before[1].enabled, Is.True);
        Assert.That(before[1].transform.position.x, Is.EqualTo(0.1f).Within(1e-4));
    }

    [UnityTearDown]
    public IEnumerator TearDown()
    {
        for (int i = owned.Count - 1; i >= 0; i--) if (owned[i] != null) Object.Destroy(owned[i]);
        owned.Clear();
        PortalSystem.Budget = previousBudget;
        yield return null;
        yield return new ExitPlayMode();
    }
}
