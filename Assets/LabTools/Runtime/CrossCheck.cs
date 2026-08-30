using System.Collections;
using System.Text;
using UnityEngine;

/// <summary>
/// Walks straight through a portal at a constant speed and records, for every frame, how far the
/// eye advanced along its path and how large the portal buffer was. Progress is measured towards
/// the entrance before the crossing and away from the exit after it, so a seamless transition
/// produces one unbroken run of equal steps. A step that is larger than its neighbours is the
/// player being thrown forward; a change in buffer width is the image changing resolution.
/// </summary>
[DefaultExecutionOrder(3000)]
public sealed class CrossCheck : MonoBehaviour
{
    public Transform playerRoot;
    public Transform viewPoint;
    public Portal entrance;
    public Portal exit;
    public PortalTraveller traveller;

    public Vector3 start = new Vector3(0f, 0.1f, -3f);
    public float stepPerFrame = 0.05f;
    public int frames = 90;

    private readonly StringBuilder _log = new StringBuilder();
    private CharacterController _controller;
    private bool _running;
    private bool _teleported;
    private float _previousProgress = float.NaN;

    private IEnumerator Start()
    {
        _controller = playerRoot.GetComponent<CharacterController>();

        if (traveller != null)
        {
            traveller.Teleported += OnTeleported;
        }

        for (int f = 0; f < 60; f++)
        {
            yield return null;
        }

        if (_controller != null)
        {
            _controller.enabled = false;
        }

        playerRoot.SetPositionAndRotation(start, Quaternion.identity);
        if (traveller != null)
        {
            traveller.ResetPortalTracking();
        }

        if (_controller != null)
        {
            _controller.enabled = true;
        }

        for (int f = 0; f < 6; f++)
        {
            yield return null;
        }

        _running = true;

        for (int f = 0; f < frames; f++)
        {
            if (_controller != null)
            {
                _controller.Move(playerRoot.forward * stepPerFrame);
            }
            else
            {
                playerRoot.position += playerRoot.forward * stepPerFrame;
            }

            yield return null;
        }

        _running = false;
        Debug.Log("[CrossCheck] step  buffer\n" + _log);
        Application.Quit(0);
    }

    private void OnTeleported(PortalTeleportContext context)
    {
        _teleported = true;
    }

    private void LateUpdate()
    {
        if (!_running)
        {
            return;
        }

        // Before the crossing the eye is closing on the entrance, afterwards it is leaving the
        // exit. Both are the same journey, so the two measures continue one another.
        Portal reference = _teleported ? exit : entrance;
        if (reference == null)
        {
            return;
        }

        float signed = Vector3.Dot(
            reference.transform.forward, viewPoint.position - reference.transform.position);
        float progress = _teleported ? signed : -signed;

        string step = float.IsNaN(_previousProgress)
            ? "    -"
            : (progress - _previousProgress).ToString("F4");
        _previousProgress = progress;

        _log.Append(step).Append("  ").Append(BufferWidth()).Append(_teleported ? "  (after)" : string.Empty)
            .Append('\n');
    }

    private string BufferWidth()
    {
        MeshRenderer screen = _teleported ? ExitScreen() : EntranceScreen();
        if (screen == null)
        {
            return "-";
        }

        var block = new MaterialPropertyBlock();
        screen.GetPropertyBlock(block);
        Texture texture = block.GetTexture("_MainTex");
        return texture != null ? texture.width + "x" + texture.height : "none";
    }

    private MeshRenderer EntranceScreen()
    {
        return entrance != null ? entrance.screen : null;
    }

    private MeshRenderer ExitScreen()
    {
        return exit != null ? exit.screen : null;
    }
}
