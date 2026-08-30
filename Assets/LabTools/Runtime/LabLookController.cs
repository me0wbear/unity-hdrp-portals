using UnityEngine;

/// <summary>
/// Stand-in for a typical hand-written first-person controller: it keeps yaw and pitch in its
/// own fields and rewrites the transforms every frame from them. That rewrite is what makes a
/// teleport appear not to rotate the view — the portal turns the body, and the next frame this
/// puts it straight back to the stored world angles.
/// </summary>
public sealed class LabLookController : MonoBehaviour
{
    public Transform body;
    public Transform head;
    public float yaw;
    public float pitch;

    private void LateUpdate()
    {
        if (body != null)
        {
            body.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        if (head != null)
        {
            head.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
