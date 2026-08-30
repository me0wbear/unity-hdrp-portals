using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

public sealed class PortalCrossingTests
{
    private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    private readonly List<GameObject> _objects = new List<GameObject>();
    private List<Portal> _active;
    private Portal _entrance;
    private Portal _exit;
    private PortalTraveller _traveller;
    private int _teleports;

    private GameObject Create(string name)
    {
        var value = new GameObject(name);
        _objects.Add(value);
        return value;
    }

    [SetUp]
    public void SetUp()
    {
        _entrance = Create("Entrance").AddComponent<Portal>();
        _exit = Create("Exit").AddComponent<Portal>();
        _exit.transform.position = new Vector3(30f, 0f, 0f);
        _entrance.exitPortal = _exit;
        _exit.exitPortal = _entrance;
        // В EditMode жизненный цикл не запускается: наполняем реестр без
        // создания постоянного носителя системы и виртуальных камер.
        _active = (List<Portal>)typeof(PortalSystem)
            .GetField("Portals", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        _active.Add(_entrance);
        _active.Add(_exit);
        _traveller = Create("Traveller").AddComponent<PortalTraveller>();
        typeof(PortalTraveller).GetField("drawClone", Private).SetValue(_traveller, false);
        _teleports = 0;
        _traveller.Teleported += _ => _teleports++;
    }

    private static void Invoke(object target, string method)
    {
        target.GetType().GetMethod(method, Private).Invoke(target, null);
    }

    private void Tick(float x, float z)
    {
        _traveller.transform.position = new Vector3(x, 0f, z);
        Invoke(_traveller, "LateUpdate");
    }

    [TearDown]
    public void TearDown()
    {
        _active.Remove(_entrance);
        _active.Remove(_exit);
        foreach (GameObject value in _objects)
        {
            if (value != null) Object.DestroyImmediate(value);
        }
        _objects.Clear();
    }

    [TestCase(0.1f, -0.1f)]
    [TestCase(0.1f, -3.1f)]
    [TestCase(3.1f, -3.1f)]
    public void Crossing_CentreSegment_TeleportsRegardlessOfEndpointRange(float start, float end)
    {
        Tick(0f, start);
        Tick(0f, end);
        Assert.AreEqual(1, _teleports);
    }

    [TestCase(2f, 0f, 0)]
    [TestCase(0f, 1.2f, 1)]
    public void Crossing_UsesPlaneIntersectionRatherThanEndpoint(float startX, float endX, int expected)
    {
        // Полуширина с запасом равна 0,85. Пересечения: x=1 и x=0,6.
        Tick(startX, 1f);
        Tick(endX, -1f);
        Assert.AreEqual(expected, _teleports);
    }

    [Test]
    public void Crossing_FirstObservationBehindPortal_DoesNotTeleport()
    {
        Tick(0f, -0.1f);
        Assert.AreEqual(0, _teleports);
    }

    [Test]
    public void Crossing_MultipleOpenings_SelectsFirstAlongSegment()
    {
        Portal farther = Create("Farther Entrance").AddComponent<Portal>();
        farther.transform.position = new Vector3(0f, 0f, -2f);
        farther.exitPortal = _exit;
        _active.Insert(0, farther);
        Portal chosen = null;
        _traveller.Teleported += context => chosen = context.Entrance;
        try
        {
            Tick(0f, 2f);
            Tick(0f, -2.5f);
            Assert.AreEqual(1, _teleports);
            Assert.AreSame(_entrance, chosen, "Ближайшее пересечение не зависит от порядка регистрации.");
        }
        finally
        {
            _active.Remove(farther);
        }
    }

    [Test]
    public void Crossing_ResetTracking_DoesNotUsePreviousPosition()
    {
        Tick(0f, 1f);
        _traveller.ResetPortalTracking();
        Tick(0f, -1f);
        Assert.AreEqual(0, _teleports);
    }

    [Test]
    public void Crossing_PortalAbsentForAnUpdate_DiscardsItsSample()
    {
        Tick(0f, 0.1f);
        _active.Remove(_entrance);
        Tick(0f, -0.1f);
        Assert.IsFalse(_traveller.TryGetTrackedDistance(_entrance, out _));
        _active.Add(_entrance);
        Tick(0f, -0.1f);
        Assert.AreEqual(0, _teleports);
    }

    [Test]
    public void Crossing_PortalReenabledBetweenUpdates_DiscardsItsSample()
    {
        Tick(0f, 0.1f);
        Invoke(_entrance, "OnDisable");
        // Register не создаёт систему для уже внесённого портала.
        _active.Add(_entrance);
        Invoke(_entrance, "OnEnable");
        Tick(0f, -0.1f);
        Assert.AreEqual(0, _teleports);
    }

    [Test]
    public void Crossing_DisabledTraveller_DiscardsItsSample()
    {
        Tick(0f, 0.1f);
        Invoke(_traveller, "OnDisable");
        Tick(0f, -0.1f);
        Assert.AreEqual(0, _teleports);
    }
}
