using NUnit.Framework;
using UnityEngine;

public sealed class PortalMathTests
{
    private Transform _entrance;
    private Transform _exit;

    [SetUp]
    public void SetUp()
    {
        _entrance = new GameObject("Entrance").transform;
        _exit = new GameObject("Exit").transform;
    }

    [TearDown]
    public void TearDown()
    {
        Object.DestroyImmediate(_entrance.gameObject);
        Object.DestroyImmediate(_exit.gameObject);
    }

    /// <summary>
    /// Точка перед входом должна оказаться позади выхода: игрок входит в портал
    /// с лицевой стороны и выходит из парного, повернувшись к нему спиной.
    /// </summary>
    [Test]
    public void EntranceToExit_PutsPointInFrontOfEntranceBehindExit()
    {
        _entrance.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _exit.SetPositionAndRotation(new Vector3(30f, 0f, 0f), Quaternion.identity);

        // Метр перед входом, вдоль его локального +Z.
        Vector3 mapped = PortalMath.EntranceToExit(_entrance, _exit)
            .MultiplyPoint(new Vector3(0f, 0f, 1f));

        // Позади выхода, то есть на метр в его локальный -Z.
        Assert.That(mapped.x, Is.EqualTo(30f).Within(1e-4f));
        Assert.That(mapped.z, Is.EqualTo(-1f).Within(1e-4f));
    }

    /// <summary>Перпендикулярные порталы должны поворачивать направление ровно на 90 градусов.</summary>
    [Test]
    public void EntranceToExit_PerpendicularPortals_RotateDirectionByNinetyDegrees()
    {
        _entrance.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
        _exit.SetPositionAndRotation(new Vector3(30f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));

        Matrix4x4 transform = PortalMath.EntranceToExit(_entrance, _exit);
        Vector3 rotated = transform.MultiplyVector(Vector3.forward);
        float angle = Vector3.Angle(Vector3.forward, rotated);

        Assert.That(angle, Is.EqualTo(90f).Within(1e-3f));
    }

    /// <summary>Проход туда и обратно возвращает исходную позу.</summary>
    [Test]
    public void EntranceToExit_ThereAndBack_IsIdentity()
    {
        _entrance.SetPositionAndRotation(new Vector3(1f, 2f, 3f), Quaternion.Euler(0f, 35f, 0f));
        _exit.SetPositionAndRotation(new Vector3(-8f, 1f, 12f), Quaternion.Euler(0f, -70f, 0f));

        Matrix4x4 there = PortalMath.EntranceToExit(_entrance, _exit);
        Matrix4x4 back = PortalMath.EntranceToExit(_exit, _entrance);
        Vector3 start = new Vector3(0.4f, 1.7f, -2.5f);

        Vector3 result = back.MultiplyPoint(there.MultiplyPoint(start));

        Assert.That(Vector3.Distance(result, start), Is.LessThan(1e-3f));
    }

    /// <summary>Уровень k — это k-кратное применение матрицы перехода.</summary>
    [Test]
    public void EntranceToExit_WithTimes_ChainsTheTransform()
    {
        _entrance.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
        _exit.SetPositionAndRotation(new Vector3(30f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));

        Matrix4x4 single = PortalMath.EntranceToExit(_entrance, _exit);
        Matrix4x4 triple = PortalMath.EntranceToExit(_entrance, _exit, 3);
        Vector3 point = new Vector3(0.5f, 1.2f, 2f);

        Vector3 expected = single.MultiplyPoint(single.MultiplyPoint(single.MultiplyPoint(point)));

        Assert.That(Vector3.Distance(triple.MultiplyPoint(point), expected), Is.LessThan(1e-3f));
    }

