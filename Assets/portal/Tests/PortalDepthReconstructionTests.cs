using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public sealed class PortalDepthReconstructionTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

    [TestCase(10f, 0f, 0f)]
    [TestCase(100f, 20f, 0f)]
    [TestCase(500f, -20f, 0f)]
    [TestCase(100f, 0f, 20f)]
    public void ContentInverseProjection_ReconstructsDeviceDepthOnGpu(float distance, float yaw, float pitch)
    {
        if (!SystemInfo.supportsComputeShaders)
        {
            Assert.Ignore("Requires a graphics device; run Unity without -nographics.");
        }

        var entranceObject = new GameObject("Depth Entrance");
        var exitObject = new GameObject("Depth Exit");
        var viewerObject = new GameObject("Depth Viewer");
        var screenObject = new GameObject("Depth Screen");
        var target = new RenderTexture(64, 64, 24);
        PortalRenderer renderer = null;
        try
        {
            Portal entrance = entranceObject.AddComponent<Portal>();
            Portal exit = exitObject.AddComponent<Portal>();
            entrance.exitPortal = exit;
            exit.transform.position = new Vector3(30f, 0f, 0f);
            entrance.screen = screenObject.AddComponent<MeshRenderer>();
            screenObject.transform.SetParent(entranceObject.transform, false);
            entrance.cullWhenOffscreen = false;
            // Проверяется матрица, поэтому дополнительный AOV-таргет не нужен.
            entrance.writeContentDepth = false;
            Camera viewer = viewerObject.AddComponent<Camera>();
            viewer.enabled = false;
            viewer.targetTexture = target;
            viewer.nearClipPlane = 0.1f;
            viewer.farClipPlane = 1000f;
            viewer.transform.SetPositionAndRotation(new Vector3(0f, 0f, 2f), Quaternion.Euler(pitch, 180f + yaw, 0f));
            renderer = new PortalRenderer(entrance);
            typeof(PortalRenderer).GetField("_reported", Private).SetValue(renderer, true);
            renderer.Render(viewer, 1);

            Camera virtualCamera = ((Camera[])typeof(PortalRenderer)
                .GetField("_cameras", Private).GetValue(renderer))[0];
            Matrix4x4 gpu = GL.GetGPUProjectionMatrix(virtualCamera.projectionMatrix, true);
            Vector4 clip = gpu * new Vector4(distance * 0.1f, distance * 0.05f, -distance, 1f);
            Vector4 device = clip / clip.w;
            // ComputeClipSpacePosition переводит экранный Y обратно в clip Y.
            float y = SystemInfo.graphicsUVStartsAtTop ? -device.y : device.y;
            device = new Vector4(device.x * 0.5f + 0.5f, y * 0.5f + 0.5f, device.z, 0f);
            ComputeShader shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(
                "Assets/portal/Tests/PortalDepthReconstruction.compute");
            Assert.IsNotNull(shader);
            using (var output = new ComputeBuffer(1, sizeof(float)))
            {
                var values = new float[1];
                shader.SetBuffer(0, "_Distance", output);
                shader.SetVector("_DevicePosition", device);
                shader.SetMatrix("_InverseProjection", gpu.inverse);
                shader.Dispatch(0, 1, 1, 1);
                output.GetData(values);
                Assert.That(values[0], Is.EqualTo(distance).Within(0.05f), "GPU control");

                shader.SetMatrix("_InverseProjection", renderer.ContentInverseProjection);
                shader.Dispatch(0, 1, 1, 1);
                output.GetData(values);
                Debug.Log($"[PortalDepthTest] api={SystemInfo.graphicsDeviceType} expected={distance:R} actual={values[0]:R}");
                Assert.That(values[0], Is.EqualTo(distance).Within(0.05f), "Portal content depth");
            }
        }
        finally
        {
            // В EditMode Destroy не исполняет отложенную уборку. Только тест
            // синхронно освобождает свои камеры после снятия подписки.
            if (renderer != null)
            {
                typeof(PortalRenderer).GetMethod("Unsubscribe", Private).Invoke(renderer, null);
                foreach (Camera camera in (Camera[])typeof(PortalRenderer).GetField("_cameras", Private).GetValue(renderer))
                {
                    if (camera == null) continue;
                    camera.targetTexture = null;
                    Object.DestroyImmediate(camera.gameObject);
                }
                foreach (RenderTexture texture in (RenderTexture[])typeof(PortalRenderer).GetField("_targets", Private).GetValue(renderer))
                {
                    if (texture != null) Object.DestroyImmediate(texture);
                }
            }
            Object.DestroyImmediate(viewerObject);
            Object.DestroyImmediate(entranceObject);
            Object.DestroyImmediate(exitObject);
            Object.DestroyImmediate(target);
        }
    }
}
