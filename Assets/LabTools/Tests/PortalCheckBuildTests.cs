using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Portals.Lab.Tests
{
    public sealed class PortalCheckBuildTests
    {
        private static string GitHead()
        {
            var start = new System.Diagnostics.ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = Path.GetFullPath(Application.dataPath + "/.."),
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            using (var process = System.Diagnostics.Process.Start(start))
            {
                string sha = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                Assert.That(process.ExitCode, Is.Zero);
                return sha;
            }
        }

        [Test]
        public void SourceDigestIncludesUntrackedSourceBytes()
        {
            Type type = LabSerializationTests.FindType("PortalCheckBuildIdentity");
            var read = type.GetMethod("ReadIdentity");
            string project = Path.GetFullPath(Application.dataPath + "/..");
            string temporary = "Assets/LabTools/Tests/Digest-" + Guid.NewGuid().ToString("N") + ".txt";
            Assert.That(File.Exists(temporary), Is.False);
            object[] args = { "Color", GitHead(), project, "digest-test", Path.Combine(project, "Logs/digest-test") };
            try
            {
                object before = read.Invoke(null, args);
                Assert.That(PortalCheckPolicyTests.Field(before, "hdrpVersion"), Is.EqualTo("17.5.0"));
                File.WriteAllText(temporary, "first source revision");
                object first = read.Invoke(null, args);
                File.WriteAllText(temporary, "second source revision");
                object second = read.Invoke(null, args);
                Assert.That(PortalCheckPolicyTests.Field(first, "dirty"), Is.True);
                Assert.That(PortalCheckPolicyTests.Field(first, "sourceDigest"),
                    Is.Not.EqualTo(PortalCheckPolicyTests.Field(before, "sourceDigest")));
                Assert.That(PortalCheckPolicyTests.Field(second, "sourceDigest"),
                    Is.Not.EqualTo(PortalCheckPolicyTests.Field(first, "sourceDigest")));
            }
            finally
            {
                File.Delete(temporary);
                if (File.Exists(temporary + ".meta")) AssetDatabase.DeleteAsset(temporary);
            }
        }

        [Test]
        public void BuildIdentityRejectsExpectedCommitFromAnotherCheckout()
        {
            Type type = LabSerializationTests.FindType("PortalCheckBuildIdentity");
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => type.GetMethod("ReadIdentity")
                .Invoke(null, new object[] { "Color", new string('0', 40),
                    Path.GetFullPath(Application.dataPath + "/.."), "test", Path.GetTempPath() }));
            Assert.That(error.InnerException, Is.TypeOf<UnityEditor.Build.BuildFailedException>());
        }

        [Test]
        public void UncleanCertifiedBuildIsRejectedBeforeSceneCacheCanBeUsed()
        {
            Type type = LabSerializationTests.FindType("PortalCheckBuildIdentity");
            var error = Assert.Throws<System.Reflection.TargetInvocationException>(() => type.GetMethod("RequireCleanBuild")
                .Invoke(null, new object[] { BuildOptions.Development, "Seam" }));
            Assert.That(error.InnerException, Is.TypeOf<UnityEditor.Build.BuildFailedException>());
        }

        [TestCase("Ghost")]
        [TestCase("Cross")]
        [TestCase("Rotate")]
        public void LegacyBuildCanStillCollectUnstructuredMetrics(string check)
        {
            Type type = LabSerializationTests.FindType("PortalCheckBuildIdentity");
            Assert.DoesNotThrow(() => type.GetMethod("RequireCleanBuild")
                .Invoke(null, new object[] { BuildOptions.Development, check }));
        }

        [Test]
        public void InjectedRunKeepsSerializedIdentityOnReopenWithoutDuplicatingComponent()
        {
            if (!Application.isBatchMode) Assert.Ignore("Scene round trip requires an isolated batchmode editor.");
            string path = "Assets/LabTools/Tests/Identity-" + Guid.NewGuid().ToString("N") + ".unity";
            Assert.That(File.Exists(path), Is.False);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                Type builder = LabSerializationTests.FindType("PortalCheckBuildIdentity");
                object identity = PortalCheckPolicyTests.Identity();
                var inject = builder.GetMethod("Inject");
                inject.Invoke(null, new object[] { scene, identity });
                inject.Invoke(null, new object[] { scene, identity });
                Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Type type = LabSerializationTests.FindType("Portals.Lab.Validation.PortalCheckRun");
                Component[] runs = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren(type, true)).ToArray();
                Assert.That(runs, Has.Length.EqualTo(1));
                Assert.That(EditorUtility.IsPersistent(MonoScript.FromMonoBehaviour((MonoBehaviour)runs[0])), Is.True);
                var serialized = new SerializedObject(runs[0]);
                Assert.That(serialized.FindProperty("identity.runId").stringValue, Is.EqualTo("task0b-test"));
                Assert.That(serialized.FindProperty("identity.commit").stringValue,
                    Is.EqualTo("b50fb09d3c4db024443562dc350ac10f7b4669a2"));
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
