using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;

public sealed class PortalVisibilityBuildTests
{
    [TestCase("Ghost")]
    [TestCase("Cross")]
    [TestCase("Rotate")]
    public void LegacyBuildUsesSavedSceneWithoutCertifiedContext(string name)
    {
        Type builder = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("PortalVisibilityCheckBuilder")).FirstOrDefault(type => type != null);
        Assert.That(builder, Is.Not.Null, "A build-only entry must not regenerate legacy scene profiles.");
        MethodInfo method = builder.GetMethod("LegacyOptions");
        Assert.That(method, Is.Not.Null);
        var options = (BuildPlayerOptions)method.Invoke(null, new object[] { name, "Logs/test-build" });
        CollectionAssert.AreEqual(new[] { "Assets/LabTools/" + name + "Check.unity" }, options.scenes);
        Assert.That(options.target, Is.EqualTo(BuildTarget.StandaloneWindows64));
        Assert.That(options.options.HasFlag(BuildOptions.Development), Is.True);
        Assert.That(options.locationPathName, Does.EndWith(name + "Check.exe"));
    }
}
