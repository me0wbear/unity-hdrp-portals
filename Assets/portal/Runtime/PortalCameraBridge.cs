using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Держит вид непрерывным на переходе. Вешается на тот же объект, что и
/// <see cref="PortalTraveller"/>.
///
/// Всё, что хранит мировую ориентацию отдельно от трансформа, должно быть
/// повёрнуто здесь. Портал поворачивает корень, но состояние Cinemachine,
/// история кадров HDRP и сохранённый угол взгляда контроллера про этот поворот
/// не знают и на следующем же кадре вернут вид в прежнюю сторону.
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

    [SerializeField]
    [Tooltip("Сбрасывать историю кадров HDRP на переходе. Без сброса один кадр "
        + "идёт с векторами движения, посчитанными от позы до перехода.")]
    private bool resetCameraHistory = true;

    private PortalUhfpsAdapter _uhfps;

    private void Reset()
    {
        traveller = GetComponent<PortalTraveller>();
        gameplayCamera = GetComponentInChildren<Camera>();
    }

    private void OnEnable()
    {
        if (traveller == null)
        {
            traveller = GetComponent<PortalTraveller>();
        }

        if (gameplayCamera == null)
        {
            gameplayCamera = GetComponentInChildren<Camera>();
        }

        _uhfps ??= new PortalUhfpsAdapter(gameObject);

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
        WarpCinemachine(context);
        _uhfps.Apply(context);
        ResetCameraHistory();
    }

    /// <summary>
    /// Переносит состояние Cinemachine вслед за целью. Без этого брейн
    /// интерполирует из позы до перехода и даёт наплыв через полмира, а активная
    /// виртуальная камера продолжает смотреть в прежнюю мировую сторону.
    /// </summary>
    private void WarpCinemachine(PortalTeleportContext context)
    {
        if (gameplayCamera == null
            || !gameplayCamera.TryGetComponent(out CinemachineBrain brain))
        {
            return;
        }

        // Позиция уже перенесена, поэтому прежняя восстанавливается обратной матрицей.
        Vector3 newPosition = transform.position;
        Vector3 oldPosition = context.Transform.inverse.MultiplyPoint(newPosition);
        Vector3 delta = newPosition - oldPosition;

        WarpTarget(transform, delta);

        if (brain.ActiveVirtualCamera is CinemachineVirtualCameraBase active)
        {
            // Камера следит не обязательно за корнем: обычно цель — держатель
            // камеры, дочерний объект игрока. Сообщение о переносе сопоставляется
            // с целью по ссылке, поэтому корня мало: не совпав, оно молча ничего
            // не делает, а камера потом навёрстывает разрыв с демпфированием и
            // даёт рывок через кадр после перехода.
            WarpTarget(active.Follow, delta);
            WarpTarget(active.LookAt, delta);

            active.OnTargetObjectWarped(transform, delta);

            // Принудительно ставить позу камеры здесь нельзя. Камера с
            // демпфированием всегда отстаёт от цели, и любая установка позы
            // это отставание обнуляет: камера прыгает вперёд, а потом заново
            // разгоняется с нуля. Замер CrossCheck по камере Cinemachine:
            // с принудительной установкой шаг на переходе доходил до 0,518 при
            // номинале 0,050, без неё максимум 0,064. Сообщение о переносе —
            // штатный механизм Cinemachine для телепорта, и его достаточно.
        }

        // Переход — это склейка, а не движение. Незакрытый переход между кадрами
        // проехал бы весь путь от старой позы к новой на глазах у игрока.
        brain.ResetState();
    }

    /// <summary>
    /// Сообщает о переносе одной цели. Дубли безвредны: Cinemachine сопоставляет
    /// цель по ссылке и на незнакомую просто не реагирует.
    /// </summary>
    private static void WarpTarget(Transform target, Vector3 delta)
    {
        if (target != null)
        {
            CinemachineCore.OnTargetObjectWarped(target, delta);
        }
    }

    /// <summary>
    /// HDRP телепорт сам не обнаруживает: сброс истории взводится только при
    /// смене режима сглаживания, смене режима очистки и на первом кадре. Без
    /// сброса один кадр идёт с векторами движения всей сцены, посчитанными от
    /// позы до перехода, то есть с мусором.
    /// </summary>
    private void ResetCameraHistory()
    {
        if (resetCameraHistory && gameplayCamera != null)
        {
            HDCamera.GetOrCreate(gameplayCamera).Reset();
        }
    }
}
