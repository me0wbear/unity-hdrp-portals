using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Portals.Lab.Tests
{
    public sealed class LabSerializationTests
    {
        internal static Type FindType(string name)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(name)).FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, "Required implementation type: " + name);
            return type;
        }

        [TestCase("UHFPS.Runtime.LookController")]
        [TestCase("UHFPS.Runtime.PlayerStateMachine")]
        public void LabStubHasPersistentMonoScript(string name)
        {
            var go = new GameObject("ScriptPersistenceTest");
            try
            {
                var component = (MonoBehaviour)go.AddComponent(FindType(name));
                MonoScript script = MonoScript.FromMonoBehaviour(component);
                Assert.That(script, Is.Not.Null);
                Assert.That(EditorUtility.IsPersistent(script), Is.True, name);
                Assert.That(AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string guid,
                    out long localId), Is.True);
                Assert.That(guid, Is.Not.Empty);
                Assert.That(script.GetClass(), Is.EqualTo(component.GetType()));
            }
            finally { UnityEngine.Object.DestroyImmediate(go); }
        }

        [Test]
        public void SavedPlayerRetainsMachineAndMotionReferenceAfterReopen()
        {
            if (!Application.isBatchMode) Assert.Ignore("Scene round trip requires an isolated batchmode editor.");
            string path = "Assets/LabTools/Tests/Serialization-" + Guid.NewGuid().ToString("N") + ".unity";
            Assert.That(System.IO.File.Exists(path), Is.False);
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            try
            {
                var player = new GameObject("Player");
                SceneManager.MoveGameObjectToScene(player, scene);
                Component machine = player.AddComponent(FindType("UHFPS.Runtime.PlayerStateMachine"));
                machine.GetType().GetField("Motion").SetValue(machine, new Vector3(1f, 2f, 3f));
                Component check = player.AddComponent(FindType("SeamCheck"));
                check.GetType().GetField("machine").SetValue(check, machine);
                Assert.That(EditorSceneManager.SaveScene(scene, path), Is.True);
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                Component savedCheck = scene.GetRootGameObjects()[0].GetComponent(FindType("SeamCheck"));
                var savedMachine = (Component)savedCheck.GetType().GetField("machine").GetValue(savedCheck);
                Assert.That(savedMachine, Is.Not.Null, "Seam machine reference must survive scene serialization.");
                Assert.That(savedMachine.GetType().GetField("Motion").GetValue(savedMachine),
                    Is.EqualTo(new Vector3(1f, 2f, 3f)));
                Assert.That(EditorUtility.IsPersistent(MonoScript.FromMonoBehaviour((MonoBehaviour)savedMachine)),
                    Is.True);
            }
            finally
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                AssetDatabase.DeleteAsset(path);
            }
        }
    }
}
