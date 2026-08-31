using UnityEngine;

/// <summary>Консервативное покрытие физического проёма в экранных координатах.</summary>
public static class PortalVisibility
{
    public readonly struct Coverage
    {
        public readonly Rect Bounds;
        public readonly bool IsEmpty;
        public readonly bool IsUncertain;
        private Coverage(Rect bounds, bool empty, bool uncertain)
        { Bounds = bounds; IsEmpty = empty; IsUncertain = uncertain; }
        public static Coverage Bounded(Rect bounds) => new Coverage(bounds, false, false);
        public static Coverage Empty => new Coverage(default, true, false);
        public static Coverage Unknown => new Coverage(new Rect(0, 0, 1, 1), false, true);
    }

    private const float Epsilon = 1e-6f;

    public static bool Intersects(Rect parent, Rect child)
    {
        if (!Valid(parent) || !Valid(child)) return true;
        return parent.xMax + Epsilon >= child.xMin && child.xMax + Epsilon >= parent.xMin
            && parent.yMax + Epsilon >= child.yMin && child.yMax + Epsilon >= parent.yMin;
    }

    public static Coverage ProjectAperture(Matrix4x4 localToWorld, Vector2 size,
        Matrix4x4 worldToClip, Vector3 eye, float near, bool uncertain,
        Vector4[] first, Vector4[] second)
    {
        // Смещённый screen и неподдержанный view не дают права отсекать по физической плоскости.
        if (uncertain || !Finite(localToWorld) || !Finite(worldToClip)
            || !Finite(size.x) || !Finite(size.y) || size.x <= 0 || size.y <= 0
            || !Finite(near) || near < 0 || localToWorld.determinant <= 0)
            return Coverage.Unknown;
        if (first == null || second == null || first.Length < 16 || second.Length < 16
            || ReferenceEquals(first, second))
            throw new System.ArgumentException("Two distinct scratch buffers of at least 16 vertices are required.");

        Matrix4x4 localToClip = worldToClip * localToWorld;
        float halfX = size.x * 0.5f, halfY = size.y * 0.5f;
        first[0] = localToClip * new Vector4(-halfX, -halfY, 0, 1);
        first[1] = localToClip * new Vector4(halfX, -halfY, 0, 1);
        first[2] = localToClip * new Vector4(halfX, halfY, 0, 1);
        first[3] = localToClip * new Vector4(-halfX, halfY, 0, 1);
        float minW = float.PositiveInfinity, maxW = float.NegativeInfinity;
        for (int i = 0; i < 4; i++)
        {
            for (int axis = 0; axis < 4; axis++) if (!Finite(first[i][axis])) return Coverage.Unknown;
            minW = Mathf.Min(minW, first[i].w);
            maxW = Mathf.Max(maxW, first[i].w);
        }
        float guard = Mathf.Max(near, Epsilon);
        if (maxW < -guard) return Coverage.Empty;
        // Физический проём внутри near-plane может иметь видимый удерживаемый screen.
        if (minW <= guard) return Coverage.Unknown;

        Vector3 normal = Vector3.Cross(localToWorld.GetColumn(0), localToWorld.GetColumn(1));
        if (normal.sqrMagnitude <= Epsilon * Epsilon) return Coverage.Unknown;
        float side = Vector3.Dot(normal.normalized, eye - (Vector3)localToWorld.GetColumn(3));
        if (!Finite(side) || Mathf.Abs(side) <= Epsilon) return Coverage.Unknown;
        if (side < 0) return Coverage.Empty;

        // Выпуклый polygon режется до perspective divide. Z-плоскости здесь намеренно
        // не используются: culling не дублирует и не меняет oblique render projection.
        int count = 4;
        Vector4[] input = first, output = second;
        for (int plane = 0; plane < 5; plane++)
        {
            int written = 0;
            Vector4 previous = input[count - 1];
            double previousDistance = Distance(previous, plane);
            for (int i = 0; i < count; i++)
            {
                Vector4 current = input[i];
                double currentDistance = Distance(current, plane);
                bool previousInside = previousDistance >= 0, currentInside = currentDistance >= 0;
                if (previousInside != currentInside)
                {
                    double denominator = previousDistance - currentDistance;
                    if (System.Math.Abs(denominator) < 1e-12 || written >= output.Length) return Coverage.Unknown;
                    float t = (float)(previousDistance / denominator);
                    output[written++] = Vector4.LerpUnclamped(previous, current, t);
                }
                if (currentInside)
                {
                    if (written >= output.Length) return Coverage.Unknown;
                    output[written++] = current;
                }
                previous = current;
                previousDistance = currentDistance;
            }
            if (written == 0) return Coverage.Empty;
            count = written;
            Vector4[] swap = input; input = output; output = swap;
        }
        float xMin = 1, yMin = 1, xMax = 0, yMax = 0;
        for (int i = 0; i < count; i++)
        {
            if (input[i].w <= Epsilon) return Coverage.Unknown;
            float x = input[i].x / input[i].w * 0.5f + 0.5f;
            float y = input[i].y / input[i].w * 0.5f + 0.5f;
            if (!Finite(x) || !Finite(y)) return Coverage.Unknown;
            xMin = Mathf.Min(xMin, x); yMin = Mathf.Min(yMin, y);
            xMax = Mathf.Max(xMax, x); yMax = Mathf.Max(yMax, y);
        }
        return Coverage.Bounded(Rect.MinMaxRect(Mathf.Clamp01(xMin - Epsilon), Mathf.Clamp01(yMin - Epsilon),
            Mathf.Clamp01(xMax + Epsilon), Mathf.Clamp01(yMax + Epsilon)));
    }

