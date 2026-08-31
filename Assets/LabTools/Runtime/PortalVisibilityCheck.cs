using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Portals.Lab.Validation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Ограниченная проверка build-copy Sandbox; исходные сцены и профили не сохраняются.
[DefaultExecutionOrder(3000)]
public sealed class PortalVisibilityCheck : MonoBehaviour
{
    private PortalCheckRun run;
    private SandboxProbeContext context;
    private Portal[] roots;
    private Camera[][] cameras, retained;
    private RenderTexture[][] retainedTargets;
    private PortalVisibilitySample observing;
    private bool requireCapacity, subscribed, armResetPending, armResetValid = true, trackArmHistory;
    private int expectedVirtualHistory = -1;
    private float previousDepth;
    private readonly List<PortalVisibilitySample> samples = new List<PortalVisibilitySample>();
    private readonly PortalVisibilityMatchedEvidence evidence = new PortalVisibilityMatchedEvidence();
    private readonly List<PortalVisibilityTriple> triples = new List<PortalVisibilityTriple>();
    private readonly PortalVisibilityRenderClock clock = new PortalVisibilityRenderClock();
    private readonly Dictionary<Camera, int> renderedInArm = new Dictionary<Camera, int>();
    private Camera[] metadataCameras;
    private Color32[] captured;
    private int savedBudget;
    private bool reinitializeRegularAo, edgeSweep;
    private RectInt captureRoi = new RectInt(480, 200, 320, 320);
    private Action restoreEdgeFixture;
    private int preparedArms;
    [Serializable]
    private sealed class AoPreparation
    {
        public int arm;
        public string cameraId, currentBefore, previousBefore;
        public bool currentClearedByOwner, previousClearedByOwner;
    }
    [Serializable]
    private sealed class AoPreparations { public AoPreparation[] cameras; }
    private readonly List<AoPreparation> aoPreparations = new List<AoPreparation>();
    private static readonly MethodInfo ReleaseAoHistory = typeof(HDCamera).GetMethod("ReleaseHistoryFrameRT",
        BindingFlags.Instance | BindingFlags.NonPublic, null, new[]{typeof(int)}, null);
    private static readonly FieldInfo HistoryFrame = typeof(HDCamera).GetField("cameraFrameCount", BindingFlags.Instance | BindingFlags.NonPublic);

    private IEnumerator Start()
    {
        run = PortalCheckRun.Current;
        if (run == null || run.IsCompleted || Application.isEditor) yield break;
        string invalid = null;
        try
        {
            context = new SandboxProbeContext(run, 1280, 720);
            if (context.Data.antialiasing != HDAdditionalCameraData.AntialiasingMode.None)
                invalid = "Visibility static certification requires the unchanged Sandbox AA=None setting.";
            savedBudget = PortalSystem.Budget;
            if (HistoryFrame == null) invalid = "Installed HDRP history counter is unavailable.";
            string aoControl = Environment.GetEnvironmentVariable("VISIBILITY_REINITIALIZE_AO_HISTORY");
            reinitializeRegularAo = aoControl == "1";
            if (!string.IsNullOrEmpty(aoControl) && aoControl != "0" && aoControl != "1")
                invalid = "VISIBILITY_REINITIALIZE_AO_HISTORY must be 0 or 1.";
            if (reinitializeRegularAo && !SupportsAoPreparation(Application.unityVersion, run.HdrpVersion))
                invalid = "Regular AO preparation requires Unity 6000.5.9f1/HDRP 17.5.0 and the exact owner release method.";
            string edgeControl = Environment.GetEnvironmentVariable("VISIBILITY_EDGE_SWEEP");
            edgeSweep = edgeControl == "1";
            invalid = invalid ?? EdgeSweepProblem(edgeControl, reinitializeRegularAo);
            evidence.regularAoHistoryReinitialized = reinitializeRegularAo;
        }
        catch (Exception error) { invalid = error.Message; }
        if (invalid != null) { Finish(new PortalCheckDecision("Blocked", invalid)); yield break; }
        yield return SandboxProbeContext.Guard(Measure(), run, error =>
        {
            run.RecordFailure("Visibility probe failed: " + error.Message);
            Finish(new PortalCheckDecision("Failed", error.Message));
        });
    }

