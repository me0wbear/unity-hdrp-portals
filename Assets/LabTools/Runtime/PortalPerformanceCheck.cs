using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Portals.Lab.Validation;
using Unity.Profiling;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

public sealed class PortalPerformanceCheck : MonoBehaviour
{
    private const int WarmupFrames = 180;
    private const int SampleFrames = 360;
    private PortalCheckRun run;
    private SandboxProbeContext context;
    private ProfilerRecorder draws, setPass, aovGpu, aovCpu;
    private Recorder aovSampler;
    private bool samplerWasEnabled, subscribed, disposed;
    private int callbacks, totalFrames;
    private ulong previousTimestamp;
    private readonly FrameTiming[] timingBuffer = new FrameTiming[1];
    private readonly List<PortalPerformanceSample> samples = new List<PortalPerformanceSample>();
    private readonly PortalImageDifference[] roi = new PortalImageDifference[2];
    private readonly List<string> summaryRows = new List<string>();
    private readonly HashSet<string> discovered = new HashSet<string>();
    private Color32[] depth2Pixels;
    private const string SummaryHeader = "round,mode,frame_samples,frame_median_ms,frame_p95_ms,gpu_samples,gpu_median_ms,gpu_p95_ms,cpu_samples,cpu_median_ms,cpu_p95_ms,main_samples,main_median_ms,main_p95_ms,render_samples,render_median_ms,render_p95_ms,draw_samples,draw_median,draw_p95,setpass_samples,setpass_median,setpass_p95,aov_gpu_samples,aov_gpu_median_ms,aov_gpu_p95_ms,aov_execution_samples,aov_executions_max,main_cameras,virtual_cameras,aov_requests,target_pixels,begin_camera_callbacks_median,begin_camera_callbacks_max\n";

    private IEnumerator Start()
    {
        run = PortalCheckRun.Current;
        if (run == null || run.IsCompleted || Application.isEditor) yield break;
        string invalid = null;
        try { context = new SandboxProbeContext(run, 1920, 1080); }
        catch (Exception error) { invalid = error.Message; }
        if (invalid != null) { Finish(new PortalCheckDecision("Blocked", invalid)); yield break; }
        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = -1;
        Application.runInBackground = true;
        RenderPipelineManager.beginCameraRendering += OnCamera;
        subscribed = true;
        yield return SandboxProbeContext.Guard(MeasureAll(), run, error =>
        {
            run.RecordFailure("Performance probe failed: " + error.Message);
            Finish(new PortalCheckDecision("Failed", error.Message));
        });
    }

    private IEnumerator MeasureAll()
    {
        context.Save("performance-contract.txt", "1920x1080; VSync=0; targetFrameRate=-1; runInBackground=true.\n"
            + "Two rounds; each mode has 180 warmup and 360 timed frames. Camera callback count excludes AOV executions.\n"
            + "An untimed enabled-mode setup frame permits lazy marker registration; discovery/storage/I/O precede all warmup frames.\n"
            + "CPU AOV execution evidence: ProfilerRecorderSample.Count, cross-checked against Recorder.sampleBlockCount.\n"
            + "Empty CPU recorder only yields measured zero when a valid enabled CPU sampler reports zero blocks. Otherwise null.\n"
            + "GPU AOV: GpuRecorder HDRenderPipelineRenderAOV ns/1000000; gpuFrameTime is not total portal GPU cost.\n"
            + "Published top-left ROI=(865,420,190,290); Texture2D bottom-left=(865,370,190,290).\n");
        for (int round = 0; round < 2; round++)
        {
            depth2Pixels = null;
            foreach (string mode in PortalPerformancePolicy.Modes) yield return Measure(round, mode);
        }
        Finish(PortalPerformancePolicy.Evaluate(samples.ToArray(), roi, context.Problem));
    }

