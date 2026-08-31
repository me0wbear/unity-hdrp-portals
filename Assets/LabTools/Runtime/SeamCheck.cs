using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Portals.Lab.Validation;
using UnityEngine;

/// <summary>
/// Проходит через портал фиксированными шагами и сохраняет покадровые метрики.
/// До калибровки визуального порога корректный набор данных получает Blocked.
/// </summary>
public sealed class SeamCheck : MonoBehaviour
{
    public Transform playerRoot;
    public PortalTraveller traveller;
    public UHFPS.Runtime.PlayerStateMachine machine;
    public Vector3 start = new Vector3(0f, 0.1f, -3f);
    public float speed = 3f;
    public int frames = 160;
    public int warmupFrames = 90;
    public string outputDirectory = "SeamCheck";
    public int pixelStride = 3;

    private readonly StringBuilder _report = new StringBuilder();
    private readonly StringBuilder _csv = new StringBuilder("frame,difference,meanLuminance,crossing,crossingCount\n");
    private readonly List<double> _differences = new List<double>();
    private readonly List<double> _luminances = new List<double>();
    private int _teleportFrame = -1;
    private int _lastTeleportFrame = -1;
    private int _crossingCount;
    private int _frame = -1;
    private bool _captureFailed;
    private PortalCheckRun _run;
    private PortalTraveller _subscribedTraveller;

    private IEnumerator Start()
    {
        _run = PortalCheckRun.Current;
        if (_run != null && _run.IsCompleted) yield break;
        string directory = _run != null ? _run.OutputDirectory
            : Path.Combine(Directory.GetCurrentDirectory(), outputDirectory);
        CharacterController controller = playerRoot != null ? playerRoot.GetComponent<CharacterController>() : null;
        if (playerRoot == null || traveller == null || machine == null || controller == null || frames < 4
            || !PortalCheckPolicy.Finite(speed) || speed <= 0)
        {
            Finish(new PortalCheckDecision("Failed", "Seam configuration is incomplete."));
            yield break;
        }
        if (!PrepareDirectory(directory))
        {
            Finish(new PortalCheckDecision("Failed", "Cannot create Seam output directory."));
            yield break;
        }

        _subscribedTraveller = traveller;
        _subscribedTraveller.Teleported += OnTeleported;
        Texture2D previousFrame = null;
        try
        {
            for (int f = 0; f < warmupFrames; f++) yield return null;

            controller.enabled = false;
            playerRoot.SetPositionAndRotation(start, Quaternion.identity);
            traveller.ResetPortalTracking();
            controller.enabled = true;
            for (int f = 0; f < 8; f++) yield return null;
            machine.Motion = new Vector3(0f, 0f, speed);
            Debug.Log("[SeamCheck] fixed simulated step=1/60 sec; walk starting at " + playerRoot.position.ToString("F2"));

            Color[] previous = null;
            for (int index = 0; index < frames; index++)
            {
                // После EndOfFrame следующая итерация обязана дождаться нового Update.
                // Тогда Move, Teleported в LateUpdate и снимок относятся к одному кадру.
                yield return null;
                _frame = index;
                controller.Move(machine.Motion * PortalCheckPolicy.SeamStepSeconds);
                yield return new WaitForEndOfFrame();

                Texture2D frame = null;
                Color[] current = null;
                double difference = double.NaN;
                double luminance = double.NaN;
                try
                {
                    frame = ScreenCapture.CaptureScreenshotAsTexture();
                    if (frame == null) throw new System.InvalidOperationException("No screenshot.");
                    current = Sample(frame);
                    difference = previous == null ? double.NaN : MeanAbsoluteDifference(previous, current);
                    luminance = MeanLuminance(current);
                    if (_lastTeleportFrame == _frame && previousFrame != null)
                        SaveFrame(directory, _frame - 1, previousFrame);
                    if (_lastTeleportFrame >= 0 && _frame - _lastTeleportFrame <= 2)
                        SaveFrame(directory, _frame, frame);
                }
                catch (System.Exception)
                {
                    _captureFailed = true;
                    _run?.RecordFailure("Seam capture or PNG write failed.");
                }
                finally
                {
                    _differences.Add(difference);
                    _luminances.Add(luminance);
                    bool crossing = _lastTeleportFrame == _frame;
                    _csv.Append(_frame).Append(',').Append(Number(difference)).Append(',')
                        .Append(Number(luminance)).Append(',').Append(crossing ? 1 : 0)
                        .Append(',').Append(_crossingCount).Append('\n');
                    _report.Append(_frame.ToString("000")).Append(" difference=").Append(Number(difference))
                        .Append(" luminance=").Append(Number(luminance))
                        .Append(crossing ? " crossing" : string.Empty).Append('\n');
                    if (previousFrame != null) Destroy(previousFrame);
                    previousFrame = frame;
                    previous = current;
                    _run?.RecordProgress(_luminances.Count, _crossingCount);
                    SaveMetrics(directory);
                }
                if (_captureFailed) break;
            }
        }
        finally
        {
            if (previousFrame != null) Destroy(previousFrame);
            Unsubscribe();
            SaveMetrics(directory);
        }

        PortalCheckDecision decision = _captureFailed
            ? new PortalCheckDecision("Failed", "Seam capture or metric persistence failed.")
            : PortalCheckPolicy.Seam(_differences, _luminances, _crossingCount, _teleportFrame);
        Finish(decision);
    }

