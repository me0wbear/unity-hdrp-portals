using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Exercises the editor automation the way a person would: build a scene with a player, invoke
/// "Portal Pair", drop two more portals in, then invoke "Wire Scene" and "Validate Scene".
/// Nothing here assigns exitPortal or playerCamera by hand.
/// </summary>
public static class AutoWireCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/AutoWireCheck.unity";

    public static void BuildPlayer()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateEnvironment();
        CreatePlayer(out Camera playerCamera, out PortalTraveller traveller);

        // Menu command 1: create a linked pair with the camera already assigned.
        PortalSetupTools.CreatePortalPair(null);

        // Two more portals dropped in raw, with nothing wired, to be picked up by Wire Scene.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");
        var extraA = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        extraA.name = "Portal_C";
        extraA.transform.SetPositionAndRotation(new Vector3(6f, 1.6f, 0f), Quaternion.Euler(0f, 180f, 0f));

        var extraB = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        extraB.name = "Portal_D";
        extraB.transform.SetPositionAndRotation(new Vector3(16f, 1.6f, 0f), Quaternion.Euler(0f, 180f, 0f));

        // Menu command 2: pair up whatever is unpaired and give everything the same camera.
        PortalSetupTools.WireScene();

        // Menu command 3: report anything still missing.
        PortalSetupTools.ValidateScene();

        ReportWiring();

        var captureObject = new GameObject("LabCapture");
        LabCapture capture = captureObject.AddComponent<LabCapture>();
        capture.playerRoot = playerCamera.transform.parent;
        capture.playerCamera = playerCamera;
        capture.traveller = traveller;
        capture.outputDirectory = "AutoWireCheck";
        capture.walkStart = new Vector3(0f, 0.1f, -6f);

        Portal firstPortal = Object.FindFirstObjectByType<Portal>();
        capture.portalScreen = firstPortal != null ? firstPortal.screen : null;
        capture.shots = new[]
        {
            new LabCapture.Shot { name = "wired", position = new Vector3(0f, 0.1f, -6f) }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildAutoWire");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "AutoWire.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[AutoWireCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static void ReportWiring()
    {
        Portal[] portals = Object.FindObjectsByType<Portal>(FindObjectsSortMode.InstanceID);
        for (int i = 0; i < portals.Length; i++)
        {
            Portal portal = portals[i];
            Debug.Log("[AutoWireCheck] " + portal.name
                + " exit=" + (portal.exitPortal != null ? portal.exitPortal.name : "NONE")
                + " camera=" + (portal.playerCamera != null ? portal.playerCamera.name : "NONE")
                + " screen=" + (portal.screen != null ? "set" : "NONE")
                + " pos=" + portal.transform.position.ToString("F1"));
        }
    }

    private static void CreatePlayer(out Camera camera, out PortalTraveller traveller)
    {
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0.1f, -6f);

        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        traveller = player.AddComponent<PortalTraveller>();

        var cameraObject = new GameObject("PlayerCamera");
        cameraObject.transform.SetParent(player.transform, false);
        cameraObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);
        camera = cameraObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        cameraObject.AddComponent<HDAdditionalCameraData>();

        SerializedObject travellerObject = new SerializedObject(traveller);
        travellerObject.FindProperty("viewPoint").objectReferenceValue = cameraObject.transform;
        travellerObject.ApplyModifiedPropertiesWithoutUndo();
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

        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
        ground.name = "Ground";
        ground.transform.position = new Vector3(6f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(50f, 1f, 30f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = grey;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "Marker";
        marker.transform.position = new Vector3(13f, 1.5f, 3f);
        marker.transform.localScale = new Vector3(1f, 3f, 1f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = teal;
    }
}
