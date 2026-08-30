using NUnit.Framework;
using UnityEngine;

public sealed class PortalTravellerTests
{
    [Test]
    public void ShouldCross_WhenSignFlipsFromFrontToBackInsideOpening()
    {
        Assert.IsTrue(PortalTraveller.ShouldCross(0.2f, -0.1f, true));
    }

    [Test]
    public void ShouldCross_IsFalseWhenSignFlipsOutsideTheOpening()
    {
        Assert.IsFalse(PortalTraveller.ShouldCross(0.2f, -0.1f, false));
    }

    /// <summary>
    /// Выход обратно через изнанку переходом не считается: иначе игрок, вышедший
    /// из портала, тут же затягивался бы в него снова.
    /// </summary>
    [Test]
    public void ShouldCross_IsFalseWhenMovingOutThroughTheBack()
    {
        Assert.IsFalse(PortalTraveller.ShouldCross(-0.1f, 0.2f, true));
    }

    [Test]
    public void ShouldCross_IsFalseWhenStayingOnTheSameSide()
    {
        Assert.IsFalse(PortalTraveller.ShouldCross(0.5f, 0.2f, true));
        Assert.IsFalse(PortalTraveller.ShouldCross(-0.5f, -0.2f, true));
    }

    /// <summary>
    /// Первое наблюдение переходом быть не может: предыдущего расстояния ещё нет,
    /// и любое значение прочиталось бы как смена знака.
    /// </summary>
    [Test]
    public void ShouldCross_IsFalseWhenPreviousDistanceIsUnknown()
    {
        Assert.IsFalse(PortalTraveller.ShouldCross(float.NaN, -0.1f, true));
    }

    /// <summary>Касание плоскости ровно в ноль уже считается переходом.</summary>
    [Test]
    public void ShouldCross_CountsExactlyZeroAsCrossed()
    {
        Assert.IsTrue(PortalTraveller.ShouldCross(0.2f, 0f, true));
    }

    [Test]
    public void ResetPortalTracking_ForgetsThePreviousDistance()
    {
        var travellerObject = new GameObject("Traveller");
        var portalObject = new GameObject("Portal");
        try
        {
            PortalTraveller traveller = travellerObject.AddComponent<PortalTraveller>();
            Portal portal = portalObject.AddComponent<Portal>();

            traveller.TrackDistance(portal, 0.5f);
            Assert.IsTrue(traveller.TryGetTrackedDistance(portal, out float tracked));
            Assert.That(tracked, Is.EqualTo(0.5f).Within(1e-5f));

            traveller.ResetPortalTracking();

            Assert.IsFalse(traveller.TryGetTrackedDistance(portal, out _));
        }
        finally
        {
            Object.DestroyImmediate(travellerObject);
            Object.DestroyImmediate(portalObject);
        }
    }

