using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Verifies the shipped prefab is usable as-is: drag it in twice, place it, link the pair,
/// assign the camera. Nothing else is configured. If this scene works, the prefab carries
/// everything it should and the integration instructions are correct.
/// </summary>
public static class PrefabCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/PrefabCheck.unity";

    public static void BuildPlayer()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateEnvironment();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");
        if (prefab == null)
        {
            Debug.LogError("[PrefabCheck] Assets/portal/Portal.prefab not found");
            EditorApplication.Exit(1);
            return;
        }

        // "Перетащите префаб в сцену" — twice, then place each one.
        var portalAObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalAObject.name = "Portal_A";
        portalAObject.transform.SetPositionAndRotation(
            new Vector3(0f, 1.5f, 0f),
            Quaternion.Euler(0f, 180f, 0f));

        var portalBObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalBObject.name = "Portal_B";
        portalBObject.transform.SetPositionAndRotation(
            new Vector3(30f, 1.5f, 0f),
            Quaternion.Euler(0f, 90f, 0f));

        Portal portalA = portalAObject.GetComponent<Portal>();
        Portal portalB = portalBObject.GetComponent<Portal>();

        // "Свяжите пару в обе стороны."
        portalA.exitPortal = portalB;
        portalB.exitPortal = portalA;

        // The player, exactly as the instructions describe.
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
        capture.outputDirectory = "PrefabCheck";
        capture.shots = new[]
        {
            new LabCapture.Shot { name = "far", position = new Vector3(0f, 0.1f, -6f) },
            new LabCapture.Shot { name = "touching", position = new Vector3(0f, 0.1f, -0.1f) }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildPrefabCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "PrefabCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[PrefabCheck] build result=" + report.summary.result);
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

        Box("Ground_A", new Vector3(0f, -0.5f, -6f), new Vector3(20f, 1f, 20f), grey);
        Box("Ground_B", new Vector3(36f, -0.5f, 0f), new Vector3(20f, 1f, 20f), grey);
        Box("Marker", new Vector3(34f, 1.5f, 2f), new Vector3(1f, 3f, 1f), teal);
    }

    private static void Box(string name, Vector3 position, Vector3 size, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.position = position;
        box.transform.localScale = size;
        if (material != null)
        {
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
        }
    }
}