    private bool PrepareDirectory(string directory)
    {
        try { Directory.CreateDirectory(directory); return true; }
        catch (System.Exception) { return false; }
    }

    private void Finish(PortalCheckDecision decision)
    {
        if (_run != null) _run.Complete("Seam", decision.status, _luminances.Count, _crossingCount, decision.failureReason);
        else
        {
            Debug.Log("[SeamCheck] uncertified " + decision.status + ": " + decision.failureReason);
            Application.Quit(decision.status == "Passed" ? 0 : decision.status == "Failed" ? 1 : 2);
        }
    }

    private void OnTeleported(PortalTeleportContext context)
    {
        _crossingCount++;
        if (_crossingCount == 1) _teleportFrame = _frame;
        _lastTeleportFrame = _frame;
    }

    private void OnDisable() => Unsubscribe();

    private void Unsubscribe()
    {
        if (_subscribedTraveller != null) _subscribedTraveller.Teleported -= OnTeleported;
        _subscribedTraveller = null;
    }

    private void SaveMetrics(string directory)
    {
        try
        {
            File.WriteAllText(Path.Combine(directory, "seam-metrics.csv"), _csv.ToString());
            File.WriteAllText(Path.Combine(directory, "seam-summary.txt"),
                "Units: normalized raw capture RGB. Fixed simulated movement step: 1/60 sec.\n"
                + "Visual threshold not calibrated. Teleported events: " + _crossingCount
                + "; first crossing frame: " + _teleportFrame + "\n" + _report);
        }
        catch (System.Exception)
        {
            _captureFailed = true;
            _run?.RecordFailure("Cannot persist Seam metrics.");
        }
    }

    private static void SaveFrame(string directory, int index, Texture2D frame)
    {
        byte[] png = frame.EncodeToPNG();
        if (png == null || png.Length == 0) throw new System.InvalidOperationException("Empty screenshot.");
        File.WriteAllBytes(Path.Combine(directory, "frame" + index.ToString("000") + ".png"), png);
    }

    private static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    private Color[] Sample(Texture2D frame)
    {
        Color[] pixels = frame.GetPixels();
        int stride = Mathf.Max(1, pixelStride);
        var sampled = new Color[(pixels.Length + stride - 1) / stride];
        for (int i = 0, j = 0; i < pixels.Length; i += stride, j++) sampled[j] = pixels[i];
        return sampled;
    }

    private static double MeanLuminance(Color[] pixels)
    {
        if (pixels == null || pixels.Length == 0) return double.NaN;
        double sum = 0;
        for (int i = 0; i < pixels.Length; i++)
            sum += 0.2126 * pixels[i].r + 0.7152 * pixels[i].g + 0.0722 * pixels[i].b;
        return sum / pixels.Length;
    }

    private static double MeanAbsoluteDifference(Color[] a, Color[] b)
    {
        if (a == null || b == null || a.Length != b.Length || a.Length == 0) return double.NaN;
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += Mathf.Abs(a[i].r - b[i].r) + Mathf.Abs(a[i].g - b[i].g) + Mathf.Abs(a[i].b - b[i].b);
        return sum / (a.Length * 3);
    }
}
