using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Реестр активных порталов и точка, из которой они рендерятся раз в кадр.
///
/// Носитель создаётся сам при первом включённом портале: интеграция в чужую
/// сцену не должна требовать, чтобы кто-то не забыл положить объект в иерархию.
/// Порядок выполнения — после геймплея и обновления камер, но раньше замеров
/// лаборатории, которые сидят на 3000.
/// </summary>
[DefaultExecutionOrder(1000)]
public sealed class PortalSystem : MonoBehaviour
{
    /// <summary>
    /// Максимум одновременно живых уровней на всю сцену. Каждый уровень — это
    /// отдельная камера и таргет размером с экран, поэтому потолок нужен: без
    /// него две пары порталов с глубиной 2 съедают шесть экранных буферов.
    /// </summary>
    public static int Budget = 8;

    private static readonly List<Portal> Portals = new List<Portal>();
    private static readonly Dictionary<Portal, PortalRenderer> Renderers =
        new Dictionary<Portal, PortalRenderer>();

    private static PortalSystem _instance;

    /// <summary>Порталы, включённые прямо сейчас. Порядок — порядок включения.</summary>
    public static IReadOnlyList<Portal> Active => Portals;

    /// <summary>
    /// Сбрасывает статическое состояние при запуске. Нужно потому, что при
    /// выключенной перезагрузке домена статические поля переживают выход из
    /// режима игры и во второй запуск приходят с мусором прошлого.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Portals.Clear();
        Renderers.Clear();
        _instance = null;
    }

    public static void Register(Portal portal)
    {
        if (portal == null || Portals.Contains(portal))
        {
            return;
        }

        Portals.Add(portal);
        Renderers[portal] = new PortalRenderer(portal);
        EnsureInstance();
    }

    public static void Unregister(Portal portal)
    {
        Portals.Remove(portal);

        if (Renderers.TryGetValue(portal, out PortalRenderer renderer))
        {
            renderer.Release();
            Renderers.Remove(portal);
        }
    }

    private static void EnsureInstance()
    {
        if (_instance != null)
        {
            return;
        }

        var host = new GameObject("PortalSystem") { hideFlags = HideFlags.HideAndDontSave };
        _instance = host.AddComponent<PortalSystem>();
        DontDestroyOnLoad(host);
    }

    private void LateUpdate()
    {
        int spent = 0;

        for (int i = 0; i < Portals.Count; i++)
        {
            Portal portal = Portals[i];
            if (portal == null || portal.playerCamera == null)
            {
                continue;
            }

            if (!Renderers.TryGetValue(portal, out PortalRenderer renderer))
            {
                continue;
            }

            PortalAperture.Fit(portal, portal.playerCamera);

            // Бюджет режет глубину рекурсии, а не сами порталы: лучше показать
            // все проёмы мельче, чем часть проёмов чёрными.
            int wanted = Mathf.Max(1, portal.recursionDepth + 1);
            int allowed = Mathf.Clamp(Budget - spent, 0, wanted);

            renderer.Render(portal.playerCamera, allowed);
            spent += renderer.LevelCount;
        }
    }

    private void OnDestroy()
    {
        foreach (PortalRenderer renderer in Renderers.Values)
        {
            renderer.Release();
        }

        Renderers.Clear();
        Portals.Clear();
        _instance = null;
    }
}
