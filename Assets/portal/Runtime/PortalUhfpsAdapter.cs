using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Как согласовать сохранённый угол взгляда UHFPS с поворотом перехода.
///
/// Документация UHFPS требует, чтобы поворот корня игрока всегда был нулевым:
/// направление живёт в look rotation камеры, а ненулевой поворот корня ассет
/// переносит туда сам. Портал же поворачивает именно корень. Как это сочетается
/// с конкретной версией живого ассета, заранее знать нельзя, поэтому режим
/// открыт настройкой; лог адаптера показывает, что он нашёл и что сделал.
/// </summary>
public enum UhfpsLookMode
{
    /// <summary>
    /// Добавить поворот перехода к сохранённому рысканию, корень не трогать.
    /// Подходит контроллеру, который каждый кадр переписывает поворот корня из
    /// сохранённого угла: без добавки вид вернётся в прежнюю мировую сторону.
    /// </summary>
    AddYawDelta,

    /// <summary>
    /// Перенести итоговое рыскание корня в сохранённый угол и обнулить рыскание
    /// корня. Повторяет инвариант документации UHFPS: поворот игрока всегда
    /// нулевой, направление живёт в look rotation. Подходит, если ассет сам
    /// переносит ненулевой поворот корня и с режимом добавки поворот удваивается.
    /// </summary>
    TransferRootYaw,

    /// <summary>
    /// Не трогать сохранённый угол вовсе. Для разбора: если после перехода вид
    /// правильный уже в этом режиме, поворот согласует сам ассет.
    /// </summary>
    DoNotTouch
}

/// <summary>
/// Поворачивает состояние контроллера UHFPS на переходе.
///
/// Модуль на UHFPS не ссылается: результат может использоваться в проекте, где
/// этого ассета нет, и жёсткая ссылка сломала бы там компиляцию. Поэтому доступ
/// идёт рефлексией по именам типов, а сами типы и члены разбираются один раз при
/// создании — в момент перехода выполняется только чтение и запись. Члены ищутся
/// и как поля, и как свойства: версии ассета отличаются.
///
/// Ограничение: мост проверен на заглушках, повторяющих имена, пространство имён
/// и сигнатуры настоящих классов. На живом ассете он не подтверждён, поэтому
/// найденное и ненайденное пишется в лог: молчаливый холостой ход выглядел бы
/// как разворот камеры в прежнюю мировую сторону сразу после перехода.
/// </summary>
public sealed class PortalUhfpsAdapter
{
    private const string LookControllerTypeName = "UHFPS.Runtime.LookController";
    private const string StateMachineTypeName = "UHFPS.Runtime.PlayerStateMachine";
    private const string LookRotationMemberName = "LookRotation";
    private const string MotionMemberName = "Motion";

    private readonly Component _lookController;
    private readonly Component _stateMachine;
    private readonly MemberAccessor _lookRotation;
    private readonly MemberAccessor _motion;

    public PortalUhfpsAdapter(GameObject owner)
    {
        if (owner == null)
        {
            return;
        }

        Type lookType = FindType(LookControllerTypeName);
        if (lookType != null)
        {
            _lookController = owner.GetComponentInChildren(lookType, true);
            _lookRotation = MemberAccessor.Find(lookType, LookRotationMemberName, typeof(Vector2));
            ReportBinding(owner, lookType, _lookController, _lookRotation, LookRotationMemberName);
        }

        Type machineType = FindType(StateMachineTypeName);
        if (machineType != null)
        {
            _stateMachine = owner.GetComponentInChildren(machineType, true);
            _motion = MemberAccessor.Find(machineType, MotionMemberName, typeof(Vector3));
            ReportBinding(owner, machineType, _stateMachine, _motion, MotionMemberName);
        }
    }

    /// <summary>Нашёлся ли на объекте хоть один контроллер UHFPS.</summary>
    public bool IsAvailable =>
        (_lookController != null && _lookRotation != null)
        || (_stateMachine != null && _motion != null);

    public void Apply(PortalTeleportContext context, UhfpsLookMode mode, Transform root)
    {
        RotateStoredLook(context.Rotation, mode, root);
        RotateStoredMotion(context.Transform);
    }

