using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Walks through a portal and measures how much each frame differs from the one before it. A
/// transition that cannot be felt produces no pair that stands out: the frame after the crossing
/// differs from the frame before it about as much as any other neighbouring pair does. Whatever
/// the cause, a felt transition has to show up here, which is why this measures the symptom
/// rather than any particular suspect.
/// </summary>
public sealed class SeamCheck : MonoBehaviour
{
    public Transform playerRoot;
    public PortalTraveller traveller;
    public UHFPS.Runtime.PlayerStateMachine machine;
    public Vector3 start = new Vector3(0f, 0.1f, -3f);
    public float speed = 3f;
    public int frames = 70;
    public int warmupFrames = 90;
    public string outputDirectory = "SeamCheck";

    /// <summary>Every nth pixel is sampled, which is plenty for a whole frame comparison.</summary>
    public int pixelStride = 3;

    private readonly StringBuilder _report = new StringBuilder();
    private int _teleportFrame = -1;
    private int _frame;

    private IEnumerator Start()
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), outputDirectory);
        Directory.CreateDirectory(directory);

        if (traveller != null)
        {
            traveller.Teleported += OnTeleported;
        }

        for (int f = 0; f < warmupFrames; f++)
        {
            yield return null;
        }

        CharacterController controller = playerRoot.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
        }

        playerRoot.SetPositionAndRotation(start, Quaternion.identity);
        if (traveller != null)
        {
            traveller.ResetPortalTracking();
        }

        if (controller != null)
        {
            controller.enabled = true;
        }

        for (int f = 0; f < 8; f++)
        {
            yield return null;
        }

        // World space velocity, the way UHFPS drives its controller. The bridge has to turn this
        // on a portal that turns, otherwise the walk drifts off its axis.
        if (machine != null)
        {
            machine.Motion = new Vector3(0f, 0f, speed);
        }

        Debug.Log("[SeamCheck] walk starting at " + playerRoot.position.ToString("F2"));

        Color[] previous = null;
        Texture2D previousFrame = null;

        for (_frame = 0; _frame < frames; _frame++)
        {
            Debug.Log("[SeamCheck] frame " + _frame + " at " + playerRoot.position.ToString("F2"));

            if (machine != null && controller != null)
            {
                controller.Move(machine.Motion * Time.deltaTime);
            }

            yield return new WaitForEndOfFrame();

            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            Color[] current = Sample(frame);

            double difference = previous == null ? double.NaN : MeanAbsoluteDifference(previous, current);
            _report.Append(_frame.ToString("00")).Append(' ')
                .Append(double.IsNaN(difference) ? "    -" : difference.ToString("F5"))
                .Append("  lum ").Append(MeanLuminance(current).ToString("F5"))
                .Append(_frame == _teleportFrame ? "  <= crossing" : string.Empty)
                .Append('\n');

            // Keep the frames around the crossing so the difference can be looked at rather than
            // only counted.
            if (_teleportFrame >= 0 && Mathf.Abs(_frame - _teleportFrame) <= 2)
            {
                File.WriteAllBytes(
                    Path.Combine(directory, string.Format("frame{0:00}.png", _frame)),
                    frame.EncodeToPNG());
            }

            previous = current;
            if (previousFrame != null)
            {
                Destroy(previousFrame);
            }

            previousFrame = frame;
        }

        if (previousFrame != null)
        {
            Destroy(previousFrame);
        }

        Debug.Log("[SeamCheck] frame to frame difference, crossing at frame " + _teleportFrame
            + "\n" + _report);
        Application.Quit(0);
    }

    private void OnTeleported(PortalTeleportContext context)
    {
        _teleportFrame = _frame;
    }

    private Color[] Sample(Texture2D frame)
    {
        Color[] pixels = frame.GetPixels();
        int stride = Mathf.Max(1, pixelStride);
        var sampled = new Color[(pixels.Length + stride - 1) / stride];
        for (int i = 0, j = 0; i < pixels.Length; i += stride, j++)
        {
            sampled[j] = pixels[i];
        }

        return sampled;
    }

    /// <summary>
    /// Mean brightness of the frame. A flash shows up here as a step, and its sign says whether
    /// the picture got brighter or darker, which the difference measure alone cannot tell.
    /// </summary>
    private static double MeanLuminance(Color[] pixels)
    {
        if (pixels == null || pixels.Length == 0)
        {
            return double.NaN;
        }

        double sum = 0;
        for (int i = 0; i < pixels.Length; i++)
        {
            sum += 0.2126 * pixels[i].r + 0.7152 * pixels[i].g + 0.0722 * pixels[i].b;
        }

        return sum / pixels.Length;
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
