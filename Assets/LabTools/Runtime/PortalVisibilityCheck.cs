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
    private bool requireReset, requireCapacity, subscribed;
    private float previousDepth;
    private readonly List<PortalVisibilitySample> samples = new List<PortalVisibilitySample>();
    private readonly PortalVisibilityEvidence evidence = new PortalVisibilityEvidence();
    private Color32[] captured;
    private int savedBudget;
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
        context.Save("visibility-contract.txt",
            "Existing Recursion_Pair and marker; AA=None; unchanged lens/quality/AOV; 320x320 central RGB ROI.\n"
            + "Reference: cullWhenOffscreen=false, depth4. Optimized: true, depth4. Exact RGB reference equality.\n"
            + "Depth0 positive control: max>=16 byte units and mean RGB MAE>=0.5 in each opening.\n"
            + "No manual history reset or disable/recreate on hide/reentry or budget starvation.\n"
            + "Three runtime build-copy pair clones: budgets 0,3,1,4,0,3; expected [000],[111],[001],[112],[000],[111].\n");
        foreach (Portal portal in context.Portals)
            if (portal != a && portal != b) portal.gameObject.SetActive(false);
        b.enabled = false;
        a.enabled = true;
        a.recursionDepth = b.recursionDepth = 4;
        roots = new[]{a};
        PortalSystem.Budget = 8;
        SetPose(new Vector3(20, 1.75f, 14), -90);
        a.cullWhenOffscreen = false;
        yield return Capture("a-reference", 40);
        Color32[] referenceA = captured;
        a.recursionDepth = 0;
        yield return Capture("a-shallow", 40);
        evidence.aShallow = Compare(referenceA, captured);
        a.recursionDepth = 4; a.cullWhenOffscreen = true;
        yield return Capture("a-visible", 40);
        evidence.aReference = Compare(referenceA, captured);
        RememberCapacity();
        SetPose(new Vector3(20, 1.75f, 14), 90);
        yield return Capture("hidden", 4, false, true);
        SetPose(new Vector3(20, 1.75f, 14), -90);
        yield return Capture("reentry-first", 0, true, true);
        Color32[] firstReturn = captured;
        yield return Capture("reentry-settled", 40, false, true);
        evidence.reentrySettled = Compare(referenceA, captured);
        context.Save("reentry-first-vs-settled.json", JsonUtility.ToJson(Compare(firstReturn, captured), true));

        // Переключение стороны относится только к подготовке следующего независимого контроля.
        a.enabled = false; b.enabled = true; roots = new[]{b};
        SetPose(new Vector3(20, 1.75f, 14), 90);
        b.cullWhenOffscreen = false;
        yield return Capture("b-reference", 40);
        Color32[] referenceB = captured;
        b.recursionDepth = 0;
        yield return Capture("b-shallow", 40);
        evidence.bShallow = Compare(referenceB, captured);
        b.recursionDepth = 4; b.cullWhenOffscreen = true;
        yield return Capture("b-visible", 40);
        evidence.bReference = Compare(referenceB, captured);

        GameObject pair = a.transform.parent.gameObject;
        pair.SetActive(false);
        yield return null;
        // Клоны создаются после удаления старых камер, в неактивном состоянии.
        roots = new Portal[3];
        Vector3 eye = new Vector3(50, 11.5f, 0);
        SetPose(eye, 0);
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
        yield return Capture("cold", 4);
        PortalSystem.Budget = 3;
        yield return Capture("roots", 10);
        PortalSystem.Budget = 1;
        yield return Capture("priority", 10);
        PortalSystem.Budget = 4;
        yield return Capture("recursion", 10);
        RememberCapacity();
        PortalSystem.Budget = 0;
        yield return Capture("starved", 4, false, true);
        PortalSystem.Budget = 3;
        yield return Capture("return", 0, true, true);
        evidence.samples = samples.ToArray();
        context.Save("visibility-evidence.json", JsonUtility.ToJson(evidence, true));
        Finish(PortalVisibilityPolicy.Evaluate(evidence, context.Problem));
    }

    private void SetPose(Vector3 eye, float yaw)
    {
        Quaternion rotation = Quaternion.Euler(0, yaw, 0);
        context.Player.SetPositionAndRotation(eye - rotation * context.EyeOffset, rotation);
        context.Main.transform.localRotation = Quaternion.identity;
    }

    private IEnumerator Capture(string mode, int settle, bool reset = false, bool capacity = false)
    {
        yield return SandboxProbeContext.Settle(settle);
        observing = new PortalVisibilitySample { mode = mode, virtualCallbacks = new int[roots.Length],
            bindingsValid = true, capacityValid = true, historyValid = true };
        requireReset = reset; requireCapacity = capacity; previousDepth = float.NegativeInfinity;
        yield return context.Capture(mode, new RectInt(480, 200, 320, 320), value => captured = value);
        samples.Add(observing);
        context.Save(mode + "-observation.json", JsonUtility.ToJson(observing, true));
        Debug.Log("[PortalVisibility] " + JsonUtility.ToJson(observing));
        observing = null;
        run.RecordProgress(context.Captures, 0);
    }

    private void LateUpdate()
    {
        if (context == null || run.IsCompleted) return;
        // Observer идёт после подписок production, включая первое создание камер.
        RenderPipelineManager.beginCameraRendering -= OnCamera;
        RenderPipelineManager.beginCameraRendering += OnCamera;
        subscribed = true;
        if (observing == null) return;
        cameras = roots.Select(root => root.GetComponentsInChildren<Camera>(true)).ToArray();
        var state = new List<string>();
        for (int root = 0; root < cameras.Length; root++)
        {
            if (observing.mode == "cold" && cameras[root].Length != 0) observing.capacityValid = false;
            if (requireCapacity && (retained == null || cameras[root].Length != retained[root].Length))
                observing.capacityValid = false;
            for (int i = 0; i < cameras[root].Length; i++)
            {
                Camera camera = cameras[root][i];
                HDCamera history = HDCamera.GetOrCreate(camera);
                uint historyFrame = (uint)HistoryFrame.GetValue(history);
                if (requireReset && camera.enabled && historyFrame != 0) observing.historyValid = false;
                if (requireCapacity && (i >= retained[root].Length || camera != retained[root][i]
                    || camera.targetTexture != retainedTargets[root][i])) observing.capacityValid = false;
                state.Add(root + "/" + i + ": camera=" + camera.GetEntityId() + "; target="
                    + (camera.targetTexture == null ? "null" : camera.targetTexture.GetEntityId().ToString())
                    + "; enabled=" + camera.enabled + "; historyBeforeRender=" + historyFrame);
            }
        }
        observing.cameraState = state.ToArray();
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
        if (run != null && !run.IsCompleted)
            run.Complete("Visibility", decision.status, context?.Captures ?? 0, 0, decision.failureReason);
    }

    private void OnDisable()
    {
        if (subscribed) RenderPipelineManager.beginCameraRendering -= OnCamera;
        if (context != null) { PortalSystem.Budget = savedBudget; context.Dispose(); }
        if (run != null && !run.IsCompleted) Finish(new PortalCheckDecision("Blocked", "Visibility probe disabled before completion."));
    }
}
