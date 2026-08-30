using NUnit.Framework;
using UnityEngine;

public sealed class PortalCloneTests
{
    private GameObject _traveller;
    private GameObject _portalObject;
    private Portal _portal;

    [SetUp]
    public void SetUp()
    {
        // Куб со стороной 1: половина протяжённости вдоль любой оси — 0,5.
        _traveller = GameObject.CreatePrimitive(PrimitiveType.Cube);
        _traveller.name = "Traveller";

        _portalObject = new GameObject("Portal");
        _portalObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _portal = _portalObject.AddComponent<Portal>();
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_traveller);
        Object.DestroyImmediate(_portalObject);
    }

    /// <summary>
    /// Плоскость проходит сквозь тело: половина уже прошла, половина ещё нет.
    /// Ровно тот случай, ради которого двойник и существует.
    /// </summary>
    [Test]
    public void StraddlesPlane_IsTrueWhenTheBodyCrossesThePlane()
    {
        _traveller.transform.position = Vector3.zero;

        var clone = new PortalClone(_traveller.transform);

        Assert.IsTrue(clone.StraddlesPlane(_portal));
    }

    /// <summary>Тело целиком перед плоскостью — двойник не нужен.</summary>
    [Test]
    public void StraddlesPlane_IsFalseWhenTheBodyIsFullyInFront()
    {
        _traveller.transform.position = new Vector3(0f, 0f, 2f);

        var clone = new PortalClone(_traveller.transform);

        Assert.IsFalse(clone.StraddlesPlane(_portal));
    }

    /// <summary>Тело целиком позади плоскости — двойник тоже не нужен.</summary>
    [Test]
    public void StraddlesPlane_IsFalseWhenTheBodyIsFullyBehind()
    {
        _traveller.transform.position = new Vector3(0f, 0f, -2f);

        var clone = new PortalClone(_traveller.transform);

        Assert.IsFalse(clone.StraddlesPlane(_portal));
    }

    /// <summary>
    /// Тело в полуметре от плоскости краем её касается: куб со стороной 1
    /// достаёт ровно до неё. Проверка идёт по протяжённости тела, а не по его
    /// центру, иначе двойник появлялся бы уже после того, как объект наполовину
    /// прошёл сквозь стену.
    /// </summary>
    [Test]
    public void StraddlesPlane_UsesBodyExtentNotCentre()
    {
        _traveller.transform.position = new Vector3(0f, 0f, 0.4f);

        var clone = new PortalClone(_traveller.transform);

        Assert.IsTrue(clone.StraddlesPlane(_portal),
            "центр в 0,4 метра, но тело достаёт до плоскости");
    }

    /// <summary>
    /// Протяжённость считается вдоль нормали портала, а не вдоль мировых осей.
    /// Повёрнутый портал должен видеть то же самое тело так же.
    /// </summary>
    [Test]
    public void StraddlesPlane_FollowsThePortalRotation()
    {
        _portalObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        _traveller.transform.position = new Vector3(0.4f, 0f, 0f);

        var clone = new PortalClone(_traveller.transform);

        Assert.IsTrue(clone.StraddlesPlane(_portal));

        _traveller.transform.position = new Vector3(2f, 0f, 0f);
        Assert.IsFalse(clone.StraddlesPlane(_portal));
    }

    /// <summary>
    /// У путешественника без видимой геометрии двойника быть не может: показывать
    /// нечего, и создавать копию незачем.
    /// </summary>
    [Test]
    public void StraddlesPlane_IsFalseWithoutAnyRenderer()
    {
        var empty = new GameObject("Empty");
        try
        {
            var clone = new PortalClone(empty.transform);
            Assert.IsFalse(clone.StraddlesPlane(_portal));
        }
        finally
        {
            Object.DestroyImmediate(empty);
        }
    }
}
