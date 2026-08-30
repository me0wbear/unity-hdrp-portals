using System.Collections;
using System.IO;
using UnityEngine;

/// <summary>
/// Drives the portal lab scene through a scripted camera path and writes one PNG per
/// waypoint so the render can be inspected outside the editor.
/// </summary>
public sealed class LabCapture : MonoBehaviour
{
    [System.Serializable]
    public struct Shot
    {
        public string name;
        public Vector3 position;
        public Vector3 eulerAngles;
    }

    public Transform playerRoot;
    public Camera playerCamera;
    public MeshRenderer portalScreen;
    public PortalTraveller traveller;
    public Shot[] shots;
    public string outputDirectory = "LabCaptures";
    public Vector3 walkStart = new Vector3(0f, 1f, -3f);
    public int warmupFrames = 40;
    public int settleFrames = 8;

    private readonly System.Text.StringBuilder _walkTrace = new System.Text.StringBuilder();
    private readonly System.Text.StringBuilder _brightness = new System.Text.StringBuilder();
    private readonly System.Text.StringBuilder _sharpness = new System.Text.StringBuilder();

    /// <summary>
    /// Walks the player straight through the portal with CharacterController.Move so trigger
    /// tracking and the teleport run exactly as they would in game, capturing every step.
    /// </summary>
    private IEnumerator Walk(string directory)
    {
        if (playerRoot == null)
        {
            yield break;
        }

        CharacterController controller = playerRoot.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.enabled = false;
        }

        playerRoot.SetPositionAndRotation(walkStart, Quaternion.identity);
        if (traveller != null)
        {
            traveller.ResetPortalTracking();
        }

        if (controller != null)
        {
            controller.enabled = true;
        }

        for (int f = 0; f < 4; f++)
        {
            yield return null;
        }

        _walkTrace.Append("START rootFwd=").Append(playerRoot.forward.ToString("F2"))
            .Append(" camFwd=")
            .Append(playerCamera != null ? playerCamera.transform.forward.ToString("F2") : "none")
            .Append(' ');

        string walkDirectory = Path.Combine(directory, "walk");
        Directory.CreateDirectory(walkDirectory);

        for (int step = 0; step < 32; step++)
        {
            Vector3 before = playerRoot.position;
            if (controller != null)
            {
                controller.Move(playerRoot.forward * 0.15f);
            }
            else
            {
                playerRoot.position += playerRoot.forward * 0.15f;
            }

            // Every frame is sampled, including the transition frame itself: a flash lasts one
            // frame, so sampling only once per step would step right over it.
            for (int f = 0; f < 4; f++)
            {
                yield return new WaitForEndOfFrame();

                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                if (f == 0 || f == 3)
                {
                    // f == 0 is the frame the movement (and any teleport) lands on: that is where
                    // a motion-blur smear would be. f == 3 is the settled frame.
                    File.WriteAllBytes(
                        Path.Combine(walkDirectory, string.Format("{0:00}_f{1}.png", step, f)),
                        frame.EncodeToPNG());
                }

                Color[] pixels = frame.GetPixels();
                double sum = 0;
                int taken = 0;
                for (int p = 0; p < pixels.Length; p += 37)
                {
                    Color c = pixels[p];
                    sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    taken++;
                }

                _brightness.Append((sum / taken).ToString("F3")).Append(' ');
                _sharpness.Append(MeasureSharpness(pixels, frame.width, frame.height).ToString("F4"))
                    .Append(' ');
                Destroy(frame);

                Vector3 now = playerRoot.position;
                if (Vector3.Distance(before, now) > 1f)
                {
                    // Record where the body and the eye were looking on both sides of the jump:
                    // a portal pair at an angle must rotate the view by that same angle.
                    _walkTrace.Append("JUMP@step").Append(step).Append("frame").Append(f)
                        .Append(" rootFwd=").Append(playerRoot.forward.ToString("F2"))
                        .Append(" camFwd=")
                        .Append(playerCamera != null
                            ? playerCamera.transform.forward.ToString("F2")
                            : "none")
                        .Append(" camPos=")
                        .Append(playerCamera != null
                            ? playerCamera.transform.position.ToString("F1")
                            : "none")
                        .Append(' ');
                    before = now;
                }
            }

            _brightness.Append("| ");
        }

