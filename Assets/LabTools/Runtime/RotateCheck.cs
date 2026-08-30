using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Sweeps the view across a portal from several distances and counts pixels of a marker that is
/// placed behind the exit portal. That marker is on the far side of the clip plane, so it must
/// never appear in the opening. Any pixel of it that does appear means the oblique near plane
/// was rejected and the level fell back to an ordinary projection, which stops clipping the
/// destination side and leaks whatever stands behind the exit.
/// </summary>
public sealed class RotateCheck : MonoBehaviour
{
    public Transform playerRoot;
    public Transform viewPoint;
    public Camera playerCamera;
    public PortalTraveller traveller;

    public float[] distances = { 2f, 1f, 0.5f, 0.25f, 0.12f };
    public float yawRange = 75f;
    public float yawStep = 15f;

    public string outputDirectory = "RotateCheck";
    public int warmupFrames = 90;
    public int settleFrames = 6;

    private readonly StringBuilder _report = new StringBuilder();

    private IEnumerator Start()
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), outputDirectory);
        Directory.CreateDirectory(directory);

        CharacterController controller = playerRoot.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
        }

        for (int f = 0; f < warmupFrames; f++)
        {
            yield return null;
        }

        foreach (float distance in distances)
        {
            _report.Append("distance ").Append(distance.ToString("F2")).Append(" m: ");

            for (float yaw = -yawRange; yaw <= yawRange + 0.01f; yaw += yawStep)
            {
                // The portal faces -Z and the eye stands in front of it, so the pose is a step
                // back along Z with the body turned by the sweep angle.
                playerRoot.SetPositionAndRotation(
                    new Vector3(0f, 0.1f, -distance), Quaternion.Euler(0f, yaw, 0f));

                if (traveller != null)
                {
                    traveller.ResetPortalTracking();
                }

                for (int f = 0; f < settleFrames; f++)
                {
                    yield return null;
                }

                yield return new WaitForEndOfFrame();

                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                int leaked = CountMarker(frame);

                if (leaked > 0)
                {
                    File.WriteAllBytes(
                        Path.Combine(directory, string.Format(
                            "leak_d{0:0.00}_yaw{1:+00;-00}.png", distance, yaw)),
                        frame.EncodeToPNG());
                }

                Destroy(frame);
                _report.Append(leaked > 0 ? leaked.ToString() : ".").Append(' ');
            }

            _report.Append('\n');
        }

        Debug.Log("[RotateCheck] leaked marker pixels per yaw step, dot means clean\n" + _report);
        Application.Quit(0);
    }

    /// <summary>
    /// Counts pixels of the saturated red marker. Nothing else in the scene is red, and the
    /// marker is unlit, so the test does not depend on how the scene happens to be lit.
    /// </summary>
    private static int CountMarker(Texture2D frame)
    {
        Color[] pixels = frame.GetPixels();
        int count = 0;
        foreach (Color pixel in pixels)
        {
            if (pixel.r > 0.35f && pixel.g < 0.12f && pixel.b < 0.12f)
            {
                count++;
            }
        }

        return count;
    }
}