    private IEnumerator Measure()
    {
        Portal a = context.Portals.SingleOrDefault(portal => portal.name == "Portal_Facing_A");
        Portal b = context.Portals.SingleOrDefault(portal => portal.name == "Portal_Facing_B");
        if (a == null || b == null || a.exitPortal != b || b.exitPortal != a
            || a.transform.parent != b.transform.parent || a.transform.parent.name != "Recursion_Pair"
            || GameObject.Find("Recursion_Marker") == null
            || Vector3.Distance(a.transform.position, new Vector3(16, 1.5f, 14)) > 0.001f
            || Vector3.Distance(b.transform.position, new Vector3(24, 1.5f, 14)) > 0.001f
            || Quaternion.Angle(a.transform.rotation, Quaternion.Euler(0, 90, 0)) > 0.01f
            || Quaternion.Angle(b.transform.rotation, Quaternion.Euler(0, -90, 0)) > 0.01f)
        { Finish(new PortalCheckDecision("Blocked", "Existing Sandbox recursion fixture does not match the archived pair.")); yield break; }
        if (edgeSweep)
        {
            yield return MeasureEdges(a, b);
            yield break;
        }
        context.Save("visibility-contract.txt",
            "Existing Recursion_Pair and marker; AA=None; unchanged lens/quality/AOV; 320x320 central RGB ROI.\n"
            + "Schema2: independent R1/optimized/R2 arms, identical setup reset, capture after 40 completed main renders.\n"
            + "Reference: cullWhenOffscreen=false, depth4. Optimized: true, depth4. Exact RGB reference equality.\n"
            + "Nonexact reference repeat is unresolved/Blocked, never a relaxed pixel tolerance.\n"
            + "Depth0 positive control: max>=16 byte units and mean RGB MAE>=0.5 in each opening.\n"
            + "Reentry arms: visible40, hidden4, first return1, settled return40; reference Budget0 only while hidden.\n"
            + "No manual history reset or disable/recreate inside hide/reentry or budget starvation.\n"
            + "Parented ordinary side-by-side: depth2 reference3/optimized1; Budget0 pixel-positive control.\n"
            + "Three runtime build-copy pair clones: budgets 0,3,1,4,0,3; expected [000],[111],[001],[112],[000],[111].\n");
        context.Save("regular-ao-history-control.txt", reinitializeRegularAo
            ? "Regular AO history is released through its HDCamera owner only at independent arm setup. AOV histories are not reset by this control. No intervention inside hide/reentry/starvation.\n"
            : "Stock HDCamera.Reset only; regular AO buffers are not explicitly reinitialized.\n");
        foreach (Portal portal in context.Portals)
            if (portal != a && portal != b) portal.gameObject.SetActive(false);
        b.enabled = false;
        a.enabled = true;
        a.recursionDepth = b.recursionDepth = 4;
        roots = new[]{a};
        Action faceA = () => SetPose(new Vector3(20, 1.75f, 14), Quaternion.Euler(0, -90, 0));
        Action faceB = () => SetPose(new Vector3(20, 1.75f, 14), Quaternion.Euler(0, 90, 0));
        yield return StaticTriple(a, "a", faceA, value => evidence.aShallow = value);

        // Переключение стороны относится только к подготовке следующего независимого контроля.
        a.enabled = false; b.enabled = true; roots = new[]{b};
        yield return StaticTriple(b, "b", faceB, value => evidence.bShallow = value);
        b.enabled = false; a.enabled = true; roots = new[]{a};
        yield return ReentryTriple(a, faceA, faceB);

        GameObject pair = a.transform.parent.gameObject;
        pair.SetActive(false);
        yield return null;
        // Клоны создаются после удаления старых камер, в неактивном состоянии.
        roots = new Portal[3];
        Vector3 eye = new Vector3(50, 11.5f, 0);
        SetPose(eye, Quaternion.identity);
        PortalSystem.Budget = 0;
        for (int i = 0; i < roots.Length; i++)
        {
            GameObject clone = Instantiate(pair);
            clone.name = "Visibility_Budget_Pair_" + i;
            Portal[] ends = clone.GetComponentsInChildren<Portal>(true);
            Portal entrance = ends.Single(portal => portal.name == a.name);
            Portal exit = ends.Single(portal => portal.name == b.name);
            entrance.enabled = exit.enabled = false;
            Vector3 offset = new Vector3((i - 1) * 2, 0, 5);
            entrance.transform.SetPositionAndRotation(eye + offset, Quaternion.LookRotation(-offset));
            exit.transform.SetPositionAndRotation(eye - offset, Quaternion.LookRotation(offset));
            foreach (Portal portal in ends)
            {
                portal.playerCamera = context.Main; portal.recursionDepth = 2;
                portal.cullWhenOffscreen = true;
                portal.screen.transform.localPosition = Vector3.zero;
                portal.screen.transform.localScale = new Vector3(0.9f + i * 0.45f, 2, 1);
                portal.CacheOpeningSize();
                portal.SetViewTexture(null); portal.SetContentBuffers(null, Matrix4x4.identity);
            }
            clone.SetActive(true);
            entrance.enabled = true;
            roots[i] = entrance;
        }
        clock.BeginArm(); renderedInArm.Clear(); trackArmHistory = false; armResetValid = true;
        yield return CaptureAt("cold", 4);
        PortalSystem.Budget = 3;
        yield return CaptureAt("roots", 14);
        PortalSystem.Budget = 1;
        yield return CaptureAt("priority", 24);
        PortalSystem.Budget = 4;
        yield return CaptureAt("recursion", 34);
        RememberCapacity();
        PortalSystem.Budget = 0;
        yield return CaptureAt("starved", 38, -1, true);
        PortalSystem.Budget = 3;
        yield return CaptureAt("return", 39, 0, true);
        foreach (Portal root in roots) root.transform.parent.gameObject.SetActive(false);
        yield return null;
        yield return ParentedControl(pair);
        evidence.samples = samples.ToArray();
        evidence.triples = triples.ToArray();
        if (reinitializeRegularAo)
            context.Save("regular-ao-history-preparation.json", JsonUtility.ToJson(
                new AoPreparations { cameras = aoPreparations.ToArray() }, true));
        context.Save("visibility-evidence.json", JsonUtility.ToJson(evidence, true));
        Finish(PortalVisibilityPolicy.EvaluateMatched(evidence, context.Problem));
    }