    private IEnumerator Measure(int round, string mode)
    {
        bool enabled = mode != "off";
        int depth = mode == "off" || mode.StartsWith("depth0", StringComparison.Ordinal) ? 0 : 2;
        bool contentDepth = !mode.EndsWith("no-aov", StringComparison.Ordinal);
        int divider = mode == "depth2-divider2" ? 2 : 1;
        float yaw = mode == "behind" ? 180 : 0;
        // AOV requests принадлежат созданной камере: сначала Release через OnDisable, затем новый режим.
        context.PortalsEnabled(false);
        yield return null;
        context.Player.SetPositionAndRotation(new Vector3(0, 0.1f, -3.5f), Quaternion.Euler(0, yaw, 0));
        context.Main.transform.localRotation = Quaternion.identity;
        if (Vector3.Distance(context.Main.transform.position, new Vector3(0, 1.75f, -3.5f)) > 0.001f)
            context.Problem = "Performance camera eye differs from the archived (0,1.75,-3.5) pose.";
        foreach (Portal portal in context.Portals)
        {
            portal.recursionDepth = depth;
            portal.resolutionDivider = divider;
            portal.writeContentDepth = contentDepth;
            portal.gameObject.SetActive(enabled);
        }
        HDCamera.GetOrCreate(context.Main).Reset();
        PortalSystem.ResetHistory();
        // Let the newly enabled cameras execute AOV once before discovering lazy markers.
        yield return null;
        DiscoverCounters();
        var frames = new double[SampleFrames];
        var callbackSamples = new double[SampleFrames];
        var gpu = new List<double>(SampleFrames);
        var cpu = new List<double>(SampleFrames);
        var main = new List<double>(SampleFrames);
        var render = new List<double>(SampleFrames);
        var drawValues = new List<double>(SampleFrames);
        var passValues = new List<double>(SampleFrames);
        var aovValues = new List<double>(SampleFrames);
        var executions = new List<double>(SampleFrames);
        var raw = new RawFrame[SampleFrames];
        bool executionDisagreement = false;
        int setupCompletedFrame = Time.frameCount;
        for (int i = 0; i < WarmupFrames; i++) { FrameTimingManager.CaptureFrameTimings(); yield return null; }
        if (FrameTimingManager.GetLatestTimings(1, timingBuffer) > 0) previousTimestamp = timingBuffer[0].frameStartTimestamp;
        int samplingStartFrame = Time.frameCount;
        for (int i = 0; i < SampleFrames; i++)
        {
            Reset(ref draws); Reset(ref setPass); Reset(ref aovGpu); Reset(ref aovCpu);
            callbacks = 0;
            FrameTimingManager.CaptureFrameTimings();
            yield return null;
            // Здесь нет discovery, сериализации, PNG, ReadPixels, EncodePNG и дискового I/O.
            frames[i] = Time.unscaledDeltaTime * 1000.0;
            callbackSamples[i] = callbacks;
            RawFrame row = new RawFrame { frame = i, frameMs = frames[i], callbacks = callbacks };
            row.draw = Read(draws, false, out row.drawCount);
            row.setPass = Read(setPass, false, out row.setPassCount);
            row.aovGpuMs = Read(aovGpu, true, out row.aovGpuCount);
            Read(aovCpu, true, out double? cpuCount);
            int? samplerCount = aovSampler != null && aovSampler.isValid && aovSampler.enabled ? aovSampler.sampleBlockCount : (int?)null;
            if (cpuCount.HasValue && samplerCount.HasValue && cpuCount.Value == samplerCount.Value)
                row.aovExecutions = cpuCount;
            else if (!cpuCount.HasValue && samplerCount == 0)
                row.aovExecutions = 0;
            else if (cpuCount.HasValue || samplerCount.GetValueOrDefault() > 0)
                executionDisagreement = true;
            row.cpuRecorderCount = cpuCount;
            row.cpuSamplerBlocks = samplerCount;
            Add(drawValues, row.draw); Add(passValues, row.setPass); Add(aovValues, row.aovGpuMs); Add(executions, row.aovExecutions);
            if (FrameTimingManager.GetLatestTimings(1, timingBuffer) > 0
                && PortalPerformanceMetrics.IsNewTimestamp(timingBuffer[0].frameStartTimestamp, previousTimestamp))
            {
                FrameTiming timing = timingBuffer[0];
                previousTimestamp = timing.frameStartTimestamp;
                row.timestamp = previousTimestamp;
                row.gpu = Positive(timing.gpuFrameTime); row.cpu = Positive(timing.cpuFrameTime);
                row.main = Positive(timing.cpuMainThreadFrameTime); row.render = Positive(timing.cpuRenderThreadFrameTime);
                Add(gpu, row.gpu); Add(cpu, row.cpu); Add(main, row.main); Add(render, row.render);
            }
            raw[i] = row;
            run.RecordProgress(++totalFrames, 0);
        }
        int samplingEndFrame = Time.frameCount;
        var sample = new PortalPerformanceSample { round = round, mode = mode, warmupFrames = WarmupFrames,
            frameSamples = frames.Length, frameMedianMs = PortalPerformanceMetrics.Percentile(frames, 0.5),
            aovExecutionSamples = executionDisagreement ? 0 : executions.Count,
            aovExecutionsMax = executionDisagreement ? null : PortalPerformanceMetrics.Percentile(executions.ToArray(), 1) };
        SnapshotCameras(sample);
        samples.Add(sample);
        string prefix = "round" + round + "/" + mode;
        context.SaveMetadata(prefix);
        context.Save(prefix + "-settings.txt", "portals=" + enabled + "; depth=" + depth + "; divider=" + divider
            + "; writeContentDepth=" + contentDepth + "; yaw=" + yaw.ToString(CultureInfo.InvariantCulture)
            + "; frameTimingEnabled=" + FrameTimingManager.IsFeatureEnabled() + "; executionCounterDisagreement=" + executionDisagreement + "\n");
        context.Save(prefix + "-window.txt", "setupCompletedFrame=" + setupCompletedFrame + "; samplingStartFrame=" + samplingStartFrame
            + "; samplingEndFrame=" + samplingEndFrame + "; warmupFrames=" + WarmupFrames + "; retainedSamples=" + frames.Length + "\n");
        context.Save(prefix + "-samples.csv", RawCsv(raw));
        string summary = round + "," + mode + "," + frames.Length + "," + F(sample.frameMedianMs) + "," + F(PortalPerformanceMetrics.Percentile(frames, 0.95))
            + Stats(gpu) + Stats(cpu) + Stats(main) + Stats(render) + Stats(drawValues) + Stats(passValues) + Stats(aovValues)
            + "," + sample.aovExecutionSamples + "," + F(sample.aovExecutionsMax)
            + "," + F(sample.cameraObserved ? (double?)sample.mainCameras : null) + "," + F(sample.cameraObserved ? (double?)sample.virtualCameras : null)
            + "," + F(sample.cameraObserved ? (double?)sample.aovRequests : null) + "," + F(sample.cameraObserved ? (double?)sample.targetPixels : null)
            + "," + F(PortalPerformanceMetrics.Percentile(callbackSamples, 0.5))
            + "," + F(PortalPerformanceMetrics.Percentile(callbackSamples, 1));
        summaryRows.Add(summary);
        context.Save("performance.csv", SummaryHeader + string.Join("\n", summaryRows) + "\n");
        Debug.Log("[PortalPerformance] " + summary);
        // Архивные depth2/depth0 PNG сохраняются только после измеряемого цикла.
        if (mode == "depth2" || mode == "depth0")
        {
            Color32[] pixels = null;
            yield return context.Capture(round == 0 ? mode : "round1/" + mode,
                PortalImageMetrics.FromTopLeft(1920, 1080, 865, 420, 190, 290), value => pixels = value);
            if (mode == "depth2") depth2Pixels = pixels;
            else if (depth2Pixels != null && pixels != null)
            {
                roi[round] = PortalImageMetrics.Compare(depth2Pixels, pixels);
                context.Save("round" + round + "/roi-metrics.json", JsonUtility.ToJson(roi[round], true));
            }
        }
    }

