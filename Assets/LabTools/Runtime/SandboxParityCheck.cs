using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using Portals.Lab.Validation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

[DefaultExecutionOrder(2000)]
public sealed class SandboxParityCheck : MonoBehaviour
{
    public Shader leakageShader;
    private PortalCheckRun run;
    private SandboxProbeContext context;
    private Portal entrance;
    private string mode;
    private bool regularProjection;
    private readonly List<SandboxParitySample> samples = new List<SandboxParitySample>();
    private readonly PortalLeakageControl control = new PortalLeakageControl();
    private GameObject marker;
    private Material markerMaterial;
    private static readonly int ContentDepthId = Shader.PropertyToID("_ContentDepth");
    private static readonly int InverseProjectionId = Shader.PropertyToID("_PortalInverseProjection");
    private MaterialPropertyBlock projectionBlock;
    private readonly ProjectionAudit projectionAudit = new ProjectionAudit();

    [Serializable]
    private sealed class ProjectionAudit
    {
        public int mainBindings, lastBindingFrame;
        public float maxElementError;
        public string portal, rootCamera, depthTexture, failureReason = string.Empty;
        public Matrix4x4 expectedInverse, boundInverse;
    }

    private IEnumerator Start()
    {
        run = PortalCheckRun.Current;
        if (run == null || run.IsCompleted || Application.isEditor) yield break;
        string invalid = null;
        try
        {
            context = new SandboxProbeContext(run, 1280, 720);
            entrance = Array.Find(context.Portals, portal => portal.name == "Portal_ToRoomA");
            if (entrance == null || entrance.exitPortal == null) invalid = "Sandbox paired reference portal is missing.";
        }
        catch (Exception error) { invalid = error.Message; }
        if (invalid != null) { Finish(new PortalCheckDecision("Blocked", invalid)); yield break; }
        yield return SandboxProbeContext.Guard(Measure(), run, error =>
        {
            run.RecordFailure("Sandbox parity probe failed: " + error.Message);
            Finish(new PortalCheckDecision("Failed", error.Message));
        });
    }

    private IEnumerator Measure()
    {
        Matrix4x4 mapping = PortalMath.EntranceToExit(entrance.transform, entrance.exitPortal.transform);
        Vector3 eye = entrance.transform.position + Vector3.up * 0.1f + entrance.transform.forward * 0.02f;
        Quaternion rotation = Quaternion.LookRotation(-entrance.transform.forward, Vector3.up);
        Vector3 direct = mapping.MultiplyPoint3x4(eye);
        if (Vector3.Distance(eye, new Vector3(40, 1.6f, -5.98f)) > 0.001f
            || Vector3.Distance(direct, new Vector3(0, 1.6f, 6.02f)) > 0.001f
            || Quaternion.Angle(rotation, Quaternion.Euler(0, 180, 0)) > 0.01f
            || Quaternion.Angle(mapping.rotation * rotation, Quaternion.Euler(0, 180, 0)) > 0.01f)
            context.Problem = "Sandbox paired poses differ from the archived fixture.";
        RectInt roi = PortalImageMetrics.FromTopLeft(1280, 720, 480, 260, 320, 200);
        foreach (string nextMode in SandboxParityPolicy.Modes)
        {
            foreach (string aa in new[] { "none", "taa" })
            {
                context.PortalsEnabled(false);
                yield return null;
                context.RestoreCamera();
                mode = nextMode;
                regularProjection = mode == "regular-projection";
                context.Data.antialiasing = aa == "none" ? HDAdditionalCameraData.AntialiasingMode.None
                    : HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing;
                var sample = new SandboxParitySample { mode = mode, aa = aa, cameraSettingsValid = true };
                samples.Add(sample);
                string prefix = mode + "/" + aa;
                Color32[] through = null, settled = null, repeated = null;
                int bindingsBefore = projectionAudit.mainBindings;
                context.SetEye(eye, rotation, true);
                yield return SandboxProbeContext.Settle(120);
                yield return context.Capture(prefix + "-through", roi, pixels => { through = pixels; Captured(sample, pixels, true); });
                SaveProjectionAudit(prefix + "-through", bindingsBefore);
                context.SetEye(direct, mapping.rotation * rotation, false);
                yield return SandboxProbeContext.Settle(1);
                yield return context.Capture(prefix + "-direct-first", roi, pixels => Captured(sample, pixels, false));
                yield return SandboxProbeContext.Settle(120);
                yield return context.Capture(prefix + "-direct", roi, pixels => { settled = pixels; Captured(sample, pixels, false); });
                yield return SandboxProbeContext.Settle(120);
                yield return context.Capture(prefix + "-direct-repeat", roi, pixels => { repeated = pixels; Captured(sample, pixels, false); });
                if (through != null && settled != null) sample.comparison = PortalImageMetrics.Compare(through, settled);
                if (settled != null && repeated != null) sample.repeat = PortalImageMetrics.Compare(settled, repeated);
                context.Save(prefix + "-metrics.json", JsonUtility.ToJson(sample, true));
                SaveMetrics();
                run.RecordProgress(context.Captures, 0);
            }
        }
        yield return Leakage();
        context.Save("leakage-control.json", JsonUtility.ToJson(control, true));
        Finish(SandboxParityPolicy.Evaluate(samples.ToArray(), control, context.Problem));
    }

