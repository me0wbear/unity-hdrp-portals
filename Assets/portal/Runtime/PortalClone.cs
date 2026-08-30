using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Двойник путешественника по ту сторону портала.
///
/// Пока путешественник пересекает плоскость, его половина уже прошла, а половина
/// ещё нет. Без двойника он выглядит въезжающим в стену с одной стороны и
/// возникающим из ниоткуда с другой. Оригинал режется плоскостью входа и держит
/// непрошедшую часть, двойник режется плоскостью выхода и держит прошедшую.
///
/// Двойник — только изображение: он собирается из одних мешей оригинала. Ни
/// скриптов, ни коллайдеров, ни камеры на нём нет и быть не может, потому что
/// они туда просто не копируются.
///
/// Ограничение: двойник собирается из обычных мешей и повторяет только позу
/// корня. Скелетная анимация не переносится, скелетные рендереры пропускаются.
/// Годится для жёсткой геометрии — предметов, ящиков, простого тела игрока.
/// </summary>
public sealed class PortalClone
{
    private static readonly int SliceCentreId = Shader.PropertyToID("_SliceCentre");
    private static readonly int SliceNormalId = Shader.PropertyToID("_SliceNormal");
    private static readonly int SliceEnabledId = Shader.PropertyToID("_SliceEnabled");

    private readonly Transform _source;
    private readonly List<Renderer> _sourceRenderers = new List<Renderer>();
    private readonly List<Renderer> _cloneRenderers = new List<Renderer>();

    private MaterialPropertyBlock _block;
    private GameObject _clone;
    private bool _visible;

    /// <summary>Умеют ли все материалы двойника отсекаться плоскостью.</summary>
    private bool _sliceable;

    public PortalClone(Transform source)
    {
        _source = source;
        _source.GetComponentsInChildren(true, _sourceRenderers);
    }

    /// <summary>Виден ли двойник прямо сейчас.</summary>
    public bool IsVisible => _visible;

    /// <summary>
    /// Пересекает ли путешественник плоскость портала. Считается по общим
    /// границам его видимой геометрии, а не по точке: точка пересекает плоскость
    /// мгновенно, а тело имеет толщину и висит на ней несколько кадров.
    /// </summary>
    public bool StraddlesPlane(Portal portal)
    {
        if (portal == null || _sourceRenderers.Count == 0)
        {
            return false;
        }

        float minimum = float.MaxValue;
        float maximum = float.MinValue;

        for (int i = 0; i < _sourceRenderers.Count; i++)
        {
            Renderer renderer = _sourceRenderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            Bounds bounds = renderer.bounds;
            Vector3 centre = bounds.center;
            Vector3 extents = bounds.extents;

            // Проекция коробки на нормаль плоскости: половина её протяжённости
            // вдоль нормали равна сумме модулей проекций полуосей.
            Vector3 normal = portal.transform.forward;
            float reach = Mathf.Abs(normal.x) * extents.x
                + Mathf.Abs(normal.y) * extents.y
                + Mathf.Abs(normal.z) * extents.z;

            float distance = PortalMath.SignedDistance(portal.transform, centre);
            minimum = Mathf.Min(minimum, distance - reach);
            maximum = Mathf.Max(maximum, distance + reach);
        }

        return minimum < 0f && maximum > 0f;
    }

    /// <summary>
    /// Показывает двойника и приводит его в соответствие с оригиналом. Вызывается
    /// каждый кадр, пока путешественник на плоскости.
    /// </summary>
    public void Show(Portal entrance)
    {
        if (entrance == null || entrance.exitPortal == null || _source == null)
        {
            Hide();
            return;
        }

        EnsureClone();
        if (_clone == null)
        {
            return;
        }

        Matrix4x4 transformMatrix = PortalMath.EntranceToExit(
            entrance.transform, entrance.exitPortal.transform);
        Matrix4x4 pose = transformMatrix * _source.localToWorldMatrix;

        _clone.transform.SetPositionAndRotation(pose.GetColumn(3), pose.rotation);
        _clone.transform.localScale = _source.lossyScale;

        // Резать можно только материал, который про плоскость знает. Если не
        // знает, решение за полем cloneFallback: показать двойника целиком или
        // не показывать вовсе.
        bool draw = _sliceable || entrance.cloneFallback == CloneFallback.DrawUnsliced;
        SetRenderersEnabled(_cloneRenderers, draw);

        bool slice = _sliceable;

        // Оригинал держит непрошедшую часть, двойник — прошедшую. Нормаль
        // указывает на ту половину, которая остаётся.
        ApplySlice(
            _sourceRenderers,
            entrance.transform.position,
            entrance.transform.forward,
            slice);
        ApplySlice(
            _cloneRenderers,
            entrance.exitPortal.transform.position,
            entrance.exitPortal.transform.forward,
            slice);

        _visible = true;
    }