    private void DiscoverCounters()
    {
        var handles = new List<ProfilerRecorderHandle>();
        ProfilerRecorderHandle.GetAvailable(handles);
        foreach (ProfilerRecorderHandle handle in handles)
        {
            ProfilerRecorderDescription description = ProfilerRecorderHandle.GetDescription(handle);
            if (description.Name.Contains("Draw Calls") || description.Name == "SetPass Calls Count" || description.Name == "HDRenderPipelineRenderAOV")
            {
                string label = description.Category.Name + ":" + description.Name + " unit=" + description.UnitType;
                if (discovered.Add(label)) Debug.Log("[PerformanceCounter] " + label);
            }
            if (description.Name == "Draw Calls Count" && !draws.Valid) draws = Start(description, false);
            if (description.Name == "SetPass Calls Count" && !setPass.Valid) setPass = Start(description, false);
            if (description.Name == "HDRenderPipelineRenderAOV" && description.UnitType.ToString() == "TimeNanoseconds")
            {
                if (!aovGpu.Valid) aovGpu = Start(description, true);
                if (!aovCpu.Valid) aovCpu = Start(description, false);
                if (aovSampler == null || !aovSampler.isValid)
                {
                    aovSampler = Recorder.Get(description.Name);
                    if (aovSampler.isValid) { samplerWasEnabled = aovSampler.enabled; aovSampler.enabled = true; }
                }
            }
        }
        context.Save("available-counters.txt", string.Join("\n", discovered));
    }