    // setupPose также подходит для будущего parent/custom-view контроля без замены clock.
    private IEnumerator BeginArm(Portal root, bool optimized, Action setupPose, int depth = 4, int budget = 8)
    {
        yield return null;
        root.recursionDepth = depth;
        root.cullWhenOffscreen = false;
        PortalSystem.Budget = 8;
        setupPose();
        // Одинаковая подготовка capacity до измеряемого reset, включая самый первый reference.
        yield return new WaitForEndOfFrame();
        yield return null;
        root.cullWhenOffscreen = optimized;
        PortalSystem.Budget = budget;
        setupPose();
        HDCamera.GetOrCreate(context.Main, 0).Reset();
        PortalSystem.ResetHistory();
        if (reinitializeRegularAo) PrepareRegularAoHistory(root);
        clock.BeginArm();
        renderedInArm.Clear();
        trackArmHistory = true;
        armResetPending = true;
        armResetValid = true;
        RememberCapacity();
    }

    private static bool SupportsAoPreparation(string unityVersion, string hdrpVersion) =>
        unityVersion == "6000.5.9f1" && hdrpVersion == "17.5.0" && ReleaseAoHistory != null
        && ReleaseAoHistory.ReturnType == typeof(void) && ReleaseAoHistory.DeclaringType == typeof(HDCamera);

    private static string EdgeSweepProblem(string flag, bool regularAo)
    {
        if (!string.IsNullOrEmpty(flag) && flag != "0" && flag != "1")
            return "VISIBILITY_EDGE_SWEEP must be 0 or 1.";
        return flag == "1" && !regularAo ? "VISIBILITY_EDGE_SWEEP=1 requires VISIBILITY_REINITIALIZE_AO_HISTORY=1." : null;
    }

    private static string AoTargetId(RTHandle handle) =>
        handle?.rt == null ? null : handle.rt.GetEntityId().ToString();

    private void PrepareRegularAoHistory(Portal root)
    {
        // Только независимая подготовка LAB: обычный Reset не очищает regular AO.
        // Release вызывается у владельца; заимствованные RTHandle здесь не освобождаются.
        // К этому моменту HDRP уже восстановил regular history system после AOV.
        preparedArms++;
        var prepared = new List<Camera> { context.Main };
        prepared.AddRange(root.GetComponentsInChildren<Camera>(true));
        int ao = (int)HDCameraFrameHistoryType.AmbientOcclusion;
        foreach (Camera camera in prepared)
        {
            HDCamera hd = HDCamera.GetOrCreate(camera, 0);
            var row = new AoPreparation { arm = preparedArms, cameraId = camera.GetEntityId().ToString(),
                currentBefore = AoTargetId(hd.GetCurrentFrameRT(ao)),
                previousBefore = AoTargetId(hd.GetPreviousFrameRT(ao)) };
            try { ReleaseAoHistory.Invoke(hd, new object[] { ao }); }
            catch (Exception error)
            {
                context.Problem = "Regular AO owner preparation failed: " + error.GetBaseException().Message;
                aoPreparations.Add(row);
                return;
            }
            // После release запрашиваем состояние заново, не читаем освобождённый handle.
            row.currentClearedByOwner = hd.GetCurrentFrameRT(ao) == null;
            row.previousClearedByOwner = hd.GetPreviousFrameRT(ao) == null;
            aoPreparations.Add(row);
            if (!row.currentClearedByOwner || !row.previousClearedByOwner)
                context.Problem = "Regular AO history is still present after owner preparation.";
        }
    }

