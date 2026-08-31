using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

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

    // Якорь Volume, которым модуль переводит грейдинг на сторону назначения.
    private static Transform _volumeAnchor;
    private static HDAdditionalCameraData _volumeAnchorOwner;

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
        _volumeAnchor = null;
        _volumeAnchorOwner = null;
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

        var host = new GameObject("PortalSystem");
        _instance = host.AddComponent<PortalSystem>();
        DontDestroyOnLoad(host);

        // Проход подмены глубины живёт на носителе системы: он глобальный,
        // сам находит нужные порталы и ничего не требует от чужой сцены.
        var passVolume = host.AddComponent<CustomPassVolume>();
        passVolume.isGlobal = true;
        // Точка впрыска — после всей непрозрачной геометрии и до прозрачной.
        // Раньше нельзя по двум причинам сразу. Во-первых, непрозрачные материалы
        // HDRP сравнивают глубину на равенство с той, что осталась от препасса:
        // подменённый буфер отсёк бы всю обычную геометрию кадра. Во-вторых,
        // туман накладывается между препассом и прозрачными, а виртуальная
        // камера свой туман уже нарисовала прямо в содержимом — вплотную к
        // проёму, где квад закрывает весь экран, второй слой выбеливает кадр.
        passVolume.injectionPoint = CustomPassInjectionPoint.BeforeTransparent;
        passVolume.AddPassOfType<PortalCompositePass>();

        // Глубина содержимого снимается копией с уже посчитанного кадра
        // виртуальной камеры, отдельным проходом. Точка впрыска — перед
        // пост-обработкой: прозрачная геометрия к этому моменту уже записала
        // свою глубину, а сам буфер ещё не тронут пост-эффектами.
        var depthCopyVolume = host.AddComponent<CustomPassVolume>();
        depthCopyVolume.isGlobal = true;
        depthCopyVolume.injectionPoint = CustomPassInjectionPoint.BeforePostProcess;
        depthCopyVolume.AddPassOfType<PortalContentDepthCopyPass>();

        // Порядок вызова OnDestroy при выходе не определён, а камеры порталов
        // держат таргеты и подписку на события пайплайна. Освобождаем их по
        // явному сигналу выхода, пока пайплайн ещё жив.
        Application.quitting += ReleaseAll;
    }

    /// <summary>
    /// Освобождает всё немедленно. Вызывается по сигналу выхода: отложенное
    /// уничтожение к этому моменту уже не отработает, и камеры с таргетами
    /// дожили бы до выгрузки пайплайна.
    /// </summary>
    private static void ReleaseAll()
    {
        foreach (PortalRenderer renderer in Renderers.Values)
        {
            renderer.Release();
        }
    }

    /// <summary>
    /// Объявляет историю кадров всех порталов недействительной. Вызывать на
    /// телепорте наблюдателя: виртуальные камеры порталов держат позу,
    /// пересчитанную от его позы, поэтому вместе с ним прыгают и они. Без
    /// сброса пайплайн примет этот прыжок за движение и один кадр проёмы будут
    /// размазаны векторами движения, посчитанными от позы до перехода.
    /// </summary>
    public static void ResetHistory()
    {
        foreach (PortalRenderer renderer in Renderers.Values)
        {
            renderer.ResetHistory();
        }
    }

    /// <summary>Готовы ли у портала буферы глубины и движения содержимого.</summary>
    public static bool HasContentBuffers(Portal portal)
    {
        return portal != null
            && Renderers.TryGetValue(portal, out PortalRenderer renderer)
            && renderer.ContentDepth != null;
    }

    private void LateUpdate()
    {
        int spent = 0;

        Portal blendPortal = null;
        float blendWeight = 0f;

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

            // Тянуть грейдинг может только портал, который сейчас рисуется для
            // этой камеры. Стоящий за спиной или вне поля зрения не показывает
            // ничего, и его сторона назначения игрока не касается.
            float weight = renderer.LevelCount > 0
                ? VolumeBlendWeight(portal, portal.playerCamera)
                : 0f;
            if (weight > blendWeight)
            {
                blendWeight = weight;
                blendPortal = portal;
            }
        }

        ApplyVolumeBlend(blendPortal, blendWeight);
    }

    /// <summary>
    /// Насколько состояние Volume уже должно принадлежать той стороне: ноль
    /// далеко от проёма, единица вплотную к нему. Считается только для того, к
    /// чьему проёму наблюдатель реально подошёл: до бесконечной плоскости портала
    /// можно оказаться близко, стоя в тридцати метрах вбок.
    /// </summary>
    private static float VolumeBlendWeight(Portal portal, Camera viewer)
    {
        if (!portal.blendVolumesThroughPortal || portal.exitPortal == null)
        {
            return 0f;
        }

        Vector3 eye = viewer.transform.position;
        if (!PortalMath.IsInsideOpening(
                portal.transform, eye, portal.OpeningSize, portal.volumeBlendDistance))
        {
            return 0f;
        }

        // Только с лицевой стороны. Портал, к которому наблюдатель стоит спиной,
        // ничего ему не показывает и тянуть его цветокоррекцию не должен. Без
        // этой проверки вышедший из портала попадает в зону переноса того же
        // портала и получает грейдинг комнаты, из которой только что ушёл.
        float distance = PortalMath.SignedDistance(portal.transform, eye);
        if (distance <= 0f)
        {
            return 0f;
        }

        return 1f - Mathf.Clamp01(distance / Mathf.Max(portal.volumeBlendDistance, 0.01f));
    }

    /// <summary>
    /// Двигает якорь Volume главной камеры к позиции, в которой наблюдатель
    /// окажется после перехода.
    ///
    /// Зачем. Виртуальная камера рендерит без пост-обработки, а цветокоррекция —
    /// это пост-обработка. Поэтому вид в проёме получает грейдинг той стороны,
    /// где стоит игрок. Пока комнаты настроены одинаково, это незаметно, но
    /// стоит развести их по цвету — и переход становится прыжком: тёплая
    /// картинка разом сменяется холодной. Сдвиг якоря заранее переводит грейдинг
    /// на сторону назначения, и к моменту перехода менять уже нечего.
    /// </summary>
    private static void ApplyVolumeBlend(Portal portal, float weight)
    {
        if (portal == null || weight <= 0f)
        {
            ReleaseVolumeAnchor();
            return;
        }

        Camera viewer = portal.playerCamera;
        if (!viewer.TryGetComponent(out HDAdditionalCameraData data))
        {
            // Отпустить прежний якорь обязательно: иначе камера, на которой
            // блендинг работал раньше, останется с ним навсегда.
            ReleaseVolumeAnchor();
            return;
        }

        // Чужой якорь не трогаем: его мог поставить сам проект.
        if (data.volumeAnchorOverride != null && data.volumeAnchorOverride != _volumeAnchor)
        {
            ReleaseVolumeAnchor();
            return;
        }

        EnsureVolumeAnchor();

        Vector3 eye = viewer.transform.position;
        Vector3 destination = PortalMath
            .EntranceToExit(portal.transform, portal.exitPortal.transform)
            .MultiplyPoint(eye);

        _volumeAnchor.position = Vector3.Lerp(eye, destination, weight);
        data.volumeAnchorOverride = _volumeAnchor;
        _volumeAnchorOwner = data;
    }

    private static void EnsureVolumeAnchor()
    {
        if (_volumeAnchor == null)
        {
            var anchorObject = new GameObject("PortalVolumeAnchor");
            DontDestroyOnLoad(anchorObject);
            _volumeAnchor = anchorObject.transform;
        }
    }

    private static void ReleaseVolumeAnchor()
    {
        if (_volumeAnchorOwner == null)
        {
            return;
        }

        if (_volumeAnchorOwner.volumeAnchorOverride == _volumeAnchor)
        {
            _volumeAnchorOwner.volumeAnchorOverride = null;
        }

        _volumeAnchorOwner = null;
    }

    private void OnDestroy()
    {
        Application.quitting -= ReleaseAll;
        ReleaseAll();

        Renderers.Clear();
        Portals.Clear();
        _instance = null;
    }
}
