using UnityEngine;

// Minimal stand-ins that match the type names, namespace and field signatures the portal
// bridge looks for in UHFPS. They exist only so the reflection path can be exercised in this
// lab without pulling the whole asset in. Field names and types mirror the real ones:
//   UHFPS.Runtime.LookController      -> public Vector2 LookRotation, public ForwardStyle PlayerForward
//   UHFPS.Runtime.PlayerStateMachine  -> public Vector3 Motion
namespace UHFPS.Runtime
{
    public class LookController : MonoBehaviour
    {
        public enum ForwardStyle { RootForward, LookForward }

        public ForwardStyle PlayerForward = ForwardStyle.LookForward;
        public Vector2 LookRotation;

        public Transform body;
        public Transform head;

        private void LateUpdate()
        {
            // Same shape as the real one: rewrite transforms from the stored angles every frame.
            if (PlayerForward == ForwardStyle.LookForward)
            {
                if (head != null)
                {
                    head.localRotation = Quaternion.Euler(LookRotation.y, LookRotation.x, 0f);
                }
            }
            else
            {
                if (body != null)
                {
                    body.localRotation = Quaternion.Euler(0f, LookRotation.x, 0f);
                }

                if (head != null)
                {
                    head.localRotation = Quaternion.Euler(LookRotation.y, 0f, 0f);
                }
            }
        }
    }

    public class PlayerStateMachine : MonoBehaviour
    {
        public Vector3 Motion;
    }
}
