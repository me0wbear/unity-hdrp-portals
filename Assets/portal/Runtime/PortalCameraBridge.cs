using UnityEngine;

/// <summary>
/// Держит вид непрерывным на переходе. Вешается на тот же объект, что и
/// <see cref="PortalTraveller"/>. Всё, что хранит мировую ориентацию отдельно от
/// трансформа — состояние Cinemachine, история кадров HDRP, сохранённый угол
/// взгляда контроллера, — должно быть повёрнуто здесь, иначе на следующем кадре
/// оно вернёт вид в прежнюю сторону.
///
/// Здесь пока только поверхность контракта: поля, без которых не компилируется
/// приёмка. Обработка перехода появляется в задаче 11 плана реализации.
/// </summary>
[RequireComponent(typeof(PortalTraveller))]
public sealed class PortalCameraBridge : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Травеллер, за переходами которого нужно следить.")]
    private PortalTraveller traveller;

    [SerializeField]
    [Tooltip("Камера, которой играет игрок.")]
    private Camera gameplayCamera;

    private void Reset()
    {
        traveller = GetComponent<PortalTraveller>();
        gameplayCamera = GetComponentInChildren<Camera>();
    }

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

    /// <summary>
    /// Здесь будут: разворот состояния Cinemachine, сброс истории кадров HDRP на
    /// <see cref="gameplayCamera"/> и поворот сохранённого угла взгляда контроллера.
    /// </summary>
    private void OnTeleported(PortalTeleportContext context)
    {
    }
}
