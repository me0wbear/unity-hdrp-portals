using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Portals.Lab.Validation;
using UnityEngine;

/// <summary>
/// Сравнивает заданные позы до и после перехода. Реальный Teleported здесь не измеряется.
/// Метрика — нормализованный raw RGB из Texture2D.GetPixels, не HDR radiance.
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
    private readonly StringBuilder _metrics = new StringBuilder("step,r,g,b,sharpness,captureStatus\n");
    private readonly StringBuilder _summary = new StringBuilder();
    private bool _captureFailed;
    private int _capturedFrames;
    private PortalCheckRun _run;

    private IEnumerator Start()
    {
        _run = PortalCheckRun.Current;
        if (_run != null && _run.IsCompleted) yield break;
        string directory = _run != null ? _run.OutputDirectory
            : Path.Combine(Directory.GetCurrentDirectory(), outputDirectory);
        if (playerRoot == null || playerCamera == null || portalObjects == null || steps == null || steps.Length == 0)
        {
            Finish(new PortalCheckDecision("Failed", "Color configuration is incomplete."));
            yield break;
        }
        if (!PrepareDirectory(directory))
        {
            Finish(new PortalCheckDecision("Failed", "Cannot create Color output directory."));
            yield break;
        }

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
        SaveMetrics(directory);
        Finish(PortalCheckPolicy.Color(_samples, steps.Length, _captureFailed));
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

        // Перестановка поз не является реальным пересечением портала.
        if (traveller != null)
        {
            traveller.ResetPortalTracking();
        }

        for (int f = 0; f < settleFrames; f++)
        {
            yield return null;
        }

        yield return new WaitForEndOfFrame();

        CaptureFrame(directory, step);
    }

    private bool PrepareDirectory(string directory)
    {
        try { Directory.CreateDirectory(directory); return true; }
        catch (System.Exception) { return false; }
    }

    private void CaptureFrame(string directory, Step step)
    {
        Texture2D frame = null;
        try
        {
            if (string.IsNullOrWhiteSpace(step.name) || step.name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new System.InvalidOperationException("Invalid capture name.");
            frame = ScreenCapture.CaptureScreenshotAsTexture();
            if (frame == null) throw new System.InvalidOperationException("No screenshot.");
            byte[] png = frame.EncodeToPNG();
            if (png == null || png.Length == 0) throw new System.InvalidOperationException("Empty screenshot.");
            File.WriteAllBytes(Path.Combine(directory, step.name + ".png"), png);
            Color mean = MeanCentre(frame);
            double sharpness = Sharpness(frame);
            if (!PortalCheckPolicy.Finite(sharpness)) throw new System.InvalidOperationException("Invalid sharpness.");
            _samples[step.name] = mean;
            _capturedFrames++;
            _metrics.Append(step.name).Append(',').Append(mean.r.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(mean.g.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(mean.b.ToString("R", CultureInfo.InvariantCulture))
                .Append(',').Append(sharpness.ToString("R", CultureInfo.InvariantCulture)).Append(",Captured\n");
            Debug.Log("[ColorCheck] " + step.name + " mean=" + Format(mean)
                + " sharpness=" + sharpness.ToString("F5", CultureInfo.InvariantCulture));
        }
        catch (System.Exception)
        {
            _captureFailed = true;
            _metrics.Append("capture-failed,,,,,Failed\n");
            _run?.RecordFailure("Color capture or artifact write failed.");
        }
        finally
        {
            if (frame != null) Destroy(frame);
            _run?.RecordProgress(_capturedFrames, 0);
            SaveMetrics(directory);
        }
    }

    private void SaveMetrics(string directory)
    {
        try
        {
            File.WriteAllText(Path.Combine(directory, "color-metrics.csv"), _metrics.ToString());
            File.WriteAllText(Path.Combine(directory, "color-summary.txt"),
                "Units: normalized raw capture RGB (Texture2D.GetPixels).\n"
                + "Cross: pose comparison; crossingCount=0. Far: diagnostic only.\n" + _summary);
        }
        catch (System.Exception)
        {
            _captureFailed = true;
            _run?.RecordFailure("Cannot persist Color metrics.");
        }
    }

    private void Finish(PortalCheckDecision decision)
    {
        if (_run != null) _run.Complete("Color", decision.status, _capturedFrames, 0, decision.failureReason);
        else
        {
            Debug.Log("[ColorCheck] uncertified " + decision.status + ": " + decision.failureReason);
            Application.Quit(decision.status == "Passed" ? 0 : 1);
        }
    }

    private void Report(string label, string first, string second)
    {
        if (!_samples.TryGetValue(first, out Color a) || !_samples.TryGetValue(second, out Color b))
        {
            return;
        }

        Color delta = new Color(
            Mathf.Abs(a.r - b.r), Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b));

        string line = label + " delta=" + Format(delta) + " max="
            + Mathf.Max(delta.r, Mathf.Max(delta.g, delta.b)).ToString("F6", CultureInfo.InvariantCulture);
        _summary.AppendLine(line);
        Debug.Log("[ColorCheck] " + line);
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
