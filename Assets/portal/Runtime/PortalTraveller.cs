using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Вешается на корень того, что должно проходить сквозь порталы: на игрока с
/// CharacterController или на объект с Rigidbody.
///
/// Переход засчитывается по точке взгляда, а не по корню: игрок должен
/// переноситься ровно в тот момент, когда плоскость пересекает глаз. Перенеси
/// его раньше или позже — и кадр после перехода не совпадёт с кадром до него,
/// то есть переход станет заметен.
/// </summary>
[DefaultExecutionOrder(900)]
public sealed class PortalTraveller : MonoBehaviour
{
    [SerializeField]
    [Tooltip("Точка взгляда. Обычно Transform камеры. Пусто — берётся сам объект.")]
    private Transform viewPoint;

    [SerializeField]
    [Tooltip("Запас, на который проём считается шире при проверке попадания. "
        + "Нужен потому, что у путешественника есть толщина.")]
    private float openingMargin = 0.35f;

    [SerializeField]
    [Tooltip("На каком расстоянии от плоскости портала начинать следить за ним.")]
    [Min(0.1f)] private float trackingRange = 3f;

    [SerializeField]
    [Tooltip("Показывать двойника по ту сторону, пока путешественник пересекает "
        + "плоскость. Нужен всем, у кого есть видимая геометрия.")]
    private bool drawClone = true;

    /// <summary>Поднимается после того, как перенос уже применён.</summary>
    public event Action<PortalTeleportContext> Teleported;

    private readonly Dictionary<Portal, float> _distances = new Dictionary<Portal, float>();

    private CharacterController _controller;
    private Rigidbody _body;
    private bool _componentsResolved;
    private PortalClone _clone;

    /// <summary>
    /// Точка, по которой засчитывается пересечение. Именно взгляд, а не корень.
    /// </summary>
    public Transform ViewPoint => viewPoint != null ? viewPoint : transform;

    /// <summary>Запас, на который проём считается шире при проверке попадания.</summary>
    public float OpeningMargin => openingMargin;

    /// <summary>
    /// Смена знака расстояния с положительного на отрицательный внутри проёма.
    /// Первое наблюдение переходом быть не может: предыдущего расстояния нет.
    /// Обратное направление тоже не считается, иначе вышедший из портала
    /// затягивался бы в него обратно.
    /// </summary>
    public static bool ShouldCross(float previousDistance, float currentDistance, bool insideOpening)
    {
        if (float.IsNaN(previousDistance) || !insideOpening)
        {
            return false;
        }

        return previousDistance > 0f && currentDistance <= 0f;
    }

    /// <summary>
    /// Забывает запомненные расстояния. Обязательно вызывать после того, как
    /// объект переставили вручную: иначе перестановка читается как пересечение
    /// и объект улетает в парный портал.
    /// </summary>
    public void ResetPortalTracking()
    {
        _distances.Clear();
    }

    /// <summary>Запомненное расстояние до портала, если за ним следят.</summary>
    public bool TryGetTrackedDistance(Portal portal, out float distance)
    {
        return _distances.TryGetValue(portal, out distance);
    }

    /// <summary>Записывает расстояние до портала напрямую. Нужно тестам и отладке.</summary>
    public void TrackDistance(Portal portal, float distance)
    {
        if (portal != null)
        {
            _distances[portal] = distance;
        }
    }

    /// <summary>
    /// Переносит путешественника сквозь <paramref name="entrance"/> и поднимает
    /// событие. Вызывается детектом, но доступен и снаружи: перенос по сюжету
    /// должен проходить тем же путём, что и обычный, иначе подписчики не узнают
    /// о нём и вид разъедется.
    /// </summary>
    public void Teleport(Portal entrance)
    {
        if (entrance == null || entrance.exitPortal == null)
        {
            return;
        }

        ResolveComponents();

        Portal exit = entrance.exitPortal;
        Matrix4x4 transformMatrix = PortalMath.EntranceToExit(entrance.transform, exit.transform);

        Matrix4x4 pose = transformMatrix * transform.localToWorldMatrix;
        Vector3 position = pose.GetColumn(3);
        Quaternion rotation = pose.rotation;

        // Контроллер владеет положением сам и вернёт объект назад, если писать
        // в трансформ при включённом контроллере.
        bool controllerWasEnabled = _controller != null && _controller.enabled;
        if (controllerWasEnabled)
        {
            _controller.enabled = false;
        }

        transform.SetPositionAndRotation(position, rotation);

        if (controllerWasEnabled)
        {
            _controller.enabled = true;
        }

        if (_body != null)
        {
            _body.linearVelocity = transformMatrix.MultiplyVector(_body.linearVelocity);
            _body.angularVelocity = transformMatrix.MultiplyVector(_body.angularVelocity);
        }

        _clone?.Hide();

        // Расстояния считаны для старой позы и после переноса бессмысленны.
        // Особенно до портала выхода: он теперь прямо за спиной, и сохранённый
        // знак прочитался бы как ещё одно пересечение.
        ResetPortalTracking();

        Teleported?.Invoke(new PortalTeleportContext(entrance, exit, transformMatrix, this));
    }

    /// <summary>
    /// Контроллер и тело ищутся при первом обращении, а не в Awake. Так перенос
    /// работает и когда тело добавили после травеллера, и в edit-mode тестах,
    /// где Awake не вызывается вовсе.
    /// </summary>
    private void ResolveComponents()
    {
        if (_componentsResolved)
        {
            return;
        }

        _componentsResolved = true;
        _controller = GetComponent<CharacterController>();
        _body = GetComponent<Rigidbody>();
    }

    /// <summary>
    /// Расстояния считаются до всех включённых порталов напрямую, а не по
    /// событиям триггера. Триггеры молчат, когда CharacterController выключен, а
    /// выключают его и замеры лаборатории, и любой код, переставляющий игрока
    /// вручную — как раз тогда, когда переход и должен отработать.
    /// </summary>
    private void LateUpdate()
    {
        Vector3 eye = ViewPoint.position;
        IReadOnlyList<Portal> portals = PortalSystem.Active;

        Portal straddled = null;

        for (int i = 0; i < portals.Count; i++)
        {
            Portal portal = portals[i];
            if (portal == null || portal.exitPortal == null)
            {
                continue;
            }

            float current = PortalMath.SignedDistance(portal.transform, eye);

            if (Mathf.Abs(current) > trackingRange)
            {
                _distances.Remove(portal);
                continue;
            }

            bool inside = PortalMath.IsInsideOpening(
                portal.transform, eye, portal.OpeningSize, openingMargin);

            float previous = TryGetTrackedDistance(portal, out float tracked) ? tracked : float.NaN;

            if (ShouldCross(previous, current, inside))
            {
                Teleport(portal);
                return;
            }

            _distances[portal] = current;

            if (straddled == null && inside && Clone != null && Clone.StraddlesPlane(portal))
            {
                straddled = portal;
            }
        }

        UpdateClone(straddled);
    }

    /// <summary>
    /// Двойник создаётся при первом обращении и только если у путешественника
    /// есть что показывать. У игрока без видимой геометрии его не будет вовсе.
    /// </summary>
    private PortalClone Clone
    {
        get
        {
            if (!drawClone)
            {
                return null;
            }

            _clone ??= new PortalClone(transform);
            return _clone;
        }
    }

    private void UpdateClone(Portal straddled)
    {
        if (_clone == null)
        {
            return;
        }

        if (straddled != null)
        {
            _clone.Show(straddled);
        }
        else
        {
            _clone.Hide();
        }
    }

    private void OnDisable()
    {
        _clone?.Dispose();
        _clone = null;
    }
}
