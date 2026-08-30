using System;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Поворачивает состояние контроллера UHFPS на переходе.
///
/// Модуль на UHFPS не ссылается: результат может использоваться в проекте, где
/// этого ассета нет, и жёсткая ссылка сломала бы там компиляцию. Поэтому доступ
/// идёт рефлексией по именам типов, а сами типы и поля разбираются один раз при
/// создании — в момент перехода выполняется только чтение и запись.
///
/// Ограничение: мост проверен на заглушках, повторяющих имена, пространство имён
/// и сигнатуры настоящих классов. На живом ассете он не подтверждён.
/// </summary>
public sealed class PortalUhfpsAdapter
{
    private const string LookControllerTypeName = "UHFPS.Runtime.LookController";
    private const string StateMachineTypeName = "UHFPS.Runtime.PlayerStateMachine";

    private readonly Component _lookController;
    private readonly Component _stateMachine;
    private readonly FieldInfo _lookRotationField;
    private readonly FieldInfo _motionField;

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
            _lookRotationField = lookType.GetField(
                "LookRotation", BindingFlags.Public | BindingFlags.Instance);
        }

        Type machineType = FindType(StateMachineTypeName);
        if (machineType != null)
        {
            _stateMachine = owner.GetComponentInChildren(machineType, true);
            _motionField = machineType.GetField(
                "Motion", BindingFlags.Public | BindingFlags.Instance);
        }
    }

    /// <summary>Нашёлся ли на объекте хоть один контроллер UHFPS.</summary>
    public bool IsAvailable =>
        (_lookController != null && _lookRotationField != null)
        || (_stateMachine != null && _motionField != null);

    public void Apply(PortalTeleportContext context)
    {
        RotateStoredLook(context.Rotation);
        RotateStoredMotion(context.Transform);
    }

    /// <summary>
    /// UHFPS переписывает трансформы из сохранённого угла каждый кадр, поэтому
    /// поворот корня без правки этого поля съедается на следующем же кадре.
    /// Правится только рыскание: переход поворачивает вокруг вертикали, тангаж
    /// он не меняет.
    /// </summary>
    private void RotateStoredLook(Quaternion rotation)
    {
        if (_lookController == null || _lookRotationField == null)
        {
            return;
        }

        var stored = (Vector2)_lookRotationField.GetValue(_lookController);
        float yawDelta = NormalizeAngle(rotation.eulerAngles.y);

        _lookRotationField.SetValue(
            _lookController, new Vector2(stored.x + yawDelta, stored.y));
    }

    /// <summary>
    /// Motion хранится в мировых координатах. Без поворота игрок после перехода
    /// через повёрнутый портал продолжает идти в прежнюю мировую сторону и
    /// сходит с оси движения.
    /// </summary>
    private void RotateStoredMotion(Matrix4x4 transformMatrix)
    {
        if (_stateMachine == null || _motionField == null)
        {
            return;
        }

        var stored = (Vector3)_motionField.GetValue(_stateMachine);
        _motionField.SetValue(_stateMachine, transformMatrix.MultiplyVector(stored));
    }

    /// <summary>
    /// Приводит угол к диапазону от минус 180 до 180. Складывать с сохранённым
    /// рысканием нужно именно знаковую дельту: 270 градусов и минус 90 — один и
    /// тот же поворот, но первое уводит накопленный угол в другую сторону.
    /// </summary>
    private static float NormalizeAngle(float degrees)
    {
        degrees %= 360f;
        return degrees > 180f ? degrees - 360f : degrees;
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
}
