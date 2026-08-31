using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Portals.Lab.Validation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

// Общая граница двух Sandbox probes: диагностический Player, снимки и metadata.
internal sealed class SandboxProbeContext : IDisposable
{
    public readonly PortalCheckRun Run;
    public readonly Camera Main;
    public readonly HDAdditionalCameraData Data;
    public readonly Transform Player;
    public readonly Portal[] Portals;
    public readonly Vector3 EyeOffset;
    public readonly int Width;
    public readonly int Height;
    public string Problem = string.Empty;
    public int Captures;
    private readonly List<Behaviour> disabled = new List<Behaviour>();
    private readonly List<Rigidbody> frozen = new List<Rigidbody>();
    private readonly CharacterController controller;
    private readonly bool controllerEnabled;
    private readonly bool customSettings;
    private readonly FrameSettings settings;
    private readonly FrameSettingsOverrideMask mask;
    private readonly HDAdditionalCameraData.AntialiasingMode aa;
    private bool disposed;

    public SandboxProbeContext(PortalCheckRun run, int width, int height)
    {
        Run = run; Width = width; Height = height;
        Main = Camera.main;
        if (Main == null) throw new InvalidOperationException("Sandbox main camera is missing.");
        Data = Main.GetComponent<HDAdditionalCameraData>();
        PortalTraveller traveller = Main.GetComponentInParent<PortalTraveller>();
        if (Data == null || traveller == null) throw new InvalidOperationException("Sandbox camera/Player references are missing.");
        Player = traveller.transform;
        Portals = UnityEngine.Object.FindObjectsByType<Portal>(FindObjectsSortMode.None);
        if (Portals.Length == 0 || Screen.width != width || Screen.height != height)
            throw new InvalidOperationException("Sandbox portals or required capture resolution are unavailable.");
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Direct3D12 || !Debug.isDebugBuild)
            throw new InvalidOperationException("Sandbox certification requires a D3D12 Development Player.");
        customSettings = Data.customRenderingSettings;
        settings = Data.renderingPathCustomFrameSettings;
        mask = Data.renderingPathCustomFrameSettingsOverrideMask;
        aa = Data.antialiasing;
        EyeOffset = Player.InverseTransformPoint(Main.transform.position);
        // Отключение движения и rigidbody ограничено этим Player, не всей сценой.
        foreach (Behaviour behaviour in Player.GetComponentsInChildren<Behaviour>())
            if (behaviour.enabled && (behaviour is PortalDemoController || behaviour is PortalTraveller || behaviour is PortalCameraBridge))
            { disabled.Add(behaviour); behaviour.enabled = false; }
        controller = Player.GetComponent<CharacterController>();
        controllerEnabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        foreach (Rigidbody body in Player.GetComponentsInChildren<Rigidbody>())
            if (!body.isKinematic) { frozen.Add(body); body.isKinematic = true; }
        Application.runInBackground = true;
    }

    public void RestoreCamera()
    {
        if (Data == null) return;
        Data.customRenderingSettings = customSettings;
        Data.renderingPathCustomFrameSettings = settings;
        Data.renderingPathCustomFrameSettingsOverrideMask = mask;
        Data.antialiasing = aa;
        Main.ResetProjectionMatrix();
    }

    public void PortalsEnabled(bool enabled)
    {
        foreach (Portal portal in Portals) if (portal != null) portal.gameObject.SetActive(enabled);
    }

    public bool IsVirtual(Camera camera)
    {
        if (camera == null || camera == Main) return false;
        Portal owner = camera.GetComponentInParent<Portal>();
        return owner != null && Array.IndexOf(Portals, owner) >= 0;
    }

    public void SetEye(Vector3 eye, Quaternion rotation, bool portalsEnabled)
    {
        PortalsEnabled(portalsEnabled);
        Player.SetPositionAndRotation(eye - rotation * EyeOffset, rotation);
        Main.transform.localRotation = Quaternion.identity;
        HDCamera.GetOrCreate(Main).Reset();
        PortalSystem.ResetHistory();
    }

    public static IEnumerator Settle(int frames)
    {
        for (int i = 0; i < frames; i++) yield return null;
    }

    public IEnumerator Capture(string name, RectInt? roi, Action<Color32[]> receive)
    {
        yield return new WaitForEndOfFrame();
        Texture2D texture = null;
        Color32[] pixels = null;
        try
        {
            texture = ScreenCapture.CaptureScreenshotAsTexture();
            if (texture == null || texture.width != Width || texture.height != Height)
                Problem = "Screenshot is missing or does not have the required resolution.";
            else
            {
                string path = Path.Combine(Run.OutputDirectory, name + ".png");
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllBytes(path, texture.EncodeToPNG());
                // GetPixels использует начало снизу слева; ROI уже переведён явно.
                if (roi.HasValue)
                {
                    RectInt rect = roi.Value;
                    Color[] rgb = texture.GetPixels(rect.x, rect.y, rect.width, rect.height);
                    pixels = new Color32[rgb.Length];
                    for (int i = 0; i < rgb.Length; i++) pixels[i] = rgb[i];
                }
                else pixels = texture.GetPixels32();
                Captures++;
                SaveMetadata(name);
            }
        }
        catch (Exception error) { Run.RecordFailure("Capture persistence failed: " + error.Message); }
        finally { if (texture != null) UnityEngine.Object.Destroy(texture); }
        receive(pixels);
    }

    public void Save(string name, string content)
    {
        try
        {
            string path = Path.Combine(Run.OutputDirectory, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content);
        }
        catch (Exception error) { Run.RecordFailure("Probe artifact persistence failed: " + error.Message); }
    }

    [Serializable]
    private sealed class CameraMetadata
    {
        public string name, aa, projection;
        public bool main, virtualCamera, ssao, ssr, ssgi, fog, postprocess, exposureControl;
        public float aoIntensity, near, far, fixedExposure;
        public Vector3 position, euler;
    }

    [Serializable]
    private sealed class Metadata
    {
        public string label, unityVersion, gpu, graphicsApi;
        public int width, height, vSync, targetFrameRate;
        public bool runInBackground;
        public CameraMetadata[] cameras;
        public string[] volumeProfiles;
    }

    public void SaveMetadata(string name)
    {
        var cameras = new List<CameraMetadata>();
        foreach (Camera camera in Camera.allCameras)
        {
            if (!camera.isActiveAndEnabled) continue;
            HDAdditionalCameraData data = camera.GetComponent<HDAdditionalCameraData>();
            if (data == null) { Problem = "An active camera is missing HDRP camera data."; continue; }
            HDCamera hd = HDCamera.GetOrCreate(camera);
            var ao = hd.volumeStack.GetComponent<ScreenSpaceAmbientOcclusion>();
            var gi = hd.volumeStack.GetComponent<GlobalIllumination>();
            var fog = hd.volumeStack.GetComponent<Fog>();
            var exposure = hd.volumeStack.GetComponent<Exposure>();
            if (ao == null || gi == null || fog == null || exposure == null) { Problem = "HDRP volume stack is incomplete."; continue; }
            var row = new CameraMetadata { name = camera.name, main = camera == Main, virtualCamera = IsVirtual(camera),
                aa = data.antialiasing.ToString(), ssao = hd.frameSettings.IsEnabled(FrameSettingsField.SSAO),
                ssr = hd.frameSettings.IsEnabled(FrameSettingsField.SSR), ssgi = gi.enable.value, fog = fog.enabled.value,
                postprocess = hd.frameSettings.IsEnabled(FrameSettingsField.Postprocess),
                exposureControl = hd.frameSettings.IsEnabled(FrameSettingsField.ExposureControl),
                aoIntensity = ao.intensity.value, fixedExposure = exposure.fixedExposure.value,
                near = camera.nearClipPlane, far = camera.farClipPlane, position = camera.transform.position,
                euler = camera.transform.eulerAngles, projection = camera.nonJitteredProjectionMatrix.ToString("F6") };
            cameras.Add(row);
            Debug.Log("[SandboxCamera] " + name + " " + JsonUtility.ToJson(row));
        }
        var profiles = new List<string>();
        foreach (Volume volume in UnityEngine.Object.FindObjectsByType<Volume>(FindObjectsSortMode.None))
            profiles.Add(volume.name + ":" + (volume.sharedProfile != null ? volume.sharedProfile.name : "null"));
        Save(name + "-metadata.json", JsonUtility.ToJson(new Metadata { label = name, unityVersion = Application.unityVersion,
            gpu = SystemInfo.graphicsDeviceName, graphicsApi = SystemInfo.graphicsDeviceType.ToString(), width = Screen.width,
            height = Screen.height, vSync = QualitySettings.vSyncCount, targetFrameRate = Application.targetFrameRate,
            runInBackground = Application.runInBackground, cameras = cameras.ToArray(), volumeProfiles = profiles.ToArray() }, true));
    }

    // Вложенные IEnumerator выполняются здесь, чтобы ошибка не оставляла run без завершения.
    public static IEnumerator Guard(IEnumerator body, PortalCheckRun run, Action<Exception> failed)
    {
        var stack = new Stack<IEnumerator>();
        stack.Push(body);
        while (stack.Count > 0 && !run.IsCompleted)
        {
            object current = null;
            Exception failure = null;
            bool next = false;
            try { next = stack.Peek().MoveNext(); if (next) current = stack.Peek().Current; }
            catch (Exception error) { failure = error; }
            if (failure != null) { failed(failure); yield break; }
            if (!next) { (stack.Pop() as IDisposable)?.Dispose(); continue; }
            if (current is IEnumerator nested) stack.Push(nested);
            else yield return current;
        }
        while (stack.Count > 0) (stack.Pop() as IDisposable)?.Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        RestoreCamera();
        foreach (Behaviour behaviour in disabled) if (behaviour != null) behaviour.enabled = true;
        if (controller != null) controller.enabled = controllerEnabled;
        foreach (Rigidbody body in frozen) if (body != null) body.isKinematic = false;
    }
}
