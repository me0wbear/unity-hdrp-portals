using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Portals.Lab.Tests
{
    public sealed class SandboxCheckBuildTests
    {
        [TestCase("SandboxParity")]
        [TestCase("Performance")]
        public void NewChecksRequireCleanIdentityAndAllowCertifiedCompletion(string check)
        {
            Type builder = LabSerializationTests.FindType("PortalCheckBuildIdentity");
            var options = (BuildPlayerOptions)builder.GetMethod("PrepareOptions").Invoke(null,
                new object[] { new BuildPlayerOptions { options = BuildOptions.Development }, check });
            Assert.That((options.options & BuildOptions.CleanBuildCache) != 0, Is.True);
            object identity = PortalCheckPolicyTests.Identity();
            SandboxCheckPolicyTests.Set(identity, "check", check);
            object session = Activator.CreateInstance(LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckSession"), identity);
            object result = session.GetType().GetMethod("TryComplete").Invoke(session, new object[] { check, "Passed", 360, 0, "" });
            Assert.That(PortalCheckPolicyTests.Field(result, "status"), Is.EqualTo("Passed"));
        }

        [TestCase("SandboxParity", "SandboxParityCheck")]
        [TestCase("Performance", "PortalPerformanceCheck")]
        public void BuildCopyInjectionIsIdempotentAndHasPersistentScript(string check, string probe)
        {
            Type processor = LabSerializationTests.FindType("SandboxCheckBuildProcessor");
            if (!Application.isBatchMode) Assert.Ignore("Requires isolated batchmode editor.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                processor.GetMethod("Inject").Invoke(null, new object[] { scene, check });
                processor.GetMethod("Inject").Invoke(null, new object[] { scene, check });
                Component[] probes = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(LabSerializationTests.FindType(probe), true)).ToArray();
                Assert.That(probes, Has.Length.EqualTo(1));
                Assert.That(EditorUtility.IsPersistent(MonoScript.FromMonoBehaviour((MonoBehaviour)probes[0])), Is.True);
            }
            finally { EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }

        [TestCase("Color")]
        [TestCase("Ghost")]
        [TestCase("")]
        public void UnselectedBuildDoesNotReceiveSandboxProbe(string check)
        {
            Type processor = LabSerializationTests.FindType("SandboxCheckBuildProcessor");
            if (!Application.isBatchMode) Assert.Ignore("Requires isolated batchmode editor.");
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            processor.GetMethod("Inject").Invoke(null, new object[] { scene, check });
            Assert.That(scene.GetRootGameObjects(), Is.Empty);
        }
    }
}