    private void Captured(SandboxParitySample sample, Color32[] pixels, bool through)
    {
        if (pixels != null) sample.captureCount++;
        bool expectedMainAo = mode != "ssao-off-both";
        bool expectedVirtualAo = mode == "baseline" || mode == "regular-projection";
        int virtualCameras = 0;
        bool mainSeen = false;
        foreach (Camera camera in Camera.allCameras)
        {
            if (!camera.isActiveAndEnabled) continue;
            bool virtualCamera = context.IsVirtual(camera);
            if (virtualCamera) virtualCameras++;
            if (camera != context.Main && !virtualCamera) continue;
            if (camera == context.Main) mainSeen = true;
            bool ao = HDCamera.GetOrCreate(camera).frameSettings.IsEnabled(FrameSettingsField.SSAO);
            if (ao != (virtualCamera ? expectedVirtualAo : expectedMainAo)) sample.cameraSettingsValid = false;
        }
        if (!mainSeen || (through ? virtualCameras < 1 : virtualCameras != 0)) sample.cameraSettingsValid = false;
    }

    private void LateUpdate()
    {
        if (context == null || run == null || run.IsCompleted) return;
        foreach (Camera camera in Camera.allCameras)
        {
            bool virtualCamera = context.IsVirtual(camera);
            if (camera != context.Main && !virtualCamera) continue;
            if (virtualCamera && regularProjection)
            {
                camera.ResetProjectionMatrix();
                camera.nonJitteredProjectionMatrix = camera.projectionMatrix;
            }
            if (mode != "ssao-off-both" && !(mode == "ssao-off-virtual-only" && virtualCamera)) continue;
            HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
            if (data == null) { context.Problem = "HDRP camera settings are missing."; continue; }
            data.customRenderingSettings = true;
            data.renderingPathCustomFrameSettingsOverrideMask.mask[(uint)FrameSettingsField.SSAO] = true;
            data.renderingPathCustomFrameSettings.SetEnabled(FrameSettingsField.SSAO, false);
        }
        // PortalSystem(1000) may recreate and resubscribe renderers this frame.
        // Move our handler to the tail only after their preparation, not just at Start.
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    private void OnBeginCameraRendering(ScriptableRenderContext renderContext, Camera camera)
    {
        if (context == null || run == null || run.IsCompleted || !regularProjection || camera != context.Main) return;
        ObserveProjectionBindings(true);
    }

    private int ObserveProjectionBindings(bool synchronize)
    {
        projectionBlock ??= new MaterialPropertyBlock();
        int observed = 0;
        Camera[] cameras = Camera.allCameras;
        foreach (Portal portal in context.Portals)
        {
            if (portal == null || !portal.gameObject.activeInHierarchy || portal.playerCamera != context.Main || portal.ViewTexture == null) continue;
            if (portal.screen == null || !portal.writeContentDepth)
            { ProjectionFailure("Regular projection requires the existing screen and content-depth binding."); continue; }
            Camera root = null;
            int matches = 0;
            foreach (Camera camera in cameras)
                if (camera != context.Main && camera.isActiveAndEnabled && camera.targetTexture == portal.ViewTexture
                    && camera.GetComponentInParent<Portal>() == portal)
                { root = camera; matches++; }
            if (matches != 1)
            { ProjectionFailure("Regular projection cannot identify exactly one bound root virtual camera."); continue; }
            portal.screen.GetPropertyBlock(projectionBlock);
            Texture depth = projectionBlock.GetTexture(ContentDepthId);
            if (depth == null || depth == Texture2D.blackTexture)
            { ProjectionFailure("Regular projection has no bound content-depth texture."); continue; }
            Matrix4x4 projection = root.projectionMatrix;
            if (!PortalCheckPolicy.Finite(projection.determinant) || Mathf.Abs(projection.determinant) < 1e-12f)
            { ProjectionFailure("Regular virtual projection is not invertible."); continue; }
            Matrix4x4 expected = GL.GetGPUProjectionMatrix(projection, true).inverse;
            // The production main-camera callback has restored ViewTexture and depth.
            // Preserve that texture and all other properties; only the inverse changes.
            if (synchronize) portal.SetContentBuffers(depth, expected);
            portal.screen.GetPropertyBlock(projectionBlock);
            Matrix4x4 bound = projectionBlock.GetMatrix(InverseProjectionId);
            float error = 0;
            for (int i = 0; i < 16; i++)
            {
                if (!PortalCheckPolicy.Finite(expected[i]) || !PortalCheckPolicy.Finite(bound[i]))
                { ProjectionFailure("Consumed regular inverse contains non-finite values."); return observed; }
                error = Mathf.Max(error, Mathf.Abs(expected[i] - bound[i]));
            }
            projectionAudit.maxElementError = Mathf.Max(projectionAudit.maxElementError, error);
            projectionAudit.portal = portal.name;
            projectionAudit.rootCamera = root.name;
            projectionAudit.depthTexture = depth.name;
            projectionAudit.expectedInverse = expected;
            projectionAudit.boundInverse = bound;
            if (error > 0.00001f || projectionBlock.GetTexture(ContentDepthId) != depth)
            { ProjectionFailure("Consumed regular inverse or content-depth texture differs from the virtual camera binding."); continue; }
            observed++;
            if (synchronize)
            {
                projectionAudit.mainBindings++;
                projectionAudit.lastBindingFrame = Time.frameCount;
            }
        }
        return observed;
    }

    private void ProjectionFailure(string reason)
    {
        if (string.IsNullOrEmpty(projectionAudit.failureReason)) projectionAudit.failureReason = reason;
        if (string.IsNullOrEmpty(context.Problem)) context.Problem = reason;
    }

    private void SaveProjectionAudit(string name, int bindingsBefore)
    {
        if (!regularProjection) return;
        // Capture runs at end of frame: re-read, do not repair a later overwrite.
        if (ObserveProjectionBindings(false) == 0 || projectionAudit.mainBindings <= bindingsBefore
            || projectionAudit.lastBindingFrame != Time.frameCount)
            ProjectionFailure("Regular capture has no demonstrated synchronized main-camera binding.");
        context.Save(name + "-projection-audit.json", JsonUtility.ToJson(projectionAudit, true));
    }

    private IEnumerator Leakage()
    {
        context.PortalsEnabled(false);
        yield return null;
        context.RestoreCamera();
        context.Data.antialiasing = HDAdditionalCameraData.AntialiasingMode.None;
        mode = "baseline";
        regularProjection = false;
        Vector3 eye = entrance.transform.position + Vector3.up * 0.1f + entrance.transform.forward;
        Quaternion rotation = Quaternion.LookRotation(-entrance.transform.forward, Vector3.up);
        Matrix4x4 mapping = PortalMath.EntranceToExit(entrance.transform, entrance.exitPortal.transform);
        Vector3 mapped = mapping.MultiplyPoint3x4(eye);
        Vector3 forward = mapping.MultiplyVector(rotation * Vector3.forward).normalized;
        Vector3 position = PortalImageMetrics.LeakageMarkerPosition(mapped, forward, entrance.exitPortal.transform.position,
            entrance.exitPortal.transform.forward);
        if (leakageShader == null || !leakageShader.isSupported)
        { control.reason = "HDRP Unlit shader is missing or unsupported."; yield break; }
        context.SetEye(eye, rotation, true);
        yield return SandboxProbeContext.Settle(120);
        Color32[] backgroundOblique = null, backgroundRegular = null, oblique = null, regular = null;
        yield return context.Capture("leakage/background-oblique", null, pixels => backgroundOblique = pixels);
        marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "SandboxParityLeakageMarker";
        Destroy(marker.GetComponent<Collider>());
        marker.transform.SetPositionAndRotation(position, mapping.rotation * rotation);
        marker.transform.localScale = Vector3.one * 0.18f;
        // Яркость учитывает EV11: виртуальная камера отдаёт непреэкспонированное содержимое.
        markerMaterial = new Material(leakageShader) { name = "SandboxParityOwnedEmissive" };
        markerMaterial.SetColor("_UnlitColor", Color.black);
        markerMaterial.SetColor("_EmissiveColor", new Color(8192, 0, 8192, 1));
        markerMaterial.SetColor("_EmissiveColorLDR", Color.magenta);
        markerMaterial.SetFloat("_EmissiveExposureWeight", 1);
        marker.GetComponent<Renderer>().sharedMaterial = markerMaterial;
        context.Save("leakage/fixture.txt", "Separate 1m standoff control; emission RGB=(8192,0,8192), exposureWeight=1.\n"
            + "Classifier: r,b>=128; g<=96; r-g,b-g>=64; exclude magenta in projection-matched background.\n"
            + "Mapped eye=" + mapped.ToString("F6") + "; marker=" + position.ToString("F6") + "\n");
        yield return SandboxProbeContext.Settle(120);
        yield return context.Capture("leakage/oblique-marker", null, pixels => oblique = pixels);
        regularProjection = true;
        int bindingsBefore = projectionAudit.mainBindings;
        PortalSystem.ResetHistory();
        yield return SandboxProbeContext.Settle(120);
        yield return context.Capture("leakage/regular-positive-marker", null, pixels => regular = pixels);
        SaveProjectionAudit("leakage/regular-positive-marker", bindingsBefore);
        marker.SetActive(false);
        bindingsBefore = projectionAudit.mainBindings;
        yield return SandboxProbeContext.Settle(120);
        yield return context.Capture("leakage/background-regular", null, pixels => backgroundRegular = pixels);
        SaveProjectionAudit("leakage/background-regular", bindingsBefore);
        control.completed = oblique != null && regular != null && backgroundOblique != null && backgroundRegular != null;
        if (control.completed)
        {
            control.obliquePixels = PortalImageMetrics.CountNewMagenta(oblique, backgroundOblique);
            control.regularPixels = PortalImageMetrics.CountNewMagenta(regular, backgroundRegular);
            control.fixtureValid = control.regularPixels > 0;
        }
        control.reason = control.fixtureValid ? "Regular projection is a positive control, not a shipped fix."
            : "No demonstrated exposed marker pixels; fixture is not valid.";
        Debug.Log("[SandboxLeakageControl] " + JsonUtility.ToJson(control));
        Destroy(marker); marker = null;
        Destroy(markerMaterial); markerMaterial = null;
        regularProjection = false;
        context.RestoreCamera();
        yield return null;
    }

    private void SaveMetrics()
    {
        var csv = new StringBuilder("mode,aa,comparison,red_mae_8bit,green_mae_8bit,blue_mae_8bit,max_channel_8bit,pixels,captures\n");
        foreach (SandboxParitySample sample in samples)
            foreach (bool repeat in new[] { false, true })
            {
                PortalImageDifference image = repeat ? sample.repeat : sample.comparison;
                csv.Append(sample.mode).Append(',').Append(sample.aa).Append(',').Append(repeat ? "direct-repeat" : "through-direct").Append(',')
                    .Append(PortalPerformanceMetrics.Format(image?.redMae)).Append(',').Append(PortalPerformanceMetrics.Format(image?.greenMae)).Append(',')
                    .Append(PortalPerformanceMetrics.Format(image?.blueMae)).Append(',').Append(PortalPerformanceMetrics.Format(image?.maxChannelDifference)).Append(',')
                    .Append(image == null ? "null" : image.pixelCount.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(',')
                    .Append(sample.captureCount).Append('\n');
            }
        context.Save("parity-metrics.csv", csv.ToString());
    }

    private void Finish(PortalCheckDecision decision)
    {
        if (run == null || run.IsCompleted) return;
        if (context != null)
        {
            SaveMetrics();
            context.Save("projection-audit.json", JsonUtility.ToJson(projectionAudit, true));
            context.Save("parity-summary.txt", decision.status + ": " + decision.failureReason
                + "\nAcceptance: baseline/None only; each channel MAE<=0.15, max<=2 in byte units. ROI bottom-left=(480,260,320,200).\n"
                + "TAA/AO-off/regular projection are diagnostic; dynamic motion is not certified.\n");
        }
        run.Complete("SandboxParity", decision.status, context?.Captures ?? 0, 0, decision.failureReason);
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        if (marker != null) Destroy(marker);
        if (markerMaterial != null) Destroy(markerMaterial);
        context?.Dispose();
        if (run != null && !run.IsCompleted) Finish(new PortalCheckDecision("Blocked", "Parity probe disabled before all modes completed."));
    }

    private void OnDestroy() => RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
}
