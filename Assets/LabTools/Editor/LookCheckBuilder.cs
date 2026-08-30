using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Reproduction for "the view does not rotate when the portals are at an angle": a controller
/// that stores yaw itself and rewrites the transforms every frame. Two perpendicular portals,
/// so a correct transition must turn the view by 90 degrees.
/// </summary>
public static class LookCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/LookCheck.unity";

    public static void BuildPlayer()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateEnvironment();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");

        var portalAObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalAObject.name = "Portal_A";
        portalAObject.transform.SetPositionAndRotation(
            new Vector3(0f, 1.6f, 0f),
            Quaternion.Euler(0f, 180f, 0f));

        // Perpendicular to A: the exit view must be rotated 90 degrees from the entrance.
        var portalBObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalBObject.name = "Portal_B";
        portalBObject.transform.SetPositionAndRotation(
            new Vector3(30f, 1.6f, 0f),
            Quaternion.Euler(0f, 90f, 0f));

        Portal portalA = portalAObject.GetComponent<Portal>();
        Portal portalB = portalBObject.GetComponent<Portal>();
        portalA.exitPortal = portalB;
        portalB.exitPortal = portalA;

        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0.1f, -6f);
        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        PortalTraveller traveller = player.AddComponent<PortalTraveller>();

        var cameraObject = new GameObject("PlayerCamera");
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        Camera playerCamera = cameraObject.AddComponent<Camera>();
        playerCamera.tag = "MainCamera";
        cameraObject.AddComponent<HDAdditionalCameraData>();

        // The controller that fights the teleport.
        UHFPS.Runtime.LookController look = player.AddComponent<UHFPS.Runtime.LookController>();
        UHFPS.Runtime.PlayerStateMachine machine = player.AddComponent<UHFPS.Runtime.PlayerStateMachine>();
        look.body = player.transform;
        look.head = cameraObject.transform;
        look.LookRotation = Vector2.zero;
        look.PlayerForward = UHFPS.Runtime.LookController.ForwardStyle.LookForward;

        // The bridge under test: it must find the UHFPS look controller and state machine by
        // reflection, turn the stored yaw, and turn the stored velocity.
        PortalCameraBridge bridge = player.AddComponent<PortalCameraBridge>();
        SerializedObject bridgeObject = new SerializedObject(bridge);
        bridgeObject.FindProperty("traveller").objectReferenceValue = traveller;
        bridgeObject.FindProperty("gameplayCamera").objectReferenceValue = playerCamera;
        bridgeObject.ApplyModifiedPropertiesWithoutUndo();

        UhfpsProbe probe = player.AddComponent<UhfpsProbe>();
        probe.traveller = traveller;
        probe.look = look;
        probe.machine = machine;

        SerializedObject travellerObject = new SerializedObject(traveller);
        travellerObject.FindProperty("viewPoint").objectReferenceValue = cameraObject.transform;
        travellerObject.ApplyModifiedPropertiesWithoutUndo();

        portalA.playerCamera = playerCamera;
        portalB.playerCamera = playerCamera;

        var captureObject = new GameObject("LabCapture");
        LabCapture capture = captureObject.AddComponent<LabCapture>();
        capture.playerRoot = player.transform;
        capture.playerCamera = playerCamera;
        capture.portalScreen = portalA.screen;
        capture.traveller = traveller;
        capture.outputDirectory = "LookCheck";
        capture.walkStart = new Vector3(0f, 0.1f, -3f);
        capture.shots = new[]
        {
            new LabCapture.Shot { name = "before", position = new Vector3(0f, 0.1f, -6f) }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildLookCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "LookCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[LookCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static void CreateEnvironment()
    {
        var sunObject = new GameObject("Sun");
        sunObject.transform.rotation = Quaternion.Euler(45f, 40f, 0f);
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sunObject.AddComponent<HDAdditionalLightData>().SetIntensity(12000f, LightUnit.Lux);

        var volumeObject = new GameObject("Sky and Fog Volume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            "Assets/Settings/SkyandFogSettingsProfile.asset");

        Material grey = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoStone.mat");
        Material teal = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoTeal.mat");

        GameObject groundA = GameObject.CreatePrimitive(PrimitiveType.Cube);
        groundA.name = "Ground_A";
        groundA.transform.position = new Vector3(0f, -0.5f, -6f);
        groundA.transform.localScale = new Vector3(20f, 1f, 20f);
        groundA.GetComponent<MeshRenderer>().sharedMaterial = grey;

        GameObject groundB = GameObject.CreatePrimitive(PrimitiveType.Cube);
        groundB.name = "Ground_B";
        groundB.transform.position = new Vector3(36f, -0.5f, 0f);
        groundB.transform.localScale = new Vector3(20f, 1f, 20f);
        groundB.GetComponent<MeshRenderer>().sharedMaterial = grey;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "Marker";
        marker.transform.position = new Vector3(34f, 1.5f, 3f);
        marker.transform.localScale = new Vector3(1f, 3f, 1f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = teal;
    }
}