    public static Coverage IntersectConsumers(Coverage parent, Coverage own, Coverage exit)
    {
        if (parent.IsEmpty || (own.IsEmpty && exit.IsEmpty)) return Coverage.Empty;
        if (parent.IsUncertain || own.IsUncertain || exit.IsUncertain) return Coverage.Unknown;
        Coverage first = Intersection(parent, own), second = Intersection(parent, exit);
        if (first.IsEmpty) return second;
        if (second.IsEmpty) return first;
        return Coverage.Bounded(Rect.MinMaxRect(Mathf.Min(first.Bounds.xMin, second.Bounds.xMin),
            Mathf.Min(first.Bounds.yMin, second.Bounds.yMin), Mathf.Max(first.Bounds.xMax, second.Bounds.xMax),
            Mathf.Max(first.Bounds.yMax, second.Bounds.yMax)));
    }

    private static Coverage Intersection(Coverage a, Coverage b)
    {
        if (a.IsEmpty || b.IsEmpty || !Intersects(a.Bounds, b.Bounds)) return Coverage.Empty;
        float x = Mathf.Max(a.Bounds.xMin, b.Bounds.xMin), y = Mathf.Max(a.Bounds.yMin, b.Bounds.yMin);
        return Coverage.Bounded(new Rect(x, y, Mathf.Max(0, Mathf.Min(a.Bounds.xMax, b.Bounds.xMax) - x),
            Mathf.Max(0, Mathf.Min(a.Bounds.yMax, b.Bounds.yMax) - y)));
    }

    private static double Distance(Vector4 point, int plane)
    {
        double w = point.w * (1.0 + Epsilon);
        switch (plane)
        {
            case 0: return point.w - Epsilon;
            case 1: return w + point.x;
            case 2: return w - point.x;
            case 3: return w + point.y;
            default: return w - point.y;
        }
    }
    private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    private static bool Finite(Matrix4x4 matrix)
    {
        for (int i = 0; i < 16; i++) if (!Finite(matrix[i])) return false;
        return true;
    }
    private static bool Valid(Rect rect) => Finite(rect.xMin) && Finite(rect.xMax) && Finite(rect.yMin)
        && Finite(rect.yMax) && rect.width >= 0 && rect.height >= 0;
}
