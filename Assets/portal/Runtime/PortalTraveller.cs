using System;
using UnityEngine;

/// <summary>
/// Вешается на корень того, что должно проходить сквозь порталы: на игрока с
/// CharacterController или на объект с Rigidbody.
///
/// Здесь пока только поверхность контракта: типы и подписи, без которых не
/// компилируется приёмка. Детект пересечения и перенос появляются вместе со
/// своими тестами в задаче 9 плана реализации.
/// </summary>
[DefaultExecutionOrder(900)]
public sealed class PortalTraveller : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Точка взгляда. Обычно Transform камеры. Пусто — берётся сам объект.")]
    private Transform viewPoint;

    [SerializeField]
    [Tooltip("Запас, на который проём считается шире при проверке попадания.")]
    private float openingMargin = 0.35f;

    /// <summary>Поднимается после того, как перенос уже применён.</summary>
    public event Action<PortalTeleportContext> Teleported;

    /// <summary>
    /// Точка, по которой засчитывается пересечение. Именно взгляд, а не корень:
    /// игрок должен переноситься в тот момент, когда плоскость пересекает глаз,
    /// иначе кадр после перехода не совпадёт с кадром до него.
    /// </summary>
    public Transform ViewPoint => viewPoint != null ? viewPoint : transform;

    /// <summary>Запас, на который проём считается шире при проверке попадания.</summary>
    public float OpeningMargin => openingMargin;

    /// <summary>
    /// Забывает запомненные расстояния до порталов. Обязательно вызывать после
    /// того, как объект переставили вручную: иначе перестановка читается как
    /// пересечение и объект улетает в парный портал.
    /// </summary>
    public void ResetPortalTracking()
    {
    }

    /// <summary>Поднимает событие перехода. Вызывается из логики переноса.</summary>
    private void RaiseTeleported(PortalTeleportContext context)
    {
        Teleported?.Invoke(context);
    }
}
