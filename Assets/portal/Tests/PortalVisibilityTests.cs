using NUnit.Framework;
using UnityEngine;

public sealed class PortalVisibilityTests
{
    private readonly Vector4[] first = new Vector4[16];
    private readonly Vector4[] second = new Vector4[16];
    private static readonly Matrix4x4 Projection = Matrix4x4.Perspective(90f, 1f, 0.1f, 100f);
    private static readonly Matrix4x4 View = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));

    private PortalVisibility.Coverage Project(Vector3 position, Vector2 size,
        Quaternion? rotation = null, Vector3? scale = null, bool uncertain = false)
    {
        return PortalVisibility.ProjectAperture(
            Matrix4x4.TRS(position, rotation ?? Quaternion.Euler(0, 180, 0), scale ?? Vector3.one),
            size, Projection * View, Vector3.zero, 0.1f, uncertain, first, second);
    }

    [Test]
    public void DisjointRectanglesDoNotIntersect() => Assert.That(PortalVisibility.Intersects(
        new Rect(0, 0, 0.4f, 1), new Rect(0.6f, 0, 0.4f, 1)), Is.False);

    [TestCase(0.4f)]
    [TestCase(0.399999f)]
    public void TouchingAndTinyOverlappingRectanglesRemainVisible(float x) => Assert.That(
        PortalVisibility.Intersects(new Rect(0, 0, 0.4f, 1), new Rect(x, 0, 0.4f, 1)), Is.True);

    [Test]
    public void ApertureInFrontHasHandCalculatedCoverage()
    {
        var result = Project(new Vector3(0, 0, 2), new Vector2(2, 2));
        Assert.That(result.IsEmpty, Is.False);
        Assert.That(result.IsUncertain, Is.False);
        Assert.That(result.Bounds.xMin, Is.EqualTo(0.25f).Within(1e-4));
        Assert.That(result.Bounds.xMax, Is.EqualTo(0.75f).Within(1e-4));
        Assert.That(result.Bounds.yMin, Is.EqualTo(0.25f).Within(1e-4));
    }

    [Test]
    public void ApertureOutsideViewportIsEmpty() => Assert.That(
        Project(new Vector3(8, 0, 2), Vector2.one).IsEmpty, Is.True);

    [Test]
    public void ApertureWhollyBehindEyeIsEmpty() => Assert.That(
        Project(new Vector3(0, 0, -2), Vector2.one, Quaternion.identity).IsEmpty, Is.True);

    [Test]
    public void BackFacingApertureIsEmpty() => Assert.That(
        Project(new Vector3(0, 0, 2), Vector2.one, Quaternion.identity).IsEmpty, Is.True);

    [Test]
    public void PolygonCrossingViewportIsClippedBeforeDivision()
    {
        var result = Project(new Vector3(2, 0, 2), new Vector2(2, 2));
        Assert.That(result.IsEmpty, Is.False);
        Assert.That(result.Bounds.xMin, Is.EqualTo(0.75f).Within(1e-4));
        Assert.That(result.Bounds.xMax, Is.EqualTo(1f).Within(1e-4));
    }

    [TestCase(0f)]
    [TestCase(0.05f)]
    [TestCase(-0.05f)]
    public void EyeAndNearPlaneAmbiguityKeepsCoverage(float z)
    {
        var result = Project(new Vector3(0, 0, z), Vector2.one, Quaternion.Euler(0, 135, 0));
        Assert.That(result.IsEmpty, Is.False);
        Assert.That(result.IsUncertain, Is.True);
    }

    [Test]
    public void ScaledRotatedPhysicalApertureUsesFullTransform()
    {
        var result = Project(new Vector3(0, 0, 4), new Vector2(2, 2),
            Quaternion.Euler(0, 180, 90), new Vector3(2, 1, 1));
        Assert.That(result.Bounds.width, Is.EqualTo(0.25f).Within(1e-4));
        Assert.That(result.Bounds.height, Is.EqualTo(0.5f).Within(1e-4));
    }

    [Test]
    public void DisplacedOrUnsupportedApertureCannotBeCulledByPhysicalGeometry()
    {
        var result = Project(new Vector3(100, 0, -2), Vector2.one, uncertain: true);
        Assert.That(result.IsEmpty, Is.False);
        Assert.That(result.IsUncertain, Is.True);
    }

    [Test]
    public void NonFiniteProjectionPreservesCoverage()
    {
        Matrix4x4 invalid = Projection * View;
        invalid.m00 = float.NaN;
        var result = PortalVisibility.ProjectAperture(Matrix4x4.identity, Vector2.one,
            invalid, Vector3.zero, 0.1f, false, first, second);
        Assert.That(result.IsUncertain, Is.True);
    }

    [Test]
    public void EitherConsumerCanRequireTheNextLevel()
    {
        var parent = PortalVisibility.Coverage.Bounded(new Rect(0.1f, 0.1f, 0.2f, 0.2f));
        var outside = PortalVisibility.Coverage.Bounded(new Rect(0.6f, 0.6f, 0.1f, 0.1f));
        var inside = PortalVisibility.Coverage.Bounded(new Rect(0.2f, 0.2f, 0.2f, 0.2f));
        Assert.That(PortalVisibility.IntersectConsumers(parent, outside, inside).IsEmpty, Is.False);
        Assert.That(PortalVisibility.IntersectConsumers(parent, inside, outside).IsEmpty, Is.False);
        Assert.That(PortalVisibility.IntersectConsumers(parent, outside, outside).IsEmpty, Is.True);
        Assert.That(PortalVisibility.IntersectConsumers(parent, outside, PortalVisibility.Coverage.Unknown).IsEmpty, Is.False);
    }
}