    private static ProfilerRecorder Start(ProfilerRecorderDescription description, bool gpu) =>
        ProfilerRecorder.StartNew(description.Category, description.Name, 1,
            ProfilerRecorderOptions.StartImmediately | ProfilerRecorderOptions.WrapAroundWhenCapacityReached
            | ProfilerRecorderOptions.SumAllSamplesInFrame | (gpu ? ProfilerRecorderOptions.GpuRecorder : (ProfilerRecorderOptions)0));
    private static void Reset(ref ProfilerRecorder recorder) { if (recorder.Valid) recorder.Reset(); }
    private static double? Read(ProfilerRecorder recorder, bool nanoseconds, out double? count)
    {
        count = null;
        if (!recorder.Valid || recorder.Count == 0) return null;
        ProfilerRecorderSample sample = recorder.GetSample(recorder.Count - 1);
        if (sample.Value < 0 || sample.Count < 0) return null;
        count = sample.Count;
        return nanoseconds ? PortalPerformanceMetrics.NanosecondsToMilliseconds(sample.Value) : sample.Value;
    }
    private static double? Positive(double value) => PortalCheckPolicy.Finite(value) && value > 0 ? value : (double?)null;
    private static void Add(List<double> values, double? value) { if (value.HasValue) values.Add(value.Value); }
    private static string F(double? value) => PortalPerformanceMetrics.Format(value);
    private static string Stats(List<double> values) => "," + values.Count + "," + F(PortalPerformanceMetrics.Percentile(values.ToArray(), 0.5))
        + "," + F(PortalPerformanceMetrics.Percentile(values.ToArray(), 0.95));
    private void OnCamera(ScriptableRenderContext renderContext, Camera camera) { callbacks++; }

