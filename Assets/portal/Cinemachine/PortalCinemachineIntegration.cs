using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Переносит состояние Cinemachine вслед за игроком на переходе.
///
/// Живёт в отдельной сборке, которая компилируется только при наличии пакета
/// Cinemachine: сам модуль порталов на него не ссылается и собирается без него.
/// Подключается сама, ничего добавлять в сцену не нужно.
/// </summary>
internal static class PortalCinemachineIntegration
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Register()
    {
        PortalCameraBridge.CameraWarped -= Warp;
        PortalCameraBridge.CameraWarped += Warp;
    }

    private static void Warp(PortalTeleportContext context, Camera gameplayCamera)
    {
        if (gameplayCamera == null
            || context.Traveller == null
            || !gameplayCamera.TryGetComponent(out CinemachineBrain brain))
        {
            return;
        }

        Transform traveller = context.Traveller.transform;

        // Позиция уже перенесена, поэтому прежняя восстанавливается обратной матрицей.
        Vector3 newPosition = traveller.position;
        Vector3 oldPosition = context.Transform.inverse.MultiplyPoint(newPosition);
        Vector3 delta = newPosition - oldPosition;

        WarpTarget(traveller, delta);

        if (brain.ActiveVirtualCamera is CinemachineVirtualCameraBase active)
        {
            // Камера следит не обязательно за корнем: обычно цель — держатель
            // камеры, дочерний объект игрока. Сообщение о переносе сопоставляется
            // с целью по ссылке, поэтому корня мало: не совпав, оно молча ничего
            // не делает, а камера потом навёрстывает разрыв с демпфированием и
            // даёт рывок через кадр после перехода.
            WarpTarget(active.Follow, delta);
            WarpTarget(active.LookAt, delta);

            active.OnTargetObjectWarped(traveller, delta);

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
}
