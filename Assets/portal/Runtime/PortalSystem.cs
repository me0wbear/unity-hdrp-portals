using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Реестр активных порталов и точка, из которой они рендерятся раз в кадр.
/// Стоит после геймплея и обновления камер, но раньше замеров лаборатории,
/// которые сидят на порядке выполнения 3000.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class PortalSystem : MonoBehaviour
{
    private static readonly List<Portal> Portals = new List<Portal>();

    /// <summary>Порталы, включённые прямо сейчас. Порядок — порядок включения.</summary>
    public static IReadOnlyList<Portal> Active => Portals;

    public static void Register(Portal portal)
    {
        if (portal == null || Portals.Contains(portal))
        {
            return;
        }

        Portals.Add(portal);
    }

    public static void Unregister(Portal portal)
    {
        Portals.Remove(portal);
    }
}
