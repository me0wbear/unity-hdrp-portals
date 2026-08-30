using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Measures how far a moving frame drifts from the frame that should have been produced at the
/// same pose. The reference is captured standing still with no temporal antialiasing, so the
/// difference is exactly the error the temporal reconstruction introduces while the camera
/// moves. Running it once through a portal and once on the same geometry directly says whether
/// the portal surface reconstructs worse than ordinary geometry, which is what a missing motion
/// vector pass would cause.
/// </summary>
public sealed class GhostCheck : MonoBehaviour
{
    public Transform playerRoot;
    public Camera playerCamera;
    public PortalTraveller traveller;
    public GameObject[] portalObjects;
    public Portal[] portals;

    public Vector3 from = new Vector3(0f, 0.1f, -4f);
    public Vector3 to = new Vector3(0f, 0.1f, -3f);
    public float directOffset = 30f;

    public string outputDirectory = "GhostCheck";
    public int warmupFrames = 90;
    public int settleFrames = 24;
    public int motionFrames = 30;

    /// <summary>Fraction of the frame, centred, that the comparison samples.</summary>
    public float sampleFraction = 0.20f;

    private int _sampleWidth;
    private int _sampleHeight;

    private IEnumerator Start()
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), outputDirectory);
        Directory.CreateDirectory(directory);

        for (int f = 0; f < warmupFrames; f++)
        {
            yield return null;
        }

        yield return Measure(directory, "portal", Vector3.zero, true);
        yield return Measure(directory, "direct", new Vector3(directOffset, 0f, 0f), false);

        // Edge crawl: how much two consecutive frames differ while the camera creeps sideways.
        // Aliased edges jump from pixel to pixel and read as shimmer; resolved edges do not.
        yield return Shimmer(directory, "portal", Vector3.zero, true);
        yield return Shimmer(directory, "direct", new Vector3(directOffset, 0f, 0f), false);

        Application.Quit(0);
    }

    private IEnumerator Shimmer(
        string directory, string label, Vector3 offset, bool portalsEnabled)
    {
        Color[] first = null;
        Color[] second = null;

        // Temporal antialiasing on the player camera is off here on purpose: the question is how
        // steady the portal image itself is, not how well the player camera hides it.
        yield return Capture(directory, label.Trim() + "_a", offset, portalsEnabled, false, true,
            result => first = result);

        playerRoot.position += new Vector3(0.01f, 0f, 0f);
        yield return new WaitForEndOfFrame();

        Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
        second = SampleCentre(frame);
        Destroy(frame);

        Debug.Log("[GhostCheck] shimmer " + label + " = "
            + MeanAbsoluteDifference(first, second).ToString("F5"));
    }

    private IEnumerator Measure(string directory, string label, Vector3 offset, bool portalsEnabled)
    {
        Color[] reference = null;
        Color[] withTaa = null;
        Color[] withoutTaa = null;

        yield return Capture(directory, label + "_reference", offset, portalsEnabled, false, false,
            result => reference = result);
        yield return Capture(directory, label + "_taa", offset, portalsEnabled, true, true,
            result => withTaa = result);
        yield return Capture(directory, label + "_plain", offset, portalsEnabled, false, true,
            result => withoutTaa = result);

        Debug.Log("[GhostCheck] " + label
            + " error with TAA=" + MeanAbsoluteDifference(withTaa, reference).ToString("F5")
            + " without TAA=" + MeanAbsoluteDifference(withoutTaa, reference).ToString("F5"));
    }

    private IEnumerator Capture(
        string directory,
        string name,
        Vector3 offset,
        bool portalsEnabled,
        bool temporalAntialiasing,
        bool moving,
        System.Action<Color[]> report)
    {
        foreach (GameObject portal in portalObjects)
        {
            if (portal != null)
            {
                portal.SetActive(portalsEnabled);
            }
        }

        if (playerCamera != null && playerCamera.TryGetComponent(out HDAdditionalCameraData data))
        {
            data.antialiasing = temporalAntialiasing
                ? HDAdditionalCameraData.AntialiasingMode.TemporalAntialiasing
                : HDAdditionalCameraData.AntialiasingMode.None;
        }

        CharacterController controller =
            playerRoot != null ? playerRoot.GetComponent<CharacterController>() : null;
        if (controller != null)
        {
            controller.enabled = false;
        }

        Vector3 start = from + offset;
        Vector3 end = to + offset;

        playerRoot.SetPositionAndRotation(moving ? start : end, Quaternion.identity);
        if (traveller != null)
        {
            traveller.ResetPortalTracking();
        }

        for (int f = 0; f < settleFrames; f++)
        {
            yield return null;
        }

        if (moving)
        {
            // Move continuously into the capture, so the history the frame is reconstructed from
            // is a real motion history rather than a converged still image.
            for (int f = 1; f <= motionFrames; f++)
            {
                playerRoot.position = Vector3.Lerp(start, end, f / (float)motionFrames);
                yield return null;
            }
        }

        yield return new WaitForEndOfFrame();

        Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), frame.EncodeToPNG());
        report(SampleCentre(frame));
        Destroy(frame);
    }

    private Color[] SampleCentre(Texture2D frame)
    {
        int halfWidth = Mathf.RoundToInt(frame.width * sampleFraction * 0.5f);
        int halfHeight = Mathf.RoundToInt(frame.height * sampleFraction * 0.5f);
        _sampleWidth = halfWidth * 2;
        _sampleHeight = halfHeight * 2;

        return frame.GetPixels(
            frame.width / 2 - halfWidth, frame.height / 2 - halfHeight, _sampleWidth, _sampleHeight);
    }

    private static double MeanAbsoluteDifference(Color[] a, Color[] b)
    {
        if (a == null || b == null || a.Length != b.Length || a.Length == 0)
        {
            return double.NaN;
        }

        double sum = 0;
        for (int i = 0; i < a.Length; i++)
        {
            sum += Mathf.Abs(a[i].r - b[i].r)
                + Mathf.Abs(a[i].g - b[i].g)
                + Mathf.Abs(a[i].b - b[i].b);
        }

        return sum / (a.Length * 3);
    }
}
