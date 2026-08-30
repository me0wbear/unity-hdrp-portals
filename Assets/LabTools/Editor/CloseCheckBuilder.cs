using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Reproduction case for portals placed close together: the exit is near enough that the
/// traveller lands back inside the entrance trigger, which can bounce it between the pair.
/// </summary>
public static class CloseCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/CloseCheck.unity";

    public static void BuildPlayer()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateEnvironment();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");

        // Only 3 metres apart and facing the same way: walking through A drops the traveller
        // right next to B, well inside the region where both triggers overlap the player.
        var portalAObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalAObject.name = "Portal_A";
        portalAObject.transform.SetPositionAndRotation(
            new Vector3(0f, 1.5f, 0f),
            Quaternion.Euler(0f, 180f, 0f));

        var portalBObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalBObject.name = "Portal_B";
        portalBObject.transform.SetPositionAndRotation(
            new Vector3(0f, 1.5f, ReadGap()),
            Quaternion.Euler(0f, 180f, 0f));

        Portal portalA = portalAObject.GetComponent<Portal>();
        Portal portalB = portalBObject.GetComponent<Portal>();
        portalA.exitPortal = portalB;
        portalB.exitPortal = portalA;

        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0.1f, -4f);
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
        capture.outputDirectory = "CloseCheck";
        capture.walkStart = new Vector3(0f, 0.1f, -4f);
        capture.shots = new[]
        {
            new LabCapture.Shot { name = "pair", position = new Vector3(0f, 0.1f, -4f) }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildCloseCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "CloseCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[CloseCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    /// <summary>Distance between the pair, so several spacings can be tested without edits.</summary>
    private static float ReadGap()
    {
        string raw = System.Environment.GetEnvironmentVariable("PORTAL_GAP");
        return float.TryParse(raw, System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float gap) ? gap : 3f;
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
        ground.transform.position = new Vector3(0f, -0.5f, 0f);
        ground.transform.localScale = new Vector3(30f, 1f, 30f);
        ground.GetComponent<MeshRenderer>().sharedMaterial = grey;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = "Marker";
        marker.transform.position = new Vector3(3f, 1.5f, 6f);
        marker.transform.localScale = new Vector3(1f, 3f, 1f);
        marker.GetComponent<MeshRenderer>().sharedMaterial = teal;
    }
}
