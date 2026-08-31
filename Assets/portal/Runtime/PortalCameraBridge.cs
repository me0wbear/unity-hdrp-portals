using System;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Держит вид непрерывным на переходе. Вешается на тот же объект, что и
/// <see cref="PortalTraveller"/>.
///
/// Всё, что хранит мировую ориентацию отдельно от трансформа, должно быть
/// повёрнуто здесь. Портал поворачивает корень, но история кадров пайплайна и
/// сохранённый угол взгляда контроллера про этот поворот не знают и на
/// следующем же кадре вернут вид в прежнюю сторону.
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
        + "идёт с векторами движения, посчитанными от позы до перехода. Сброс "
        + "перезапускает и адаптацию автоматической экспозиции: при "
        + "автоэкспозиции в сцене это читается как моргание в кадр перехода. "
        + "Выбор из двух зол; с фиксированной экспозицией сброс безвреден.")]
    private bool resetCameraHistory = true;

    [SerializeField]
    [Tooltip("Как согласовать сохранённый угол взгляда UHFPS с поворотом "
        + "перехода. Документация UHFPS держит поворот корня игрока нулевым и "
        + "переносит направление в look rotation; если с режимом добавки камера "
        + "после перехода разворачивается в прежнюю мировую сторону или "
        + "поворот удваивается, переключить на TransferRootYaw. Лог адаптера "
        + "показывает, какие члены UHFPS он нашёл.")]
    private UhfpsLookMode uhfpsLookMode = UhfpsLookMode.AddYawDelta;

    private PortalUhfpsAdapter _uhfps;

    /// <summary>
    /// Точка подключения для систем, которые держат позу камеры сами.
    /// Поднимается на переходе, до сброса истории кадров.
    ///
    /// Через неё подключается интеграция с Cinemachine: она живёт в отдельной
    /// сборке и компилируется, только если пакет есть в проекте. Сам модуль на
    /// Cinemachine не ссылается и собирается без него.
    ///
    /// Аргументы: что произошло при переходе и камера, которой играет игрок.
    /// </summary>
    public static event Action<PortalTeleportContext, Camera> CameraWarped;

    /// <summary>Камера, которой играет игрок. Пустая, если не назначена.</summary>
    public Camera GameplayCamera => gameplayCamera;

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
        CameraWarped?.Invoke(context, gameplayCamera);
        _uhfps.Apply(context, uhfpsLookMode, transform);
        ResetCameraHistory();

        // Виртуальные камеры порталов прыгнули вместе с наблюдателем, и их
        // историю кадров надо выбросить так же, как и его собственную. Это не
        // зависит от resetCameraHistory: тот переключатель отдаёт наружу
        // геймплейную камеру, а до внутренних камер модуля снаружи не добраться.
        PortalSystem.ResetHistory();
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
