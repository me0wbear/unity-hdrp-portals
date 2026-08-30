using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Captures the same view of the same geometry from both sides of a portal transition. The
/// portal pair is set up so the viewpoint just before the crossing and the viewpoint just after
/// it are four centimetres apart, which makes the two frames the same picture. Anything that
/// differs between them is what the player sees as the transition.
/// </summary>
public sealed class ColorCheck : MonoBehaviour
{
    [System.Serializable]
    public struct Step
    {
        public string name;
        public Vector3 pose;
        public bool portalsEnabled;
    }

    public Transform playerRoot;
    public Camera playerCamera;
    public PortalTraveller traveller;
    public GameObject[] portalObjects;
    public Step[] steps;
    public string outputDirectory = "ColorCheck";
    public int warmupFrames = 90;
    public int settleFrames = 14;

    /// <summary>Fraction of the frame, centred, that the comparison samples.</summary>
    public float sampleFraction = 0.22f;

    private readonly Dictionary<string, Color> _samples = new Dictionary<string, Color>();

    private IEnumerator Start()
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), outputDirectory);
        Directory.CreateDirectory(directory);

        for (int f = 0; f < warmupFrames; f++)
        {
            yield return null;
        }

        foreach (Step step in steps)
        {
            yield return Capture(directory, step);
        }

        Report("far", "farThrough", "farDirect");
        Report("cross", "crossBefore", "crossAfter");

        Application.Quit(0);
    }

    private IEnumerator Capture(string directory, Step step)
    {
        foreach (GameObject portal in portalObjects)
        {
            if (portal != null)
            {
                portal.SetActive(step.portalsEnabled);
            }
        }

        CharacterController controller =
            playerRoot != null ? playerRoot.GetComponent<CharacterController>() : null;
        if (controller != null)
        {
            controller.enabled = false;
        }

        playerRoot.SetPositionAndRotation(step.pose, Quaternion.identity);

        // Without this the tracker still holds the distance measured at the previous pose and
        // reads the jump between them as a crossing, teleporting the player out of the shot.
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
        File.WriteAllBytes(Path.Combine(directory, step.name + ".png"), frame.EncodeToPNG());
        Color mean = MeanCentre(frame);
        _samples[step.name] = mean;
        double sharpness = Sharpness(frame);
        Destroy(frame);

        Debug.Log("[ColorCheck] " + step.name + " at " + playerRoot.position.ToString("F2")
            + " mean=" + Format(mean) + " sharpness=" + sharpness.ToString("F5"));
    }

    private void Report(string label, string first, string second)
    {
        if (!_samples.TryGetValue(first, out Color a) || !_samples.TryGetValue(second, out Color b))
        {
            return;
        }

        Color delta = new Color(
            Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));

        Debug.Log("[ColorCheck] " + label + " delta=" + Format(delta)
            + " max=" + Mathf.Max(delta.r, Mathf.Max(delta.g, delta.b)).ToString("F4"));
    }

    private Color MeanCentre(Texture2D frame)
    {
        int halfWidth = Mathf.RoundToInt(frame.width * sampleFraction * 0.5f);
        int halfHeight = Mathf.RoundToInt(frame.height * sampleFraction * 0.5f);
        int x = frame.width / 2 - halfWidth;
        int y = frame.height / 2 - halfHeight;

        Color[] pixels = frame.GetPixels(x, y, halfWidth * 2, halfHeight * 2);
        double r = 0, g = 0, b = 0;
        foreach (Color pixel in pixels)
        {
            r += pixel.r;
            g += pixel.g;
            b += pixel.b;
        }

        return new Color(
            (float)(r / pixels.Length), (float)(g / pixels.Length), (float)(b / pixels.Length));
    }

    /// <summary>
    /// Mean absolute luminance step between neighbouring pixels. Blur removes those steps, so a
    /// falling value as the portal is approached is the image going soft.
    /// </summary>
    private double Sharpness(Texture2D frame)
    {
        int halfWidth = Mathf.RoundToInt(frame.width * sampleFraction * 0.5f);
        int halfHeight = Mathf.RoundToInt(frame.height * sampleFraction * 0.5f);
        int width = halfWidth * 2;
        int height = halfHeight * 2;

        Color[] pixels = frame.GetPixels(
            frame.width / 2 - halfWidth, frame.height / 2 - halfHeight, width, height);

        double sum = 0;
        int taken = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 1; x < width; x++)
            {
                sum += Mathf.Abs(
                    Luminance(pixels[y * width + x]) - Luminance(pixels[y * width + x - 1]));
                taken++;
            }
        }

        return taken > 0 ? sum / taken : 0;
    }

    private static float Luminance(Color color)
    {
        return 0.2126f * color.r + 0.7152f * color.g + 0.0722f * color.b;
    }

    private static string Format(Color color)
    {
        return "(" + color.r.ToString("F4") + ", " + color.g.ToString("F4") + ", "
            + color.b.ToString("F4") + ")";
    }
}