        Debug.Log("[LabCapture] walk finished at " + playerRoot.position);
        Debug.Log("[LabCapture] walk trace: " + _walkTrace);
        Debug.Log("[LabCapture] walk brightness: " + _brightness);
        Debug.Log("[LabCapture] walk sharpness: " + _sharpness);
        yield return MeasureCost();
    }

    /// <summary>
    /// Mean absolute horizontal luminance gradient: a blurred frame loses high-frequency detail,
    /// so motion blur on the transition frame shows up as a dip in this value.
    /// </summary>
    private static double MeasureSharpness(Color[] pixels, int width, int height)
    {
        double sum = 0;
        int taken = 0;
        for (int y = 4; y < height - 4; y += 7)
        {
            int row = y * width;
            for (int x = 4; x < width - 5; x += 3)
            {
                Color a = pixels[row + x];
                Color b = pixels[row + x + 1];
                double la = 0.2126 * a.r + 0.7152 * a.g + 0.0722 * a.b;
                double lb = 0.2126 * b.r + 0.7152 * b.g + 0.0722 * b.b;
                sum += la > lb ? la - lb : lb - la;
                taken++;
            }
        }

        return taken > 0 ? sum / taken : 0;
    }

    /// <summary>
    /// Samples mean screen brightness for the frames straight after a teleport. A visible flash
    /// shows up here as one or two frames well outside the surrounding values.
    /// </summary>
    private IEnumerator LogBrightnessAcrossCut(int step)
    {
        var values = new System.Text.StringBuilder();
        for (int i = 0; i < 10; i++)
        {
            yield return new WaitForEndOfFrame();
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            Color[] pixels = frame.GetPixels();

            double sum = 0;
            for (int p = 0; p < pixels.Length; p += 37)
            {
                Color c = pixels[p];
                sum += 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
            }

            double mean = sum / (pixels.Length / 37 + 1);
            values.Append(i == 0 ? "" : " ").Append(mean.ToString("F4"));
            Destroy(frame);
        }

        Debug.Log("[LabCapture] brightness after teleport (step " + step + "): " + values);
    }

    /// <summary>
    /// Times frames with the portals rendering and again with every portal disabled, so the cost
    /// the module adds to this scene is measured rather than guessed.
    /// </summary>
    private IEnumerator MeasureCost()
    {
        yield return MeasureAt("close", new Vector3(0f, 0.05f, -4f));
        yield return MeasureAt("far", new Vector3(0f, 0.05f, -13f));
    }

    private IEnumerator MeasureAt(string label, Vector3 position)
    {
        if (playerRoot != null)
        {
            CharacterController controller = playerRoot.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            playerRoot.SetPositionAndRotation(position, Quaternion.identity);
            if (traveller != null)
            {
                traveller.ResetPortalTracking();
            }

            if (controller != null)
            {
                controller.enabled = true;
            }
        }

        Portal[] portals = FindObjectsByType<Portal>(FindObjectsSortMode.None);

        float portalsOn = 0f;
        yield return AverageFrameMs(value => portalsOn = value);

        for (int i = 0; i < portals.Length; i++)
        {
            portals[i].enabled = false;
        }

        float portalsOff = 0f;
        yield return AverageFrameMs(value => portalsOff = value);

        for (int i = 0; i < portals.Length; i++)
        {
            portals[i].enabled = true;
        }

        Debug.Log("[LabCapture] frame cost (" + label + " at z=" + position.z + "): portals on "
            + portalsOn.ToString("F2") + " ms, off " + portalsOff.ToString("F2")
            + " ms, delta " + (portalsOn - portalsOff).ToString("F2") + " ms ("
            + portals.Length + " portals, recursion depth 2)");
    }

    private IEnumerator AverageFrameMs(System.Action<float> report)
    {
        // Discard the first frames so shader warm-up and the enable change settle.
        for (int i = 0; i < 40; i++)
        {
            yield return null;
        }

        double sum = 0;
        const int samples = 90;
        for (int i = 0; i < samples; i++)
        {
            yield return null;
            sum += Time.unscaledDeltaTime * 1000.0;
        }

        report((float)(sum / samples));
    }

    /// <summary>
    /// Writes what the portal surface is actually sampling, so the render target can be
    /// compared against the composited frame.
    /// </summary>
    private void DumpPortalTexture(string directory, int index, string shotName)
    {
        if (portalScreen == null)
        {
            return;
        }

        var block = new MaterialPropertyBlock();
        portalScreen.GetPropertyBlock(block);
        Texture texture = block.GetTexture("_MainTex");
        float mask = block.GetFloat("_DisplayMask");
        if (texture == null)
        {
            Debug.Log("[LabCapture] portal texture missing for " + shotName + " mask=" + mask);
            return;
        }

        var source = texture as RenderTexture;
        if (source == null)
        {
            Debug.Log("[LabCapture] portal texture is " + texture.name + " (" + texture.GetType().Name
                + ") for " + shotName + " mask=" + mask);
            return;
        }

        RenderTexture previous = RenderTexture.active;
        var readback = new Texture2D(source.width, source.height, TextureFormat.RGBAFloat, false, true);
        try
        {
            RenderTexture.active = source;
            readback.ReadPixels(new Rect(0, 0, source.width, source.height), 0, 0);
            readback.Apply();

            Color[] pixels = readback.GetPixels();
            double top = 0;
            double bottom = 0;
            int topCount = 0;
            int bottomCount = 0;
            var output = new Texture2D(source.width, source.height, TextureFormat.RGBA32, false, false);
            for (int y = 0; y < source.height; y++)
            {
                for (int x = 0; x < source.width; x++)
                {
                    Color c = pixels[y * source.width + x];
                    double luminance = 0.2126 * c.r + 0.7152 * c.g + 0.0722 * c.b;
                    if (y > source.height * 2 / 3)
                    {
                        top += luminance;
                        topCount++;
                    }
                    else if (y < source.height / 3)
                    {
                        bottom += luminance;
                        bottomCount++;
                    }

                    pixels[y * source.width + x] = new Color(
                        Mathf.LinearToGammaSpace(c.r / (1f + c.r)),
                        Mathf.LinearToGammaSpace(c.g / (1f + c.g)),
                        Mathf.LinearToGammaSpace(c.b / (1f + c.b)),
                        1f);
                }
            }

            output.SetPixels(pixels);
            output.Apply();
            File.WriteAllBytes(
                Path.Combine(directory, string.Format("{0:00}_{1}_RT.png", index, shotName)),
                output.EncodeToPNG());
            Destroy(output);

            Debug.Log("[LabCapture] portal RT " + shotName
                + " camera=" + (playerCamera != null ? playerCamera.transform.position.ToString("F2") : "?")
                + " forcedOff=" + portalScreen.forceRenderingOff
                + " name=" + source.name
                + " size=" + source.width + "x" + source.height
                + " mask=" + mask
                + " topMean=" + (topCount > 0 ? top / topCount : 0).ToString("F3")
                + " bottomMean=" + (bottomCount > 0 ? bottom / bottomCount : 0).ToString("F3"));
        }
        finally
        {
            RenderTexture.active = previous;
            Destroy(readback);
        }
    }

    private IEnumerator Start()
    {
        string directory = Path.Combine(Directory.GetCurrentDirectory(), outputDirectory);
        Directory.CreateDirectory(directory);

        for (int i = 0; i < warmupFrames; i++)
        {
            yield return null;
        }

        for (int i = 0; i < shots.Length; i++)
        {
            Shot shot = shots[i];
            if (playerRoot != null)
            {
                CharacterController controller = playerRoot.GetComponent<CharacterController>();
                if (controller != null)
                {
                    controller.enabled = false;
                }

                playerRoot.SetPositionAndRotation(shot.position, Quaternion.Euler(shot.eulerAngles));

                // Warping the player is not locomotion, so portal crossing state must be dropped.
                if (traveller != null)
                {
                    traveller.ResetPortalTracking();
                }

                if (controller != null)
                {
                    controller.enabled = true;
                }
            }

            for (int f = 0; f < settleFrames; f++)
            {
                yield return null;
            }

            yield return new WaitForEndOfFrame();
            Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
            byte[] png = frame.EncodeToPNG();
            string file = Path.Combine(directory, string.Format("{0:00}_{1}.png", i, shot.name));
            File.WriteAllBytes(file, png);
            Destroy(frame);
            Debug.Log("[LabCapture] wrote " + file);
            DumpPortalTexture(directory, i, shot.name);
        }

        yield return Walk(directory);

        Debug.Log("[LabCapture] complete");
        yield return null;
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }
}
