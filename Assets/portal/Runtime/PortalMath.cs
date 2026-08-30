using UnityEngine;

/// <summary>
/// Геометрия портала, вынесенная из компонентов, чтобы её можно было проверить
/// без сцены. Всё здесь — чистые функции от аргументов.
/// </summary>
public static class PortalMath
{
    /// <summary>
    /// Локальная ось +Z портала смотрит наружу, на наблюдателя. Поэтому переход
    /// между парой включает разворот на 180 градусов: войдя в лицевую сторону
    /// одного портала, наблюдатель выходит из парного, повернувшись к нему спиной.
    /// </summary>
    private static readonly Matrix4x4 HalfTurn = Matrix4x4.Rotate(Quaternion.Euler(0f, 180f, 0f));

    /// <summary>
    /// Поза без масштаба. Масштаб трансформа портала не должен попадать в матрицу
    /// перехода: иначе непреднамеренный масштаб на корне растянет и вид, и игрока.
    /// </summary>
    private static Matrix4x4 Pose(Transform transform)
    {
        return Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one);
    }

    /// <summary>Мировая матрица, переносящая позу от входа к выходу.</summary>
    public static Matrix4x4 EntranceToExit(Transform entrance, Transform exit)
    {
        return Pose(exit) * HalfTurn * Pose(entrance).inverse;
    }

    /// <summary>
    /// Матрица перехода, применённая <paramref name="times"/> раз. Уровень рекурсии k
    /// это ровно k-кратное применение: наблюдатель смотрит сквозь портал, за ним
    /// снова видит портал, и так далее.
    /// </summary>
    public static Matrix4x4 EntranceToExit(Transform entrance, Transform exit, int times)
    {
        Matrix4x4 step = EntranceToExit(entrance, exit);
        Matrix4x4 result = Matrix4x4.identity;
        for (int i = 0; i < times; i++)
        {
            result = step * result;
        }

        return result;
    }

    /// <summary>
    /// Плоскость в пространстве камеры для <see cref="Camera.CalculateObliqueMatrix"/>.
    /// Отсекается всё, что лежит с обратной стороны от <paramref name="normal"/>.
    /// <paramref name="offset"/> сдвигает плоскость вдоль нормали: небольшой
    /// положительный сдвиг убирает мерцание точно на грани проёма.
    /// </summary>
    public static Vector4 CameraSpacePlane(Camera camera, Vector3 point, Vector3 normal, float offset)
    {
        Matrix4x4 worldToCamera = camera.worldToCameraMatrix;
        Vector3 cameraPoint = worldToCamera.MultiplyPoint(point + normal * offset);
        Vector3 cameraNormal = worldToCamera.MultiplyVector(normal).normalized;

        return new Vector4(
            cameraNormal.x,
            cameraNormal.y,
            cameraNormal.z,
            -Vector3.Dot(cameraPoint, cameraNormal));
    }

    /// <summary>Расстояние до плоскости портала. Положительное — перед лицевой стороной.</summary>
    public static float SignedDistance(Transform portal, Vector3 worldPoint)
    {
        return Vector3.Dot(portal.forward, worldPoint - portal.position);
    }

    /// <summary>
    /// Попадает ли точка в прямоугольник проёма, если спроецировать её на плоскость
    /// портала. <paramref name="margin"/> расширяет прямоугольник во все стороны:
    /// игрок имеет толщину, и засчитывать переход строго по центру нельзя.
    /// </summary>
    public static bool IsInsideOpening(Transform portal, Vector3 worldPoint, Vector2 size, float margin)
    {
        Vector3 local = portal.InverseTransformPoint(worldPoint);
        return Mathf.Abs(local.x) <= size.x * 0.5f + margin
            && Mathf.Abs(local.y) <= size.y * 0.5f + margin;
    }

    /// <summary>
    /// Расстояние от камеры до дальнего угла её ближней плоскости, умноженное на запас.
    /// На эту величину квад портала выдвигается навстречу камере вблизи, иначе
    /// ближняя плоскость прорезает квад и в углах экрана появляется просвет.
    /// </summary>
    public static float NearPlaneThickness(Camera camera, float safetyFactor)
    {
        float halfHeight = camera.nearClipPlane * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * camera.aspect;
        return new Vector3(halfWidth, halfHeight, camera.nearClipPlane).magnitude * safetyFactor;
    }
}