    private void SnapshotCameras(PortalPerformanceSample sample)
    {
        sample.cameraObserved = context.Main != null;
        foreach (Camera camera in Camera.allCameras)
        {
            if (!camera.isActiveAndEnabled) continue;
            if (camera == context.Main) sample.mainCameras++;
            else if (context.IsVirtual(camera)) sample.virtualCameras++;
            else sample.cameraObserved = false;
            if (camera.targetTexture != null) sample.targetPixels += (long)camera.targetTexture.width * camera.targetTexture.height;
            HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
            if (data == null) { sample.cameraObserved = false; continue; }
            if (data.aovRequests != null) foreach (var request in data.aovRequests) sample.aovRequests++;
        }
    }

    private struct RawFrame
    {
        public int frame, callbacks;
        public ulong timestamp;
        public double frameMs;
        public double? draw, drawCount, setPass, setPassCount, aovGpuMs, aovGpuCount, aovExecutions, cpuRecorderCount, cpuSamplerBlocks;
        public double? gpu, cpu, main, render;
    }

    private static string RawCsv(RawFrame[] frames)
    {
        var csv = new StringBuilder("frame,unscaled_delta_ms,timing_timestamp,gpu_ms,cpu_ms,main_ms,render_ms,draw_value,draw_count,setpass_value,setpass_count,aov_gpu_ms,aov_gpu_count,aov_executions,cpu_aov_recorder_count,cpu_aov_sampler_blocks,begin_camera_callbacks\n");
        foreach (RawFrame row in frames)
            csv.Append(row.frame).Append(',').Append(F(row.frameMs)).Append(',').Append(row.timestamp == 0 ? "null" : row.timestamp.ToString(CultureInfo.InvariantCulture)).Append(',')
                .Append(F(row.gpu)).Append(',').Append(F(row.cpu)).Append(',').Append(F(row.main)).Append(',').Append(F(row.render)).Append(',')
                .Append(F(row.draw)).Append(',').Append(F(row.drawCount)).Append(',').Append(F(row.setPass)).Append(',').Append(F(row.setPassCount)).Append(',')
                .Append(F(row.aovGpuMs)).Append(',').Append(F(row.aovGpuCount)).Append(',').Append(F(row.aovExecutions)).Append(',')
                .Append(F(row.cpuRecorderCount)).Append(',').Append(F(row.cpuSamplerBlocks)).Append(',').Append(row.callbacks).Append('\n');
        return csv.ToString();
    }

    private void Finish(PortalCheckDecision decision)
    {
        if (run == null || run.IsCompleted) return;
        if (context != null)
        {
            var report = new StringBuilder(decision.status + ": " + decision.failureReason + "\n");
            for (int round = 0; round < 2; round++)
            {
                PortalPerformanceSample baseline = samples.Find(sample => sample.round == round && sample.mode == "depth2");
                PortalPerformanceSample shallow = samples.Find(sample => sample.round == round && sample.mode == "depth0");
                report.Append("round=").Append(round).Append(" depth2/depth0 frame median ratio=")
                    .Append(F(PortalPerformanceMetrics.Ratio(baseline?.frameMedianMs, shallow?.frameMedianMs))).Append('\n');
            }
            report.Append("No production performance improvement is claimed. GPU frame timing is not complete portal GPU cost.\n")
                .Append("Gate: two complete rounds, identical ROI, default=1 main+1 virtual/no AOV, off/behind=1 main/no AOV.\n");
            context.Save("performance-summary.txt", report.ToString());
        }
        run.Complete("Performance", decision.status, totalFrames, 0, decision.failureReason);
    }

    private void OnDisable()
    {
        if (disposed) return;
        disposed = true;
        if (subscribed) RenderPipelineManager.beginCameraRendering -= OnCamera;
        draws.Dispose(); setPass.Dispose(); aovGpu.Dispose(); aovCpu.Dispose();
        if (aovSampler != null && aovSampler.isValid) aovSampler.enabled = samplerWasEnabled;
        context?.Dispose();
        if (run != null && !run.IsCompleted) Finish(new PortalCheckDecision("Blocked", "Performance probe disabled before both rounds completed."));
    }

    private void OnDestroy() => OnDisable();
}
