using UnityEngine;

/// <summary>
/// Замер перехода в живой сессии, когда сценой управляет человек, а не скрипт
/// захвата. Пишет в лог, что именно изменилось при переходе: насколько повернулся
/// вид и сколько времени заняло восстановление плавности кадров после него.
///
/// Существует потому, что автоматические проверки ходят по фиксированному
/// маршруту и не поймают то, что заметно только при живом управлении.
/// </summary>
public sealed class PortalTransitionProbe : MonoBehaviour
{
    [Tooltip("Травеллер, за переходами которого нужно следить.")]
    public PortalTraveller traveller;

    [Tooltip("Сколько кадров после перехода измерять время кадра.")]
    [Min(1)] public int framesAfterTeleport = 20;

    private int _remainingFrames;
    private float _worstFrameTime;
    private float _teleportFrameTime;

    private void OnEnable()
    {
        if (traveller != null)
        {
            traveller.Teleported += OnTeleported;
        }
    }

    private void OnDisable()
    {
        if (traveller != null)
        {
            traveller.Teleported -= OnTeleported;
        }
    }

    private void OnTeleported(PortalTeleportContext context)
    {
        _teleportFrameTime = Time.unscaledDeltaTime;
        _worstFrameTime = 0f;
        _remainingFrames = framesAfterTeleport;

        Debug.Log("[PortalTransitionProbe] teleported"
            + " from " + (context.Entrance != null ? context.Entrance.name : "?")
            + " to " + (context.Exit != null ? context.Exit.name : "?")
            + " yaw=" + context.Rotation.eulerAngles.y.ToString("F1")
            + " frameTime=" + (_teleportFrameTime * 1000f).ToString("F2") + " ms");
    }

    private void LateUpdate()
    {
        if (_remainingFrames <= 0)
        {
            return;
        }

        _worstFrameTime = Mathf.Max(_worstFrameTime, Time.unscaledDeltaTime);
        _remainingFrames--;

        if (_remainingFrames == 0)
        {
            // Заметный провал сразу после перехода — признак того, что перенос
            // тянет за собой пересоздание таргетов или сброс истории кадров.
            Debug.Log("[PortalTransitionProbe] settled:"
                + " teleportFrame=" + (_teleportFrameTime * 1000f).ToString("F2") + " ms"
                + " worstAfter=" + (_worstFrameTime * 1000f).ToString("F2") + " ms"
                + " over " + framesAfterTeleport + " frames");
        }
    }
}