    /// <summary>Убирает двойника и снимает резку с оригинала.</summary>
    public void Hide()
    {
        if (!_visible)
        {
            return;
        }

        _visible = false;

        SetRenderersEnabled(_cloneRenderers, false);
        ApplySlice(_sourceRenderers, Vector3.zero, Vector3.up, false);
    }

    /// <summary>Уничтожает двойника совсем. Вызывается при выключении путешественника.</summary>
    public void Dispose()
    {
        Hide();

        if (_clone != null)
        {
            Object.Destroy(_clone);
            _clone = null;
        }

        _cloneRenderers.Clear();
    }

    /// <summary>
    /// Собирает двойника из одних рендереров, а не копирует объект целиком.
    ///
    /// Копирование через Instantiate выглядит проще, но копия получает и камеру,
    /// и контроллер, и сам PortalTraveller, и их Awake отрабатывает сразу. Снять
    /// их потом можно только отложенным уничтожением, то есть кадр в сцене живут
    /// второй играющий объект и вторая камера. Поэтому берётся только видимая
    /// часть, и брать нечего, кроме неё.
    /// </summary>
    private void EnsureClone()
    {
        if (_clone != null)
        {
            return;
        }

        _clone = new GameObject(_source.name + " (portal clone)");
        Matrix4x4 sourceToLocal = _source.worldToLocalMatrix;

        for (int i = 0; i < _sourceRenderers.Count; i++)
        {
            Renderer source = _sourceRenderers[i];
            if (source == null || !(source is MeshRenderer))
            {
                continue;
            }

            if (!source.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
            {
                continue;
            }

            var part = new GameObject(source.name);
            part.transform.SetParent(_clone.transform, false);

            // Поза части относительно корня оригинала, чтобы двойник повторял
            // его строение, а не собирался в одну точку.
            Matrix4x4 local = sourceToLocal * source.transform.localToWorldMatrix;
            part.transform.localPosition = local.GetColumn(3);
            part.transform.localRotation = local.rotation;
            part.transform.localScale = local.lossyScale;

            part.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;

            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = source.sharedMaterials;
            renderer.shadowCastingMode = source.shadowCastingMode;
            renderer.receiveShadows = source.receiveShadows;
            renderer.enabled = false;

            _cloneRenderers.Add(renderer);
        }

        _sliceable = AllMaterialsSupportSlicing(_cloneRenderers);
    }

    /// <summary>
    /// Материал считается пригодным к резке, если у него есть свойство
    /// _SliceEnabled. Контракт свойств описан в SETUP.md; шейдер, который про
    /// них не знает, просто их игнорирует, и резка молча не работает — поэтому
    /// пригодность проверяется явно, а не по факту.
    /// </summary>
    private static bool AllMaterialsSupportSlicing(List<Renderer> renderers)
    {
        if (renderers.Count == 0)
        {
            return false;
        }

        for (int i = 0; i < renderers.Count; i++)
        {
            Material[] materials = renderers[i].sharedMaterials;
            for (int m = 0; m < materials.Length; m++)
            {
                if (materials[m] == null || !materials[m].HasProperty(SliceEnabledId))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void SetRenderersEnabled(List<Renderer> renderers, bool value)
    {
        for (int i = 0; i < renderers.Count; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = value;
            }
        }
    }

    /// <summary>
    /// Кладёт параметры плоскости резки в блок свойств. Материалы, которые резку
    /// не поддерживают, эти свойства просто игнорируют, поэтому вызов безопасен
    /// для любого рендерера — видимый результат зависит от того, умеет ли шейдер
    /// отбрасывать фрагменты по плоскости.
    /// </summary>
    private void ApplySlice(List<Renderer> renderers, Vector3 centre, Vector3 normal, bool enabled)
    {
        _block ??= new MaterialPropertyBlock();

        for (int i = 0; i < renderers.Count; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.GetPropertyBlock(_block);
            _block.SetVector(SliceCentreId, centre);
            _block.SetVector(SliceNormalId, normal);
            _block.SetFloat(SliceEnabledId, enabled ? 1f : 0f);
            renderer.SetPropertyBlock(_block);
        }
    }
}