    /// <summary>
    /// Перенос применяет матрицу перехода к позе целиком: и положение, и поворот.
    /// Через перпендикулярную пару взгляд обязан развернуться на 90 градусов.
    /// </summary>
    [Test]
    public void Teleport_AppliesTheTransformToPositionAndRotation()
    {
        var travellerObject = new GameObject("Traveller");
        var entranceObject = new GameObject("Entrance");
        var exitObject = new GameObject("Exit");
        try
        {
            entranceObject.transform.SetPositionAndRotation(
                Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            exitObject.transform.SetPositionAndRotation(
                new Vector3(30f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));

            Portal entrance = entranceObject.AddComponent<Portal>();
            Portal exit = exitObject.AddComponent<Portal>();
            entrance.exitPortal = exit;
            exit.exitPortal = entrance;

            PortalTraveller traveller = travellerObject.AddComponent<PortalTraveller>();
            travellerObject.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, 2f), Quaternion.identity);

            PortalTeleportContext reported = default;
            bool raised = false;
            traveller.Teleported += context =>
            {
                reported = context;
                raised = true;
            };

            traveller.Teleport(entrance);

            Assert.IsTrue(raised, "событие перехода не поднялось");
            Assert.AreSame(entrance, reported.Entrance);
            Assert.AreSame(exit, reported.Exit);

            Matrix4x4 expected = PortalMath.EntranceToExit(
                entranceObject.transform, exitObject.transform);
            Vector3 expectedPosition = expected.MultiplyPoint(new Vector3(0f, 0f, 2f));

            Assert.That(
                Vector3.Distance(travellerObject.transform.position, expectedPosition),
                Is.LessThan(1e-3f));
            Assert.That(
                Quaternion.Angle(travellerObject.transform.rotation, reported.Rotation),
                Is.EqualTo(0f).Within(1e-2f),
                "поворот корня должен совпасть с поворотом перехода");
        }
        finally
        {
            Object.DestroyImmediate(travellerObject);
            Object.DestroyImmediate(entranceObject);
            Object.DestroyImmediate(exitObject);
        }
    }

    /// <summary>
    /// После переноса отслеживание обязано обнулиться. Портал выхода стоит прямо
    /// за спиной, и сохранённое расстояние до входа прочиталось бы как ещё одно
    /// пересечение — игрок замкнулся бы в петлю.
    /// </summary>
    [Test]
    public void Teleport_ForgetsTrackingSoTheExitDoesNotFireImmediately()
    {
        var travellerObject = new GameObject("Traveller");
        var entranceObject = new GameObject("Entrance");
        var exitObject = new GameObject("Exit");
        try
        {
            exitObject.transform.position = new Vector3(30f, 0f, 0f);

            Portal entrance = entranceObject.AddComponent<Portal>();
            Portal exit = exitObject.AddComponent<Portal>();
            entrance.exitPortal = exit;
            exit.exitPortal = entrance;

            PortalTraveller traveller = travellerObject.AddComponent<PortalTraveller>();
            traveller.TrackDistance(entrance, 0.3f);
            traveller.TrackDistance(exit, 0.4f);

            traveller.Teleport(entrance);

            Assert.IsFalse(traveller.TryGetTrackedDistance(entrance, out _));
            Assert.IsFalse(traveller.TryGetTrackedDistance(exit, out _));
        }
        finally
        {
            Object.DestroyImmediate(travellerObject);
            Object.DestroyImmediate(entranceObject);
            Object.DestroyImmediate(exitObject);
        }
    }

    /// <summary>Скорость физического тела обязана повернуться вместе с ним.</summary>
    [Test]
    public void Teleport_RotatesRigidbodyVelocity()
    {
        var travellerObject = new GameObject("Traveller");
        var entranceObject = new GameObject("Entrance");
        var exitObject = new GameObject("Exit");
        try
        {
            entranceObject.transform.SetPositionAndRotation(
                Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
            exitObject.transform.SetPositionAndRotation(
                new Vector3(30f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));

            Portal entrance = entranceObject.AddComponent<Portal>();
            Portal exit = exitObject.AddComponent<Portal>();
            entrance.exitPortal = exit;
            exit.exitPortal = entrance;

            Rigidbody body = travellerObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.linearVelocity = new Vector3(0f, 0f, 5f);

            PortalTraveller traveller = travellerObject.AddComponent<PortalTraveller>();
            traveller.Teleport(entrance);

            Matrix4x4 transformMatrix = PortalMath.EntranceToExit(
                entranceObject.transform, exitObject.transform);
            Vector3 expected = transformMatrix.MultiplyVector(new Vector3(0f, 0f, 5f));

            Assert.That(Vector3.Distance(body.linearVelocity, expected), Is.LessThan(1e-3f));
            Assert.That(body.linearVelocity.magnitude, Is.EqualTo(5f).Within(1e-3f),
                "поворот не должен менять величину скорости");
        }
        finally
        {
            Object.DestroyImmediate(travellerObject);
            Object.DestroyImmediate(entranceObject);
            Object.DestroyImmediate(exitObject);
        }
    }
}
