using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Checks that lights on the far side show up through the portal. Room B has no directional
/// light at all: everything visible there is lit by point lights, so if light culling drops
/// them the portal view goes black while the rest of the scene stays lit.
/// </summary>
public static class LightCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/LightCheck.unity";

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
        player.transform.position = new Vector3(0f, 0.1f, -5f);
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
        capture.outputDirectory = "LightCheck";
        capture.walkStart = new Vector3(0f, 0.1f, -5f);
        capture.shots = new[]
        {
            new LabCapture.Shot { name = "through", position = new Vector3(0f, 0.1f, -4f) },
            new LabCapture.Shot { name = "closer", position = new Vector3(0f, 0.1f, -1.5f) }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildLightCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "LightCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[LightCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static void CreateEnvironment()
    {
        // No directional light and no sky contribution worth speaking of: room B is lit only by
        // its point lights, which is exactly what the test is about.
        var volumeObject = new GameObject("Sky and Fog Volume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            "Assets/Settings/SkyandFogSettingsProfile.asset");

        // Volumetric fog makes the light itself visible as a cone in the air. Volumetrics rely
        // on temporal history, which the portal camera does not keep, so this is the case most
        // likely to look like "the lights are missing" through a portal.
        var fogObject = new GameObject("Volumetric Fog Volume");
        Volume fogVolume = fogObject.AddComponent<Volume>();
        fogVolume.isGlobal = true;
        fogVolume.priority = 5f;

        VolumeProfile fogProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(fogProfile, "Assets/LabTools/LightCheckFog.asset");

        Fog fog = fogProfile.Add<Fog>(true);
        fog.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(fog, fogProfile);
        fog.enabled.overrideState = true;
        fog.enabled.value = true;
        fog.enableVolumetricFog.overrideState = true;
        fog.enableVolumetricFog.value = true;
        fog.meanFreePath.overrideState = true;
        fog.meanFreePath.value = 25f;
        fog.baseHeight.overrideState = true;
        fog.baseHeight.value = 0f;
        fog.maximumHeight.overrideState = true;
        fog.maximumHeight.value = 12f;
        fog.albedo.overrideState = true;
        fog.albedo.value = Color.white;
        AssetDatabase.SaveAssets();

        fogVolume.sharedProfile = fogProfile;

        Material grey = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoStone.mat");
        Material teal = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoTeal.mat");
        Material rust = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoRust.mat");

        Box("Ground_A", new Vector3(0f, -0.5f, -5f), new Vector3(16f, 1f, 16f), grey);
        Lamp("Lamp_A", new Vector3(0f, 3f, -4f), new Color(1f, 0.9f, 0.75f), 8000f, 18f);

        // Room B: floor, two props, and two point lights close to them.
        Box("Ground_B", new Vector3(36f, -0.5f, 0f), new Vector3(18f, 1f, 18f), grey);
        Box("B_Column", new Vector3(34f, 1.5f, 2f), new Vector3(1f, 3f, 1f), teal);
        Box("B_Crate", new Vector3(33.5f, 0.6f, -1.5f), new Vector3(1.2f, 1.2f, 1.2f), rust);
        Box("B_Wall", new Vector3(40f, 2.5f, 0f), new Vector3(0.5f, 5f, 16f), grey);

        Lamp("Lamp_B1", new Vector3(34f, 3f, 0f), new Color(1f, 0.85f, 0.6f), 12000f, 22f);
        Lamp("Lamp_B2", new Vector3(37f, 2.5f, 3f), new Color(0.6f, 0.8f, 1f), 9000f, 20f);

        // Behind the exit plane (portal B faces +X, so this sits on the far side of it) but
        // lighting the floor in front of it. Culling by the oblique frustum drops this light
        // even though what it lights is plainly visible through the portal.
        Lamp("Lamp_B_Behind", new Vector3(28.5f, 2.5f, 0f), new Color(1f, 0.4f, 0.15f), 20000f, 26f);
    }

    private static void Lamp(string name, Vector3 position, Color color, float lumens, float range)
    {
        var lampObject = new GameObject(name);
        lampObject.transform.position = position;
        Light light = lampObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        HDAdditionalLightData data = lampObject.AddComponent<HDAdditionalLightData>();
        data.SetIntensity(lumens, LightUnit.Lumen);
        data.range = range;
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