    private IEnumerator StaticTriple(Portal root, string prefix, Action setupPose,
        Action<PortalImageDifference> positive, int depth = 4, bool noViewPositive = false)
    {
        yield return BeginArm(root, false, setupPose, depth);
        yield return CaptureAt(prefix + "-reference-r1", 40, 39, true);
        Color32[] r1 = captured;
        yield return BeginArm(root, true, setupPose, depth);
        yield return CaptureAt(prefix + "-visible", 40, 39, true);
        Color32[] optimized = captured;
        yield return BeginArm(root, false, setupPose, depth);
        yield return CaptureAt(prefix + "-reference-r2", 40, 39, true);
        AddTriple(prefix == "parented" ? prefix : "static-" + prefix, r1, optimized, captured);
        // Положительный контроль независим и больше не меняет lifecycle между R1/O/R2.
        yield return BeginArm(root, true, setupPose, noViewPositive ? depth : 0, noViewPositive ? 0 : 8);
        yield return CaptureAt(prefix + (noViewPositive ? "-no-view" : "-shallow"), 40, 39, true);
        positive(Compare(r1, captured));
    }

    private IEnumerator ReentryTriple(Portal root, Action visiblePose, Action hiddenPose)
    {
        string[] arms = { "r1", "o", "r2" };
        var visible = new Color32[3][];
        var first = new Color32[3][];
        var settled = new Color32[3][];
        for (int i = 0; i < arms.Length; i++)
        {
            bool optimized = i == 1;
            string prefix = "reentry-" + arms[i];
            yield return BeginArm(root, optimized, visiblePose);
            yield return CaptureAt(prefix + "-visible", 40, 39, true);
            visible[i] = captured;
            hiddenPose();
            PortalSystem.Budget = optimized ? 8 : 0;
            yield return CaptureAt(prefix + "-hidden", 44, -1, true);
            visiblePose();
            PortalSystem.Budget = 8;
            // Никаких ручных reset/disable/recreate после начала траектории.
            yield return CaptureAt(prefix + "-first", 45, 0, true);
            first[i] = captured;
            yield return CaptureAt(prefix + "-settled", 84, 39, true);
            settled[i] = captured;
            context.Save(prefix + "-first-vs-settled.json", JsonUtility.ToJson(Compare(first[i], settled[i]), true));
        }
        AddTriple("reentry-visible", visible[0], visible[1], visible[2]);
        AddTriple("reentry-first", first[0], first[1], first[2]);
        AddTriple("reentry-settled", settled[0], settled[1], settled[2]);
    }

    private void AddTriple(string name, Color32[] r1, Color32[] optimized, Color32[] r2)
    {
        var triple = new PortalVisibilityTriple { name = name, referenceRepeat = Compare(r1, r2),
            optimizedVsR1 = Compare(optimized, r1), optimizedVsR2 = Compare(optimized, r2) };
        triples.Add(triple);
        context.Save(name + "-triple.json", JsonUtility.ToJson(triple, true));
    }