    /// <summary>
    /// UHFPS переписывает трансформы из сохранённого угла каждый кадр, поэтому
    /// поворот корня без правки этого угла съедается на следующем же кадре.
    /// Правится только рыскание: переход поворачивает вокруг вертикали, тангаж
    /// он не меняет.
    /// </summary>
    private void RotateStoredLook(Quaternion rotation, UhfpsLookMode mode, Transform root)
    {
        if (_lookController == null || _lookRotation == null || mode == UhfpsLookMode.DoNotTouch)
        {
            return;
        }

        var stored = (Vector2)_lookRotation.Get(_lookController);

        if (mode == UhfpsLookMode.AddYawDelta)
        {
            float yawDelta = NormalizeAngle(rotation.eulerAngles.y);
            _lookRotation.Set(_lookController, new Vector2(stored.x + yawDelta, stored.y));
            return;
        }

        // TransferRootYaw: итоговое рыскание корня после перехода целиком
        // уходит в сохранённый угол, а корень возвращается к нулевому рысканию —
        // инвариант UHFPS. Тангаж и крен корня не трогаются: у прямоходящего
        // игрока они нулевые, а чужие значения переписывать не наше дело.
        if (root != null)
        {
            Vector3 euler = root.eulerAngles;
            float rootYaw = NormalizeAngle(euler.y);
            _lookRotation.Set(_lookController, new Vector2(stored.x + rootYaw, stored.y));
            root.rotation = Quaternion.Euler(euler.x, 0f, euler.z);
        }
    }

    /// <summary>
    /// Motion хранится в мировых координатах. Без поворота игрок после перехода
    /// через повёрнутый портал продолжает идти в прежнюю мировую сторону и
    /// сходит с оси движения.
    /// </summary>
    private void RotateStoredMotion(Matrix4x4 transformMatrix)
    {
        if (_stateMachine == null || _motion == null)
        {
            return;
        }

        var stored = (Vector3)_motion.Get(_stateMachine);
        _motion.Set(_stateMachine, transformMatrix.MultiplyVector(stored));
    }

    /// <summary>
    /// Приводит угол к диапазону от минус 180 до 180. Складывать с сохранённым
    /// рысканием нужно именно знаковую дельту: 270 градусов и минус 90 — один и
    /// тот же поворот, но первое уводит накопленный угол в другую сторону.
    /// </summary>
    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        if (degrees > 180f)
        {
            return degrees - 360f;
        }

        return degrees < -180f ? degrees + 360f : degrees;
    }

    /// <summary>
    /// Пишет в лог, что именно связалось. Тип найден, а компонент или член нет —
    /// предупреждение: адаптер в этом состоянии ничего не делает, и снаружи это
    /// выглядит как разворот камеры в прежнюю сторону сразу после перехода.
    /// </summary>
    private static void ReportBinding(
        GameObject owner, Type type, Component component, MemberAccessor member, string memberName)
    {
        if (component == null)
        {
            Debug.LogWarning("[Portal] UHFPS type " + type.Name + " is present in the project, "
                + "but no component of it was found under " + owner.name
                + ": the portal bridge will not adjust it on a crossing");
            return;
        }

        if (member == null)
        {
            Debug.LogWarning("[Portal] UHFPS component " + type.Name + " was found, but member "
                + memberName + " was not: this UHFPS version stores its state differently "
                + "and the portal bridge cannot adjust it");
            return;
        }

        Debug.Log("[Portal] UHFPS binding: " + type.Name + "." + memberName + " ("
            + (member.IsProperty ? "property" : "field") + ") will be adjusted on crossings");
    }

    private static Type FindType(string fullName)
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            Type type = assemblies[i].GetType(fullName, false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Поле или свойство с одним именем. В разных версиях ассета один и тот же
    /// член бывает и тем, и другим; вызывающему без разницы.
    /// </summary>
    private sealed class MemberAccessor
    {
        private readonly FieldInfo _field;
        private readonly PropertyInfo _property;

        private MemberAccessor(FieldInfo field, PropertyInfo property)
        {
            _field = field;
            _property = property;
        }

        public bool IsProperty => _property != null;

        public static MemberAccessor Find(Type type, string name, Type expected)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType == expected)
            {
                return new MemberAccessor(field, null);
            }

            PropertyInfo property = type.GetProperty(
                name, BindingFlags.Public | BindingFlags.Instance);
            if (property != null
                && property.PropertyType == expected
                && property.CanRead
                && property.CanWrite)
            {
                return new MemberAccessor(null, property);
            }

            return null;
        }

        public object Get(Component target)
        {
            return _field != null ? _field.GetValue(target) : _property.GetValue(target);
        }

        public void Set(Component target, object value)
        {
            if (_field != null)
            {
                _field.SetValue(target, value);
            }
            else
            {
                _property.SetValue(target, value);
            }
        }
    }
}
