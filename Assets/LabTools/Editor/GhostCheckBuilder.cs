using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Same geometry, same motion, seen through a portal and seen directly, with temporal
/// antialiasing on and off. Grading is global here so the only variable left is how the frame is
/// reconstructed over time.
/// </summary>
public static class GhostCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/GhostCheck.unity";
    private const string ProfileDirectory = "Assets/LabTools/Profiles";

    public static void BuildPlayer()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sunObject = new GameObject("Sun");
        sunObject.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sunObject.AddComponent<HDAdditionalLightData>().SetIntensity(20000f, LightUnit.Lux);

        CreateGlobalVolume();

        Material grey = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoStone.mat");
        Material teal = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoTeal.mat");

        BuildFloor(0f, grey);
        BuildFloor(30f, grey);
        BuildDetail(30f, grey, teal);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");

        var portalAObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalAObject.name = "Portal_A";
        portalAObject.transform.SetPositionAndRotation(
            new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 180f, 0f));

        var portalBObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalBObject.name = "Portal_B";
        portalBObject.transform.SetPositionAndRotation(
            new Vector3(30f, 1.6f, 0f), Quaternion.identity);

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
        cameraObject.transform.localPosition = new Vector3(0f, 1.5f, 0f);
        Camera playerCamera = cameraObject.AddComponent<Camera>();
        playerCamera.tag = "MainCamera";
        cameraObject.AddComponent<HDAdditionalCameraData>();

        SerializedObject travellerObject = new SerializedObject(traveller);
        travellerObject.FindProperty("viewPoint").objectReferenceValue = cameraObject.transform;
        travellerObject.ApplyModifiedPropertiesWithoutUndo();

        portalA.playerCamera = playerCamera;
        portalB.playerCamera = playerCamera;

        var checkObject = new GameObject("GhostCheck");
        GhostCheck check = checkObject.AddComponent<GhostCheck>();
        check.playerRoot = player.transform;
        check.playerCamera = playerCamera;
        check.traveller = traveller;
        check.portalObjects = new[] { portalAObject, portalBObject };
        check.portals = new[] { portalA, portalB };
        // Sideways, not forwards. Forward motion produces almost no screen displacement at the
        // centre of the frame, which is exactly where the comparison samples, so it would measure
        // the one place a depth-based reprojection error cannot show up.
        check.from = new Vector3(-0.4f, 0.1f, -3f);
        check.to = new Vector3(0.4f, 0.1f, -3f);
        check.motionFrames = 16;
        check.sampleFraction = 0.14f;
        check.directOffset = 30f;

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildGhostCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "GhostCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[GhostCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static void BuildFloor(float x, Material material)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor_" + x;
        floor.transform.position = new Vector3(x, -0.5f, 0f);
        floor.transform.localScale = new Vector3(24f, 1f, 40f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void BuildDetail(float x, Material floorMaterial, Material accentMaterial)
    {
        // Fine, high-contrast structure: ghosting shows up as a loss of edge contrast, so the
        // measurement needs edges to lose.
        for (int i = 0; i < 12; i++)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar_" + i;
            bar.transform.position = new Vector3(x - 1.1f + i * 0.2f, 1.6f, 6f);
            bar.transform.localScale = new Vector3(0.08f, 2.6f, 0.08f);
            bar.GetComponent<MeshRenderer>().sharedMaterial =
                (i & 1) == 0 ? accentMaterial : floorMaterial;
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall";
        wall.transform.position = new Vector3(x, 3f, 9f);
        wall.transform.localScale = new Vector3(24f, 6f, 1f);
        wall.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;

        // A small bright source, which is where a temporal artefact reads most obviously.
        var lightObject = new GameObject("Spark");
        lightObject.transform.position = new Vector3(x, 1.6f, 5f);
        Light spark = lightObject.AddComponent<Light>();
        spark.type = LightType.Point;
        spark.color = new Color(1f, 0.95f, 0.8f);
        HDAdditionalLightData data = lightObject.AddComponent<HDAdditionalLightData>();
        data.SetIntensity(600f, LightUnit.Lumen);
        data.range = 12f;
    }

    private static void CreateGlobalVolume()
    {
        Directory.CreateDirectory(ProfileDirectory);
        string path = ProfileDirectory + "/GhostCheckGlobal.asset";
        AssetDatabase.DeleteAsset(path);
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        Exposure exposure = AddOverride<Exposure>(profile);
        exposure.mode.overrideState = true;
        exposure.mode.value = ExposureMode.Fixed;
        exposure.fixedExposure.overrideState = true;
        exposure.fixedExposure.value = 11f;

        Tonemapping tonemapping = AddOverride<Tonemapping>(profile);
        tonemapping.mode.overrideState = true;
        tonemapping.mode.value = TonemappingMode.ACES;

        AssetDatabase.SaveAssets();

        var volumeObject = new GameObject("Global Volume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }

    private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        T component = ScriptableObject.CreateInstance<T>();
        component.name = typeof(T).Name;
        component.hideFlags = HideFlags.HideInHierarchy;
        profile.components.Add(component);
        AssetDatabase.AddObjectToAsset(component, profile);
        EditorUtility.SetDirty(profile);
        return component;
    }
}
