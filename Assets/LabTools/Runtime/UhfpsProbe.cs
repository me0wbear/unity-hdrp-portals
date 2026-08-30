using UnityEngine;

/// <summary>
/// Drives the UHFPS stand-ins through a portal and reports whether the bridge turned the
/// stored look angle and the stored velocity. Both are what UHFPS actually uses to decide
/// where the player faces and which way it keeps moving.
/// </summary>
public sealed class UhfpsProbe : MonoBehaviour
{
    public PortalTraveller traveller;
    public UHFPS.Runtime.LookController look;
    public UHFPS.Runtime.PlayerStateMachine machine;

    private void OnEnable()
    {
        if (traveller != null)
        {
            traveller.Teleported += Report;
        }
    }

    private void OnDisable()
    {
        if (traveller != null)
        {
            traveller.Teleported -= Report;
        }
    }

    private void Start()
    {
        // A velocity pointing straight down +Z, the direction the walk test moves in.
        if (machine != null)
        {
            machine.Motion = new Vector3(0f, 0f, 5f);
        }
    }

    private void Report(PortalTeleportContext context)
    {
        // Read on the next frame so the bridge, which is also a subscriber, has already run.
        StartCoroutine(ReportNextFrame(context));
    }

    private System.Collections.IEnumerator ReportNextFrame(PortalTeleportContext context)
    {
        yield return null;

        Debug.Log("[UhfpsProbe] after teleport:"
            + " yaw=" + (look != null ? look.LookRotation.x.ToString("F1") : "?")
            + " pitch=" + (look != null ? look.LookRotation.y.ToString("F1") : "?")
            + " motion=" + (machine != null ? machine.Motion.ToString("F2") : "?")
            + " rootFwd=" + traveller.transform.forward.ToString("F2"));
    }
}