    /// <summary>Ноль применений матрицы перехода не должен менять точку.</summary>
    [Test]
    public void EntranceToExit_WithZeroTimes_IsIdentity()
    {
        _entrance.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 180f, 0f));
        _exit.SetPositionAndRotation(new Vector3(30f, 0f, 0f), Quaternion.Euler(0f, 90f, 0f));

        var point = new Vector3(0.5f, 1.2f, 2f);
        Vector3 result = PortalMath.EntranceToExit(_entrance, _exit, 0).MultiplyPoint(point);

        Assert.That(Vector3.Distance(result, point), Is.LessThan(1e-5f));
    }

    /// <summary>
    /// Масштаб на корне портала не должен попадать в матрицу перехода: иначе
    /// случайно растянутый трансформ растянул бы и вид, и прошедшего игрока.
    /// </summary>
    [Test]
    public void EntranceToExit_IgnoresPortalScale()
    {
        _entrance.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _exit.SetPositionAndRotation(new Vector3(30f, 0f, 0f), Quaternion.identity);
        _entrance.localScale = new Vector3(3f, 3f, 3f);
        _exit.localScale = new Vector3(0.5f, 0.5f, 0.5f);

        Vector3 mapped = PortalMath.EntranceToExit(_entrance, _exit)
            .MultiplyPoint(new Vector3(0f, 0f, 1f));

        Assert.That(mapped.x, Is.EqualTo(30f).Within(1e-4f));
        Assert.That(mapped.z, Is.EqualTo(-1f).Within(1e-4f));
    }

    /// <summary>
    /// Косая ближняя плоскость обязана отсечь всё, что стоит между камерой и плоскостью
    /// выхода, и оставить всё, что за ней. Без этого в проём протекает геометрия,
    /// стоящая позади парного портала.
    /// </summary>
    [Test]
    public void ObliqueMatrix_ClipsGeometryBetweenCameraAndPortalPlane()
    {
        var cameraObject = new GameObject("Camera");
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, -5f), Quaternion.identity);

            // Плоскость портала в начале координат, нормаль смотрит в +Z, то есть от камеры.
            Vector4 plane = PortalMath.CameraSpacePlane(camera, Vector3.zero, Vector3.forward, 0f);
            Matrix4x4 projection = camera.CalculateObliqueMatrix(plane);
            Matrix4x4 viewProjection = projection * camera.worldToCameraMatrix;

            Assert.IsTrue(IsClipped(viewProjection, new Vector3(0f, 0f, -1f)),
                "точка между камерой и плоскостью портала должна быть отсечена");
            Assert.IsFalse(IsClipped(viewProjection, new Vector3(0f, 0f, 5f)),
                "точка за плоскостью портала должна остаться видимой");
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }

    /// <summary>
    /// Сдвиг плоскости вдоль нормали должен отодвигать границу отсечения: точка,
    /// стоявшая сразу за плоскостью, при положительном сдвиге тоже отсекается.
    /// Этим полем убирается мерцание точно на грани проёма.
    /// </summary>
    [Test]
    public void CameraSpacePlane_OffsetMovesTheClipBoundaryAlongTheNormal()
    {
        var cameraObject = new GameObject("Camera");
        try
        {
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 500f;
            camera.fieldOfView = 60f;
            camera.aspect = 16f / 9f;
            cameraObject.transform.SetPositionAndRotation(
                new Vector3(0f, 0f, -5f), Quaternion.identity);

            var justBeyond = new Vector3(0f, 0f, 0.1f);

            Matrix4x4 withoutOffset = camera.CalculateObliqueMatrix(
                PortalMath.CameraSpacePlane(camera, Vector3.zero, Vector3.forward, 0f))
                * camera.worldToCameraMatrix;
            Matrix4x4 withOffset = camera.CalculateObliqueMatrix(
                PortalMath.CameraSpacePlane(camera, Vector3.zero, Vector3.forward, 0.5f))
                * camera.worldToCameraMatrix;

            Assert.IsFalse(IsClipped(withoutOffset, justBeyond));
            Assert.IsTrue(IsClipped(withOffset, justBeyond));
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }

    /// <summary>
    /// Camera.projectionMatrix использует соглашение OpenGL, где видимый диапазон
    /// по глубине это -w &lt;= z &lt;= w. Всё, что ближе -w, отсечено ближней плоскостью.
    /// </summary>
    private static bool IsClipped(Matrix4x4 viewProjection, Vector3 worldPoint)
    {
        Vector4 clip = viewProjection * new Vector4(worldPoint.x, worldPoint.y, worldPoint.z, 1f);
        return clip.z < -clip.w;
    }

    [Test]
    public void SignedDistance_IsPositiveInFrontAndNegativeBehind()
    {
        _entrance.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

        Assert.That(PortalMath.SignedDistance(_entrance, new Vector3(0f, 0f, 2f)),
            Is.EqualTo(2f).Within(1e-4f));
        Assert.That(PortalMath.SignedDistance(_entrance, new Vector3(0f, 0f, -3f)),
            Is.EqualTo(-3f).Within(1e-4f));
    }

    [Test]
    public void IsInsideOpening_AcceptsCentreAndRejectsPointBeyondTheEdge()
    {
        _entrance.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        var size = new Vector2(2f, 3f);

        Assert.IsTrue(PortalMath.IsInsideOpening(_entrance, new Vector3(0.5f, 1f, 0f), size, 0f));
        Assert.IsFalse(PortalMath.IsInsideOpening(_entrance, new Vector3(2.5f, 0f, 0f), size, 0f));
        Assert.IsTrue(PortalMath.IsInsideOpening(_entrance, new Vector3(1.4f, 0f, 0f), size, 0.5f),
            "запас должен расширять проём");
    }

    /// <summary>
    /// Проверка попадания идёт в локальных координатах портала, поэтому поворот
    /// портала должен поворачивать и прямоугольник проёма вместе с ним.
    /// </summary>
    [Test]
    public void IsInsideOpening_FollowsThePortalRotation()
    {
        _entrance.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 90f, 0f));
        var size = new Vector2(2f, 3f);

        // Портал развёрнут на 90 градусов, поэтому его ширина легла вдоль мировой Z.
        Assert.IsTrue(PortalMath.IsInsideOpening(_entrance, new Vector3(0f, 0f, 0.8f), size, 0f));
        Assert.IsFalse(PortalMath.IsInsideOpening(_entrance, new Vector3(0f, 0f, 1.8f), size, 0f));
    }

    /// <summary>Расстояние удержания обязано остаться за ближней плоскостью.</summary>
    [Test]
    public void ScreenHoldDistance_StaysBeyondTheNearPlane()
    {
        var cameraObject = new GameObject("Camera");
        try
        {
            Camera camera = MakeCamera(cameraObject, near: 0.05f, fov: 60f);
            var opening = new Vector2(2.2f, 3.2f);

            foreach (float safety in new[] { 0f, 0.5f, 1f, 2f, 10f })
            {
                float hold = PortalMath.ScreenHoldDistance(camera, opening, safety);
                Assert.That(hold, Is.GreaterThan(camera.nearClipPlane),
                    "при запасе " + safety + " квад оказался бы за ближней плоскостью");
            }
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }

    /// <summary>
    /// На расстоянии удержания квад обязан закрывать весь экран. Иначе по краям
    /// видно то, что за порталом, — это и есть просвет, который ловит CloseCheck.
    /// </summary>
    [Test]
    public void ScreenHoldDistance_KeepsTheOpeningCoveringTheWholeScreen()
    {
        var cameraObject = new GameObject("Camera");
        try
        {
            // Ближняя плоскость по умолчанию: именно с ней собраны чек-сцены,
            // и именно на ней расчёт по углу ближней плоскости давал перелёт.
            Camera camera = MakeCamera(cameraObject, near: 0.3f, fov: 60f);
            var opening = new Vector2(2.2f, 3.2f);

            float hold = PortalMath.ScreenHoldDistance(camera, opening, 2f);

            float tanVertical = Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanHorizontal = tanVertical * camera.aspect;

            Assert.That(hold * tanHorizontal, Is.LessThanOrEqualTo(opening.x * 0.5f + 1e-4f),
                "по горизонтали квад не закрывает экран");
            Assert.That(hold * tanVertical, Is.LessThanOrEqualTo(opening.y * 0.5f + 1e-4f),
                "по вертикали квад не закрывает экран");
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }

    /// <summary>
    /// Узкий проём закрыть экран не может ни на каком расстоянии. Тогда
    /// побеждает нижнее требование: лучше видимые края, чем погасший проём.
    /// </summary>
    [Test]
    public void ScreenHoldDistance_PrefersStayingVisibleWhenTheOpeningIsTooNarrow()
    {
        var cameraObject = new GameObject("Camera");
        try
        {
            Camera camera = MakeCamera(cameraObject, near: 0.3f, fov: 60f);

            float hold = PortalMath.ScreenHoldDistance(camera, new Vector2(0.05f, 0.05f), 2f);

            Assert.That(hold, Is.GreaterThan(camera.nearClipPlane));
        }
        finally
        {
            Object.DestroyImmediate(cameraObject);
        }
    }

    private static Camera MakeCamera(GameObject host, float near, float fov)
    {
        Camera camera = host.AddComponent<Camera>();
        camera.nearClipPlane = near;
        camera.farClipPlane = 500f;
        camera.fieldOfView = fov;
        camera.aspect = 16f / 9f;
        return camera;
    }
}
