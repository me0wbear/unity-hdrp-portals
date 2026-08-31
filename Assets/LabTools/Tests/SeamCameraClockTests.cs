using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace Portals.Lab.Tests
{
    public sealed class SeamCameraClockTests
    {
        private const float Step = 1f / 60f;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private readonly List<GameObject> objects = new List<GameObject>();
        private float previousDelta;
        private ICinemachineCamera previousSolo;

        [SetUp]
        public void SetUp()
        {
            previousDelta = CinemachineCore.UniformDeltaTimeOverride;
            previousSolo = CinemachineCore.SoloCamera;
            CinemachineCore.SoloCamera = null;
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = objects.Count - 1; i >= 0; i--)
                if (objects[i] != null) UnityEngine.Object.DestroyImmediate(objects[i]);
            objects.Clear();
            CinemachineCore.UniformDeltaTimeOverride = previousDelta;
            CinemachineCore.SoloCamera = previousSolo;
        }

        private GameObject Create(string name)
        {
            var go = new GameObject(name);
            objects.Add(go);
            return go;
        }

        private Component Rig(out Camera camera, out CinemachineBrain brain, out Transform target)
        {
            target = Create("ClockTarget").transform;
            camera = Create("ClockCamera").AddComponent<Camera>();
            camera.enabled = false;
            brain = camera.gameObject.AddComponent<CinemachineBrain>();
            brain.ChannelMask = (OutputChannels)(1 << 30);
            var vcam = Create("ClockVirtualCamera").AddComponent<CinemachineCamera>();
            vcam.OutputChannel = brain.ChannelMask;
            vcam.Follow = target;
            var follow = vcam.gameObject.AddComponent<CinemachineFollow>();
            follow.FollowOffset = Vector3.zero;
            follow.TrackerSettings.PositionDamping = Vector3.one * 0.2f;
            vcam.gameObject.AddComponent<CinemachineRotateWithFollowTarget>();
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            brain.ManualUpdate(0, Step);
            Assert.That(brain.ActiveVirtualCamera, Is.EqualTo(vcam), "The real Brain+Follow fixture must be live.");
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.SmartUpdate;
            return Create("SeamClock").AddComponent(LabSerializationTests.FindType("SeamCheck"));
        }

        // Runtime-only MonoBehaviour нельзя вызывать через SendMessage в EditMode.
        // Отсутствующий обработчик, как у DontRequireReceiver, оставляет состояние неизменным.
        private static void Message(Component seam, string name, params object[] args) =>
            seam.GetType().GetMethod(name, PrivateInstance)?.Invoke(seam, args);

        private static void Begin(Component seam, Camera camera) =>
            Message(seam, "StartCameraClock", camera);

        private static void Tick(Component seam, int frame) =>
            Message(seam, "AdvanceCameraClock", frame);

        private static void Set(Component seam, string field, object value) =>
            seam.GetType().GetField(field).SetValue(seam, value);

        private static Dictionary<string, string> Row(Component seam)
        {
            var csv = (StringBuilder)seam.GetType().GetField("_csv", PrivateInstance).GetValue(seam);
            csv.Append("0,NaN,0.2,0,0");
            Message(seam, "AppendCameraPose");
            string[] lines = csv.ToString().TrimEnd('\n').Split('\n');
            string[] names = lines[0].Split(',');
            string[] values = lines[lines.Length - 1].Split(',');
            Assert.That(values.Length, Is.EqualTo(names.Length), "CSV header and capture row must agree.");
            var row = new Dictionary<string, string>();
            for (int i = 0; i < names.Length; i++) row.Add(names[i], values[i]);
            Assert.That(row.ContainsKey("cameraTick"), Is.True, "Each capture needs its simulated camera tick.");
            return row;
        }

        private static double Value(Dictionary<string, string> row, string key) =>
            double.Parse(row[key], CultureInfo.InvariantCulture);

        private Vector3[] Trajectory(bool fixedClock, float ambientDelta)
        {
            Component seam = Rig(out Camera camera, out CinemachineBrain brain, out Transform target);
            if (fixedClock) Begin(seam, camera);
            else brain.UpdateMethod = CinemachineBrain.UpdateMethods.ManualUpdate;
            var positions = new Vector3[24];
            for (int i = 0; i < positions.Length; i++)
            {
                CinemachineCore.UniformDeltaTimeOverride = ambientDelta * (i % 2 == 0 ? 1f : 1.5f);
                float ambient = CinemachineCore.UniformDeltaTimeOverride;
                target.SetPositionAndRotation(new Vector3(0f, 0f, (i + 1) * 0.05f),
                    Quaternion.Euler(0f, i * 2f, 0f));
                if (fixedClock) Tick(seam, 100 + i);
                else brain.ManualUpdate(i + 1, ambient);
                positions[i] = camera.transform.position;
                Assert.That(CinemachineCore.UniformDeltaTimeOverride, Is.EqualTo(ambient), "Do not leak clock overrides.");
            }
            Assert.That(positions[23].z, Is.GreaterThan(0.1f), "The gameplay camera must actually advance.");
            Assert.That(target.position.z - positions[23].z, Is.GreaterThan(0.01f), "Position damping must retain lag.");
            Assert.That(camera.transform.rotation.eulerAngles.y, Is.EqualTo(46f).Within(0.001f));
            Message(seam, "StopCameraClock");
            return positions;
        }

        [Test]
        public void AmbientDeltaControlChangesRealDampedTrajectory()
        {
            Vector3[] fast = Trajectory(false, 0.005f);
            Vector3[] slow = Trajectory(false, 0.05f);
            Assert.That(Vector3.Distance(fast[23], slow[23]), Is.GreaterThan(0.05f));
        }

        [Test]
        public void SeamClockIgnoresAmbientDeltaAndPreservesDamping()
        {
            Vector3[] fast = Trajectory(true, 0.005f);
            Vector3[] slow = Trajectory(true, 0.05f);
            for (int i = 0; i < fast.Length; i++)
                Assert.That(Vector3.Distance(fast[i], slow[i]), Is.LessThan(0.00001f), "Camera trajectory at step " + i);
        }

        [Test]
        public void LateUpdateDrivesOneCameraTickForTheCurrentFrame()
        {
            Component seam = Rig(out Camera camera, out _, out _);
            Begin(seam, camera);
            Message(seam, "LateUpdate");
            Message(seam, "LateUpdate");
            Assert.That(Value(Row(seam), "cameraTick"), Is.EqualTo(1));
        }

        [Test]
        public void CameraAdvancesOnlyOncePerFrameAndCountsStepsNotFrameGaps()
        {
            Component seam = Rig(out Camera camera, out _, out Transform target);
            Begin(seam, camera);
            Tick(seam, 100);
            target.position = Vector3.forward;
            Tick(seam, 101);
            Vector3 once = camera.transform.position;
            target.position = Vector3.forward * 2f;
            Tick(seam, 101);
            Assert.That(camera.transform.position, Is.EqualTo(once));
            Assert.That(Value(Row(seam), "cameraTick"), Is.EqualTo(2));
            Tick(seam, 104);
            Assert.That(camera.transform.position.z, Is.GreaterThan(once.z));
            var row = Row(seam);
            Assert.That(Value(row, "cameraTick"), Is.EqualTo(3));
            Assert.That(Value(row, "cameraSimulatedTime"), Is.EqualTo(3d * Step).Within(1e-9));
        }

        [TestCase("StopCameraClock")]
        [TestCase("OnDisable")]
        public void ManualClockRestoresBothFormerBrainModes(string stopMessage)
        {
            Component seam = Rig(out Camera camera, out CinemachineBrain brain, out _);
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.FixedUpdate;
            brain.BlendUpdateMethod = CinemachineBrain.BrainUpdateMethods.FixedUpdate;
            Begin(seam, camera);
            Assert.That(brain.UpdateMethod, Is.EqualTo(CinemachineBrain.UpdateMethods.ManualUpdate));
            Assert.That(brain.BlendUpdateMethod, Is.EqualTo(CinemachineBrain.BrainUpdateMethods.LateUpdate));
            Begin(seam, camera);
            Message(seam, stopMessage);
            Assert.That(brain.UpdateMethod, Is.EqualTo(CinemachineBrain.UpdateMethods.FixedUpdate));
            Assert.That(brain.BlendUpdateMethod, Is.EqualTo(CinemachineBrain.BrainUpdateMethods.FixedUpdate));
            Tick(seam, 100);
            Assert.That(Value(Row(seam), "cameraTick"), Is.Zero, "Stopped clocks must not advance.");
        }

        [Test]
        public void ClockOrderIsStrictlyBetweenTravellerAndPortalSystem()
        {
            Type seam = LabSerializationTests.FindType("SeamCheck");
            Type traveller = LabSerializationTests.FindType("PortalTraveller");
            Type system = LabSerializationTests.FindType("PortalSystem");
            int Order(Type type) => type.GetCustomAttribute<DefaultExecutionOrder>()?.order ?? 0;
            Assert.That(Order(seam), Is.GreaterThan(Order(traveller)).And.LessThan(Order(system)));
            int ImportOrder(string path)
            {
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                Assert.That(script, Is.Not.Null);
                int explicitOrder = MonoImporter.GetExecutionOrder(script);
                // Нулевой importer order оставляет DefaultExecutionOrder действующим.
                return explicitOrder == 0 ? Order(script.GetClass()) : explicitOrder;
            }
            Assert.That(ImportOrder("Assets/LabTools/Runtime/SeamCheck.cs"),
                Is.GreaterThan(ImportOrder("Assets/portal/Runtime/PortalTraveller.cs"))
                    .And.LessThan(ImportOrder("Assets/portal/Runtime/PortalSystem.cs")));
        }

        [Test]
        public void CsvPreservesMetricsAndRecordsActualGameplayPoseInvariantly()
        {
            Component seam = Rig(out Camera camera, out _, out Transform target);
            Begin(seam, camera);
            Tick(seam, 1);
            target.position = new Vector3(90f, 80f, 70f);
            camera.transform.SetPositionAndRotation(new Vector3(1.25f, 2.5f, 3.75f), Quaternion.Euler(12f, 34f, 56f));
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
                var row = Row(seam);
                Assert.That(row["frame"], Is.EqualTo("0"));
                Assert.That(row["difference"], Is.EqualTo("NaN"));
                Assert.That(row["meanLuminance"], Is.EqualTo("0.2"));
                Assert.That(row["crossing"], Is.EqualTo("0"));
                Assert.That(row["crossingCount"], Is.EqualTo("0"));
                Assert.That(Value(row, "cameraTick"), Is.EqualTo(1));
                Assert.That(Value(row, "cameraPositionX"), Is.EqualTo(1.25));
                Assert.That(Value(row, "cameraPositionY"), Is.EqualTo(2.5));
                Assert.That(Value(row, "cameraPositionZ"), Is.EqualTo(3.75));
                Quaternion q = camera.transform.rotation;
                Assert.That(Value(row, "cameraRotationX"), Is.EqualTo(q.x).Within(1e-7));
                Assert.That(Value(row, "cameraRotationY"), Is.EqualTo(q.y).Within(1e-7));
                Assert.That(Value(row, "cameraRotationZ"), Is.EqualTo(q.z).Within(1e-7));
                Assert.That(Value(row, "cameraRotationW"), Is.EqualTo(q.w).Within(1e-7));
            }
            finally { CultureInfo.CurrentCulture = previous; }
        }

        [Test]
        public void StartupTicksWarmupSettleAndWalkWithoutResetAndRestoresOnDispose()
        {
            Component seam = Rig(out Camera camera, out CinemachineBrain brain, out Transform target);
            var player = Create("ClockPlayer");
            player.AddComponent<CharacterController>();
            Component traveller = player.AddComponent(LabSerializationTests.FindType("PortalTraveller"));
            Component machine = player.AddComponent(LabSerializationTests.FindType("UHFPS.Runtime.PlayerStateMachine"));
            Set(seam, "playerRoot", player.transform);
            Set(seam, "traveller", traveller);
            Set(seam, "machine", machine);
            Set(seam, "warmupFrames", 2);
            // Старый компонент не имеет ссылки: проверка ниже всё равно достигает реального Start.
            seam.GetType().GetField("gameplayCamera")?.SetValue(seam, camera);
            string output = Path.GetFullPath("Logs/task0b-review-fixes-fixture-" + Guid.NewGuid().ToString("N"));
            Assert.That(Directory.Exists(output), Is.False);
            Set(seam, "outputDirectory", output);
            var routine = (IEnumerator)seam.GetType().GetMethod("Start", PrivateInstance).Invoke(seam, null);
            try
            {
                for (int i = 0; i < 11; i++)
                {
                    Assert.That(routine.MoveNext(), Is.True);
                    Assert.That(routine.Current, Is.Null, "Warmup, settle, then the next movement frame.");
                    Tick(seam, 100 + i);
                }
                Assert.That(brain.UpdateMethod, Is.EqualTo(CinemachineBrain.UpdateMethods.ManualUpdate));
                Assert.That(routine.MoveNext(), Is.True);
                Assert.That(routine.Current, Is.TypeOf<WaitForEndOfFrame>());
                Tick(seam, 111);
                Assert.That(Value(Row(seam), "cameraTick"), Is.EqualTo(12));
            }
            finally { ((IDisposable)routine).Dispose(); }
            Assert.That(brain.UpdateMethod, Is.EqualTo(CinemachineBrain.UpdateMethods.SmartUpdate));
            Assert.That(brain.BlendUpdateMethod, Is.EqualTo(CinemachineBrain.BrainUpdateMethods.LateUpdate));
        }

        [Test]
        public void CameraWithoutBrainStillRecordsClockAndPose()
        {
            var seam = Create("SeamWithoutBrain").AddComponent(LabSerializationTests.FindType("SeamCheck"));
            var camera = Create("DirectCamera").AddComponent<Camera>();
            Begin(seam, camera);
            Tick(seam, 10);
            Assert.That(Value(Row(seam), "cameraTick"), Is.EqualTo(1));
        }
    }
}
