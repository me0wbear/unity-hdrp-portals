using NUnit.Framework;
using UnityEngine;

public sealed class PortalApertureTests
{
    private const float Thickness = 0.1f;

    /// <summary>Дальше запаса квад стоит ровно в плоскости проёма.</summary>
    [Test]
    public void Offset_IsZeroBeyondTheThickness()
    {
        Assert.That(PortalAperture.Offset(0.5f, Thickness), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(PortalAperture.Offset(-0.5f, Thickness), Is.EqualTo(0f).Within(1e-6f));
        Assert.That(PortalAperture.Offset(Thickness, Thickness), Is.EqualTo(0f).Within(1e-6f));
    }

    /// <summary>
    /// Главное свойство: вблизи расстояние от наблюдателя до квада всегда равно
    /// запасу. Именно оно не даёт ближней плоскости срезать квад.
    /// </summary>
    [Test]
    public void Offset_KeepsTheScreenAtThicknessFromTheViewer()
    {
        foreach (float distance in new[] { 0.09f, 0.05f, 0.01f, 0f, -0.01f, -0.05f, -0.09f })
        {
            float offset = PortalAperture.Offset(distance, Thickness);
            float remaining = Mathf.Abs(distance - offset);

            Assert.That(remaining, Is.EqualTo(Thickness).Within(1e-5f),
                "на расстоянии " + distance + " квад должен остаться в " + Thickness + " м от глаза");
        }
    }

    /// <summary>
    /// На границе запаса обе ветки обязаны дать одно и то же, иначе квад
    /// дёргается ровно в тот момент, когда игрок подходит к порталу.
    /// </summary>
    [Test]
    public void Offset_IsContinuousAtTheThresholdOnBothSides()
    {
        const float epsilon = 1e-4f;

        Assert.That(
            PortalAperture.Offset(Thickness - epsilon, Thickness),
            Is.EqualTo(PortalAperture.Offset(Thickness + epsilon, Thickness)).Within(1e-3f));

        Assert.That(
            PortalAperture.Offset(-Thickness + epsilon, Thickness),
            Is.EqualTo(PortalAperture.Offset(-Thickness - epsilon, Thickness)).Within(1e-3f));
    }

    /// <summary>Квад отодвигается от наблюдателя, а не навстречу ему.</summary>
    [Test]
    public void Offset_MovesTheScreenAwayFromTheViewer()
    {
        Assert.That(PortalAperture.Offset(0.02f, Thickness), Is.LessThan(0f),
            "наблюдатель перед порталом — квад уходит в минус по локальной Z");
        Assert.That(PortalAperture.Offset(-0.02f, Thickness), Is.GreaterThan(0f),
            "наблюдатель за порталом — квад уходит в плюс");
    }
}