    private IEnumerator ParentedControl(GameObject sourcePair)
    {
        Transform player = context.Player;
        Transform oldParent = player.parent;
        Vector3 oldPosition = player.position;
        Quaternion oldRotation = player.rotation;
        var parent = new GameObject("Visibility_Camera_Parent");
        parent.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(11, 23, 5));
        GameObject clone = Instantiate(sourcePair);
        clone.name = "Visibility_Parented_Pair";
        GameObject marker = clone.GetComponentsInChildren<Transform>(true)
            .Single(value => value.name == "Recursion_Marker").gameObject;
        marker.name = "Visibility_Parented_Marker";
        try
        {
            Portal[] ends = clone.GetComponentsInChildren<Portal>(true);
            Portal entrance = ends.Single(portal => portal.name == "Portal_Facing_A");
            Portal exit = ends.Single(portal => portal.name == "Portal_Facing_B");
            Vector3 eye = new Vector3(13.25f, 11.5f, 27.75f);
            Quaternion rotation = Quaternion.Euler(17, 31, 7);
            entrance.transform.SetPositionAndRotation(eye + rotation * new Vector3(0, 0, 2),
                rotation * Quaternion.Euler(0, 180, 0));
            exit.transform.SetPositionAndRotation(eye + rotation * new Vector3(30, 0, 2), rotation);
            foreach (Portal portal in ends)
            {
                portal.enabled = false;
                portal.playerCamera = context.Main;
                portal.recursionDepth = 2;
                portal.screen.transform.localPosition = Vector3.zero;
                portal.CacheOpeningSize();
            }
            marker.transform.SetPositionAndRotation(exit.transform.position + rotation * new Vector3(0, 0, 4), rotation);
            player.SetParent(parent.transform, true);
            clone.SetActive(true);
            entrance.enabled = true;
            roots = new[]{entrance};
            yield return StaticTriple(entrance, "parented", () => SetPose(eye, rotation),
                value => evidence.parentedPositive = value, 2, true);
        }
        finally
        {
            player.SetParent(oldParent, true);
            player.SetPositionAndRotation(oldPosition, oldRotation);
            Destroy(clone); Destroy(parent);
        }
    }

    private IEnumerator MeasureEdges(Portal a, Portal b)
    {
        context.Save("visibility-contract.txt",
            "VISIBILITY_EDGE_SWEEP=1; schema3; exactly 16 captures, not the default 30-capture route.\n"
            + "VISIBILITY_REINITIALIZE_AO_HISTORY=1 required; unchanged AA, effects, pixel thresholds and native lens projection.\n"
            + "Each independent R1/O/R2/positive arm captures completed main render 40 with matching active-camera metadata.\n"
            + "Recursion x=1.499: 3/3/3/1; x=1.6: 3/2/3/1. ROI (768,200,320,320).\n"
            + "Custom viewport and far-straddle: 3/1/3/0. Viewport ROI (960,200,320,320); far ROI (480,200,320,320).\n"
            + "All reference repeats and optimized comparisons exact RGB; each positive max>=16 and mean RGB MAE>=0.5.\n"
            + "Custom linear view compares optimization with existing full-prefix only, not general custom-view correctness.\n");
        context.Save("regular-ao-history-control.txt",
            "Regular AO history is released through its HDCamera owner at each independent edge arm setup; effects remain enabled.\n");
        foreach (Portal portal in context.Portals)
            if (portal != a && portal != b) portal.gameObject.SetActive(false);
        GameObject sourcePair = a.transform.parent.gameObject;
        a.enabled = b.enabled = false;
        sourcePair.SetActive(false);
        yield return null;
        // Не переносим старые виртуальные камеры в build-copy fixture.
        if (sourcePair.GetComponentsInChildren<Camera>(true).Length != 0)
        { Finish(new PortalCheckDecision("Blocked", "Source pair still contains cameras before inactive cloning.")); yield break; }

        var edges = new PortalVisibilityEdgeEvidence { edgeSweep = true,
            regularAoHistoryReinitialized = reinitializeRegularAo, positives = new PortalImageDifference[4] };
        Camera main = context.Main;
        Transform player = context.Player;
        Transform oldParent = player.parent;
        Vector3 oldPosition = player.localPosition, oldScale = player.localScale;
        Quaternion oldRotation = player.localRotation, oldCameraRotation = main.transform.localRotation;
        float oldFar = main.farClipPlane;
        Matrix4x4 oldView = main.worldToCameraMatrix, oldCulling = main.cullingMatrix;
        RectInt oldRoi = captureRoi;
        GameObject parent = null, clone = null;
        restoreEdgeFixture = () =>
        {
            roots = null; cameras = null; observing = null; metadataCameras = null;
            if (clone != null) { clone.SetActive(false); Destroy(clone); }
            player.SetParent(oldParent, false);
            player.localPosition = oldPosition; player.localRotation = oldRotation; player.localScale = oldScale;
            main.transform.localRotation = oldCameraRotation;
            main.farClipPlane = oldFar;
            main.worldToCameraMatrix = oldView; main.cullingMatrix = oldCulling;
            captureRoi = oldRoi;
            if (parent != null) Destroy(parent);
        };
        try
        {
            parent = new GameObject("Visibility_Edge_Camera_Parent");
            parent.transform.SetPositionAndRotation(new Vector3(2, 3, 4), Quaternion.Euler(11, 23, 5));
            player.SetParent(parent.transform, true);
            clone = Instantiate(sourcePair);
            clone.name = "Visibility_Edge_Pair";
            Portal[] ends = clone.GetComponentsInChildren<Portal>(true);
            Portal entrance = ends.Single(portal => portal.name == a.name);
            Portal exit = ends.Single(portal => portal.name == b.name);
            Transform marker = clone.GetComponentsInChildren<Transform>(true).Single(value => value.name == "Recursion_Marker");
            marker.name = "Visibility_Edge_Marker";
            foreach (Portal portal in ends)
            {
                portal.enabled = false;
                portal.playerCamera = main;
                portal.recursionDepth = 2;
                portal.SetViewTexture(null); portal.SetContentBuffers(null, Matrix4x4.identity);
            }
            Vector3 eye = new Vector3(13.25f, 11.5f, 27.75f);
            Quaternion rotation = Quaternion.Euler(17, 31, 7);
            ConfigureEdgeFixture(entrance, exit, marker, eye, rotation, 0, oldFar);
            clone.SetActive(true);
            entrance.enabled = true;
            roots = new[]{entrance};
            string[] names = { "edge-recursion-inside", "edge-recursion-outside", "edge-custom-viewport", "edge-custom-far" };
            for (int i = 0; i < names.Length; i++)
            {
                int index = i;
                captureRoi = i < 2 ? new RectInt(768, 200, 320, 320)
                    : i == 2 ? new RectInt(960, 200, 320, 320) : new RectInt(480, 200, 320, 320);
                Action setup = () => ConfigureEdgeFixture(entrance, exit, marker, eye, rotation, index, oldFar);
                setup();
                context.Save(names[i] + "-fixture.txt", "VISIBILITY_EDGE_SWEEP=1\nROI bottom-left: " + captureRoi
                    + "\nEye: " + eye.ToString("F9") + "\nRotation: " + rotation.ToString("F9")
                    + "\nEntrance: " + entrance.transform.position.ToString("F9")
                    + "\nExit: " + exit.transform.position.ToString("F9") + "\nFar: " + main.farClipPlane
                    + "\nRaw view:\n" + main.worldToCameraMatrix.ToString("F9")
                    + "\nCulling:\n" + main.cullingMatrix.ToString("F9")
                    + "\nNative projection:\n" + main.projectionMatrix.ToString("F9"));
                yield return StaticTriple(entrance, names[i], setup, value => edges.positives[index] = value, 2, i >= 2);
            }
        }
        finally { RestoreEdgeFixture(); }
        edges.samples = samples.ToArray(); edges.triples = triples.ToArray();
        context.Save("regular-ao-history-preparation.json", JsonUtility.ToJson(new AoPreparations { cameras = aoPreparations.ToArray() }, true));
        context.Save("visibility-edge-evidence.json", JsonUtility.ToJson(edges, true));
        Finish(PortalVisibilityPolicy.EvaluateEdges(edges, context.Problem));
    }

    private void ConfigureEdgeFixture(Portal entrance, Portal exit, Transform marker,
        Vector3 eye, Quaternion rotation, int index, float normalFar)
    {
        Camera main = context.Main;
        main.ResetWorldToCameraMatrix(); main.ResetCullingMatrix();
        main.farClipPlane = index == 3 ? 10 : normalFar;
        SetPose(eye, rotation);
        float z = index == 3 ? 5.1f : 2f;
        // При custom view ширина задаётся effective depth=2*z и горизонтальным scale=1.4.
        float x = index == 0 ? 1.499f : index == 1 ? 1.6f : index == 2
            ? 2 * z * Mathf.Tan(main.fieldOfView * Mathf.Deg2Rad * 0.5f) * main.aspect / 1.4f : 0;
        entrance.transform.SetPositionAndRotation(eye + rotation * new Vector3(x, 0, z),
            rotation * Quaternion.Euler(0, index == 3 ? 135 : 180, 0));
        exit.transform.SetPositionAndRotation(eye + rotation * new Vector3(index < 2 ? x : x + 30, 0, index < 2 ? -2 : z), rotation);
        foreach (Portal portal in new[]{entrance, exit})
        {
            portal.screen.transform.localPosition = Vector3.zero;
            portal.screen.transform.localScale = new Vector3(2, 3, 1);
            portal.CacheOpeningSize();
        }
        marker.SetPositionAndRotation(index < 2 ? eye + rotation * new Vector3(x, 0, 0)
            : exit.transform.position + rotation * new Vector3(0, 0, 1), rotation);
        if (index < 2) return;
        Matrix4x4 linear = Matrix4x4.Scale(new Vector3(1.4f, 1, -2)) * Matrix4x4.Rotate(rotation).transpose;
        Matrix4x4 raw = linear;
        raw.SetColumn(3, new Vector4(111, 222, 333, 1));
        main.worldToCameraMatrix = raw;
        // Culling согласован с camera-relative effective view, не с намеренно неверным raw translation.
        main.cullingMatrix = main.projectionMatrix * linear * Matrix4x4.Translate(-eye);
    }

    private void RestoreEdgeFixture()
    {
        // Guard может завершить вложенный arm с ошибкой; cleanup нужен также из Finish/OnDisable.
        Action restore = restoreEdgeFixture;
        restoreEdgeFixture = null;
        restore?.Invoke();
    }

    private void SetPose(Vector3 eye, Quaternion rotation)
    {
        context.Player.SetPositionAndRotation(eye - rotation * context.EyeOffset, rotation);
        context.Main.transform.localRotation = Quaternion.identity;
    }

    private IEnumerator CaptureAt(string mode, int completedMainRenders, int virtualHistory = -1, bool capacity = false)
    {
        // Нормализуем вход в Update, в том числе после предыдущего screenshot в EndOfFrame.
        yield return null;
        while (clock.Completed < completedMainRenders - 1) yield return null;
        if (clock.Completed != completedMainRenders - 1)
        {
            context.Problem = mode + ": missed the requested completed-main-render boundary.";
            captured = null;
            yield break;
        }
        observing = new PortalVisibilitySample { mode = mode, virtualCallbacks = new int[roots.Length],
            bindingsValid = true, capacityValid = true, historyValid = armResetValid };
        expectedVirtualHistory = virtualHistory; requireCapacity = capacity; previousDepth = float.NegativeInfinity;
        yield return context.Capture(mode, captureRoi, value => captured = value);
        observing.completedMainRenders = clock.Completed;
        observing.clockValid = observing.clockValid && clock.Valid && clock.Completed == completedMainRenders;
        samples.Add(observing);
        context.Save(mode + "-observation.json", JsonUtility.ToJson(observing, true));
        Debug.Log("[PortalVisibility] " + JsonUtility.ToJson(observing));
        observing = null;
        run.RecordProgress(context.Captures, 0);
    }

    private void LateUpdate()
    {
        if (context == null || run.IsCompleted || roots == null) return;
        // Observer идёт после подписок production, включая первое создание камер.
        RenderPipelineManager.beginCameraRendering -= OnCamera;
        RenderPipelineManager.beginCameraRendering += OnCamera;
        RenderPipelineManager.endCameraRendering -= OnCameraEnd;
        RenderPipelineManager.endCameraRendering += OnCameraEnd;
        subscribed = true;
        cameras = roots.Select(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
        if (armResetPending)
        {
            armResetValid = History(context.Main) == 0;
            foreach (Camera[] group in cameras)
                foreach (Camera camera in group)
                    if (camera.enabled && History(camera) != 0) armResetValid = false;
            armResetPending = false;
        }
        if (observing == null) return;
        observing.historyValid &= armResetValid;
        observing.clockValid = !trackArmHistory || History(context.Main) == (uint)clock.Completed;
        observing.unityFrame = Time.frameCount;
        observing.time = Time.timeAsDouble;
        observing.unscaledTime = Time.unscaledTimeAsDouble;
        observing.deltaTime = Time.deltaTime;
        observing.smoothDeltaTime = Time.smoothDeltaTime;
        var metadata = new List<PortalVisibilityCameraSample> { ReadCamera(context.Main, -1, -1) };
        var tracked = new List<Camera> { context.Main };
        var state = new List<string>();
        for (int root = 0; root < cameras.Length; root++)
        {
            if (observing.mode == "cold" && cameras[root].Length != 0) observing.capacityValid = false;
            if (requireCapacity && (retained == null || cameras[root].Length != retained[root].Length))
                observing.capacityValid = false;
            for (int i = 0; i < cameras[root].Length; i++)
            {
                Camera camera = cameras[root][i];
                uint historyFrame = History(camera);
                if (camera.enabled && expectedVirtualHistory >= 0 && historyFrame != (uint)expectedVirtualHistory)
                {
                    if (expectedVirtualHistory == 0) observing.historyValid = false;
                    else observing.clockValid = false;
                }
                if (requireCapacity && (retained == null || root >= retained.Length
                    || i >= retained[root].Length || camera != retained[root][i]
                    || camera.targetTexture != retainedTargets[root][i])) observing.capacityValid = false;
                metadata.Add(ReadCamera(camera, root, i));
                tracked.Add(camera);
                state.Add(root + "/" + i + ": camera=" + camera.GetEntityId() + "; target="
                    + (camera.targetTexture == null ? "null" : camera.targetTexture.GetEntityId().ToString())
                    + "; enabled=" + camera.enabled + "; historyBeforeRender=" + historyFrame);
            }
        }
        observing.cameraState = state.ToArray();
        observing.cameraMetadata = metadata.ToArray();
        metadataCameras = tracked.ToArray();
    }

    private static uint History(Camera camera) => (uint)HistoryFrame.GetValue(HDCamera.GetOrCreate(camera, 0));

    private PortalVisibilityCameraSample ReadCamera(Camera camera, int root, int level)
    {
        Vector3 position = camera.transform.position;
        Quaternion rotation = camera.transform.rotation;
        uint history = History(camera);
        renderedInArm.TryGetValue(camera, out int completed);
        return new PortalVisibilityCameraSample { main = root == -1, root = root, level = level,
            enabled = camera.isActiveAndEnabled, cameraId = camera.GetEntityId().ToString(),
            targetId = camera.targetTexture == null ? null : camera.targetTexture.GetEntityId().ToString(),
            historyBefore = history, historyAfter = history, completedRenders = completed,
            position = new[]{position.x, position.y, position.z},
            rotation = new[]{rotation.x, rotation.y, rotation.z, rotation.w},
            view = MatrixValues(camera.worldToCameraMatrix), projection = MatrixValues(camera.projectionMatrix),
            nonJitteredProjection = MatrixValues(camera.nonJitteredProjectionMatrix) };
    }

    private static float[] MatrixValues(Matrix4x4 matrix)
    {
        var values = new float[16];
        for (int i = 0; i < values.Length; i++) values[i] = matrix[i];
        return values;
    }

    private void OnCameraEnd(ScriptableRenderContext renderContext, Camera camera)
    {
        if (context == null || cameras == null) return;
        bool tracked = camera == context.Main;
        foreach (Camera[] group in cameras) tracked |= Array.IndexOf(group, camera) >= 0;
        if (!tracked) return;
        renderedInArm.TryGetValue(camera, out int completed);
        renderedInArm[camera] = completed + 1;
        if (camera == context.Main)
        {
            clock.Complete(Time.frameCount);
            if (!clock.Valid) context.Problem = "Duplicate or out-of-order completed main render.";
        }
        if (observing == null || metadataCameras == null) return;
        int index = Array.IndexOf(metadataCameras, camera);
        if (index < 0) return;
        PortalVisibilityCameraSample row = observing.cameraMetadata[index];
        HDCamera hd = HDCamera.GetOrCreate(camera, 0);
        row.historyAfter = History(camera);
        row.completedRenders = completed + 1;
        row.hdrpTime = hd.time;
        row.view = MatrixValues(camera.worldToCameraMatrix);
        row.projection = MatrixValues(camera.projectionMatrix);
        row.nonJitteredProjection = MatrixValues(camera.nonJitteredProjectionMatrix);
        // Диагностика настроенных эффектов; это не утверждение об их GPU execution.
        var blur = hd.volumeStack?.GetComponent<MotionBlur>();
        var ao = hd.volumeStack?.GetComponent<ScreenSpaceAmbientOcclusion>();
        var grain = hd.volumeStack?.GetComponent<FilmGrain>();
        row.motionBlur = blur != null && blur.IsActive() && hd.frameSettings.IsEnabled(FrameSettingsField.MotionBlur);
        row.aoTemporal = ao != null && ao.temporalAccumulation.value && hd.frameSettings.IsEnabled(FrameSettingsField.MotionVectors);
        row.filmGrain = grain != null && grain.IsActive() && hd.frameSettings.IsEnabled(FrameSettingsField.FilmGrain);
        row.dithering = camera.GetComponent<HDAdditionalCameraData>()?.dithering ?? false;
    }

    private void OnCamera(ScriptableRenderContext renderContext, Camera camera)
    {
        if (observing == null) return;
        if (camera.depth < previousDepth) observing.bindingsValid = false;
        previousDepth = camera.depth;
        if (camera == context.Main)
        {
            observing.mainCallbacks++;
            for (int root = 0; root < roots.Length; root++)
            {
                Camera first = cameras[root].FirstOrDefault(value => value.enabled);
                Texture expected = first == null ? null : first.targetTexture;
                var block = new MaterialPropertyBlock();
                roots[root].screen.GetPropertyBlock(block);
                if (roots[root].ViewTexture != expected || (block.GetFloat("_HasTexture") > 0) != (expected != null))
                    observing.bindingsValid = false;
                if (first == null && (PortalSystem.HasContentBuffers(roots[root])
                    || block.GetTexture("_ContentDepth") != Texture2D.blackTexture
                    || block.GetMatrix("_PortalInverseProjection") != Matrix4x4.identity))
                    observing.bindingsValid = false;
            }
            return;
        }
        for (int root = 0; root < roots.Length; root++)
        {
            int index = Array.IndexOf(cameras[root], camera);
            if (index < 0) continue;
            observing.virtualCallbacks[root]++;
            Texture next = index + 1 < cameras[root].Length && cameras[root][index + 1].enabled
                ? cameras[root][index + 1].targetTexture : null;
            if (roots[root].ViewTexture != next || roots[root].exitPortal.ViewTexture != next
                || next == camera.targetTexture) observing.bindingsValid = false;
            return;
        }
        observing.bindingsValid = false;
    }

    private void RememberCapacity()
    {
        retained = roots.Select(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
        retainedTargets = retained.Select(group => group.Select(camera => camera.targetTexture).ToArray()).ToArray();
    }

    private PortalImageDifference Compare(Color32[] a, Color32[] b) =>
        a == null || b == null ? null : PortalImageMetrics.Compare(a, b);

    private void Finish(PortalCheckDecision decision)
    {
        RestoreEdgeFixture();
        if (run != null && !run.IsCompleted)
            run.Complete("Visibility", decision.status, context?.Captures ?? 0, 0, decision.failureReason);
    }

    private void OnDisable()
    {
        RestoreEdgeFixture();
        if (subscribed)
        {
            RenderPipelineManager.beginCameraRendering -= OnCamera;
            RenderPipelineManager.endCameraRendering -= OnCameraEnd;
        }
        if (context != null) { PortalSystem.Budget = savedBudget; context.Dispose(); }
        if (run != null && !run.IsCompleted) Finish(new PortalCheckDecision("Blocked", "Visibility probe disabled before completion."));
    }
}
