using NUnit.Framework;

public sealed class PortalBudgetTests
{
    [TestCase(0, 0, 0, 0)]
    [TestCase(1, 1, 0, 0)]
    [TestCase(3, 1, 1, 1)]
    [TestCase(4, 2, 1, 1)]
    [TestCase(6, 2, 2, 2)]
    [TestCase(8, 3, 3, 2)]
    [TestCase(30, 3, 3, 3)]
    public void RootsAreReservedBeforeRecursion(int budget, int a, int b, int c)
    {
        var output = new int[3];
        PortalBudget.AllocateVisibleLevels(new[] { 3, 3, 3 }, budget, output);
        CollectionAssert.AreEqual(new[] { a, b, c }, output);
    }

    [Test]
    public void CoveragePriorityIsStableAndDoesNotMutateRequests()
    {
        var wanted = new[] { 3, 3, 3 };
        var output = new int[3];
        var order = new int[3];
        for (int i = 0; i < 3; i++)
        {
            PortalBudget.AllocateVisibleLevels(wanted, new[] { 0.1f, 0.8f, 0.8f }, 3, 2, output, order);
            CollectionAssert.AreEqual(new[] { 0, 1, 1 }, output);
            CollectionAssert.AreEqual(new[] { 3, 3, 3 }, wanted);
        }
    }

    [TestCase(-1)]
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(20)]
    public void OneRootNeverExceedsWantedOrBudget(int budget)
    {
        var output = new int[1];
        PortalBudget.AllocateVisibleLevels(new[] { 3 }, budget, output);
        Assert.That(output[0], Is.EqualTo(System.Math.Max(0, System.Math.Min(3, budget))));
    }

    [Test]
    public void NonPositiveWantedAndUnusedBufferTailAreCleared()
    {
        var output = new[] { 9, 9, 9, 9, 9 };
        PortalBudget.AllocateVisibleLevels(new[] { -3, 0, 2, 99 }, new[] { 1f, 1f, 0.1f, 1f },
            3, 20, output, new int[4]);
        CollectionAssert.AreEqual(new[] { 0, 0, 2, 0, 0 }, output);
    }
}
