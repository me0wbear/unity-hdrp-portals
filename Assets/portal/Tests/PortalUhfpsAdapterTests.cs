using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Проверяет мост к UHFPS на типах-заглушках.
///
/// Тип берётся той же рефлексией, какой его ищет сам мост, а не ссылкой на
/// сборку. Объявить свои копии заглушек здесь нельзя: два типа с одним полным
/// именем в разных сборках компилируются, но поиск по имени начинает возвращать
/// то один, то другой, и тест ловил бы не то, что ловит мост.
///
/// Если заглушек в проекте нет, тесты пропускаются: модуль должен собираться и
/// работать там, где про UHFPS никто не слышал.
/// </summary>
public sealed class PortalUhfpsAdapterTests
{
    private const string LookControllerTypeName = "UHFPS.Runtime.LookController";
    private const string StateMachineTypeName = "UHFPS.Runtime.PlayerStateMachine";

    private GameObject _owner;

    [SetUp]
    public void SetUp()
    {
        _owner = new GameObject("UhfpsOwner");
    }

    [TearDown]
    public void TearDown()
    {
        if (_owner != null)
        {
            UnityEngine.Object.DestroyImmediate(_owner);
        }
    }

    [Test]
    public void Apply_AddsYawToStoredLook()
    {
        Component look = AddStub(LookControllerTypeName);
        SetField(look, "LookRotation", new Vector2(10f, 5f));

        new PortalUhfpsAdapter(_owner).Apply(Context(90f));

        var stored = (Vector2)GetField(look, "LookRotation");
        Assert.AreEqual(100f, stored.x, 0.001f);
    }

    /// <summary>
    /// Поворот на 270 градусов и на минус 90 — один и тот же поворот, но
    /// складывать с накопленным углом нужно знаковую дельту. Без приведения
    /// сохранённое рыскание уезжает на 360 градусов в другую сторону, и игрок
    /// после перехода смотрит не туда.
    /// </summary>
    [Test]
    public void Apply_UsesSignedYawDelta()
    {
        Component look = AddStub(LookControllerTypeName);
        SetField(look, "LookRotation", new Vector2(0f, 0f));

        new PortalUhfpsAdapter(_owner).Apply(Context(270f));

        var stored = (Vector2)GetField(look, "LookRotation");
        Assert.AreEqual(-90f, stored.x, 0.001f);
    }

    /// <summary>Переход поворачивает вокруг вертикали, тангаж он не меняет.</summary>
    [Test]
    public void Apply_LeavesPitchAlone()
    {
        Component look = AddStub(LookControllerTypeName);
        SetField(look, "LookRotation", new Vector2(10f, 33f));

        new PortalUhfpsAdapter(_owner).Apply(Context(90f));

        var stored = (Vector2)GetField(look, "LookRotation");
        Assert.AreEqual(33f, stored.y, 0.001f);
    }

    /// <summary>
    /// Motion хранится в мировых координатах, поэтому его поворачивает та же
    /// матрица перехода. Иначе игрок продолжает идти в прежнюю мировую сторону.
    /// </summary>
    [Test]
    public void Apply_RotatesStoredMotionIntoTheNewFrame()
    {
        Component machine = AddStub(StateMachineTypeName);
        SetField(machine, "Motion", new Vector3(0f, 0f, 5f));

        new PortalUhfpsAdapter(_owner).Apply(Context(90f));

        var stored = (Vector3)GetField(machine, "Motion");
        Assert.AreEqual(5f, stored.x, 0.001f);
        Assert.AreEqual(0f, stored.z, 0.001f);
    }

    [Test]
    public void Apply_OnObjectWithoutControllers_DoesNothing()
    {
        var adapter = new PortalUhfpsAdapter(_owner);

        Assert.IsFalse(adapter.IsAvailable);
        Assert.DoesNotThrow(() => adapter.Apply(Context(90f)));
    }

    /// <summary>
    /// Половина контроллеров тоже считается: у UHFPS они лежат на разных
    /// объектах, и сборка, где есть только один из двух, обязана работать.
    /// </summary>
    [Test]
    public void Available_WhenOnlyOneControllerIsPresent()
    {
        AddStub(StateMachineTypeName);

        Assert.IsTrue(new PortalUhfpsAdapter(_owner).IsAvailable);
    }

    private static PortalTeleportContext Context(float yaw)
    {
        Matrix4x4 transform = Matrix4x4.Rotate(Quaternion.Euler(0f, yaw, 0f));
        return new PortalTeleportContext(null, null, transform, null);
    }

    private Component AddStub(string typeName)
    {
        Type type = FindType(typeName);
        if (type == null)
        {
            Assert.Ignore(typeName + " not in the project, nothing to exercise the bridge against");
        }

        return _owner.AddComponent(type);
    }

    private static void SetField(Component target, string name, object value)
    {
        target.GetType()
            .GetField(name, BindingFlags.Public | BindingFlags.Instance)
            .SetValue(target, value);
    }

    private static object GetField(Component target, string name)
    {
        return target.GetType()
            .GetField(name, BindingFlags.Public | BindingFlags.Instance)
            .GetValue(target);
    }

    private static Type FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType(fullName, false);
            if (type != null)
            {
                return type;
            }
        }

        return null;
    }
}
