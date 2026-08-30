using UnityEngine;

/// <summary>
/// Что именно произошло при переходе. Передаётся подписчикам события
/// <see cref="PortalTraveller.Teleported"/> уже после того, как перенос применён.
/// </summary>
public readonly struct PortalTeleportContext
{
    /// <summary>Портал, сквозь который прошли.</summary>
    public Portal Entrance { get; }

    /// <summary>Портал, из которого вышли.</summary>
    public Portal Exit { get; }

    /// <summary>Мировая матрица, переносящая позу от входа к выходу.</summary>
    public Matrix4x4 Transform { get; }

    /// <summary>
    /// Поворотная часть <see cref="Transform"/>. На неё нужно повернуть всё, что
    /// хранит мировой угол или мировую скорость отдельно от трансформа: иначе
    /// на следующем кадре оно вернёт объект в прежнюю ориентацию.
    /// </summary>
    public Quaternion Rotation { get; }

    /// <summary>Кто прошёл.</summary>
    public PortalTraveller Traveller { get; }

    public PortalTeleportContext(
        Portal entrance, Portal exit, Matrix4x4 transform, PortalTraveller traveller)
    {
        Entrance = entrance;
        Exit = exit;
        Transform = transform;
        Rotation = transform.rotation;
        Traveller = traveller;
    }
}
