using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Two rooms that are graded and fogged differently, joined by a portal pair whose transform
/// makes the portal camera stand exactly where the player stands for the direct shot. Anything
/// that differs between "seen through the portal" and "seen directly" is a seam.
/// </summary>
public static class ColorCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/ColorCheck.unity";
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

        // Room A is warm and red-fogged, room B is cold and blue-fogged. The two boxes touch but
        // do not overlap, so each camera sits unambiguously in one of them.
        CreateRoomVolume("Room_A_Volume", new Vector3(0f, 1.6f, 0f),
            new Color(1f, 0.55f, 0.30f), new Color(0.75f, 0.18f, 0.10f));
        CreateRoomVolume("Room_B_Volume", new Vector3(30f, 1.6f, 0f),
            new Color(0.30f, 0.55f, 1f), new Color(0.10f, 0.20f, 0.80f));

        Material grey = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoStone.mat");
        Material teal = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoTeal.mat");

        // Room A is deliberately bare. Everything the centre of the frame lands on therefore
        // arrives through the portal, so the measurement cannot accidentally sample room A.
        BuildRoom(0f, grey, teal, false);
        BuildRoom(30f, grey, teal, true);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");

        var portalAObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalAObject.name = "Portal_A";
        portalAObject.transform.SetPositionAndRotation(
            new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 180f, 0f));

        // Same orientation as A, so the mapped camera pose is a pure 30 m translation and the
        // portal view and the direct view are the same view.
        var portalBObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalBObject.name = "Portal_B";
        portalBObject.transform.SetPositionAndRotation(
            new Vector3(30f, 1.6f, 0f), Quaternion.identity);

        Portal portalA = portalAObject.GetComponent<Portal>();
        Portal portalB = portalBObject.GetComponent<Portal>();
        portalA.exitPortal = portalB;
        portalB.exitPortal = portalA;

        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0.1f, -3f);
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

        var checkObject = new GameObject("ColorCheck");
        ColorCheck check = checkObject.AddComponent<ColorCheck>();
        check.playerRoot = player.transform;
        check.playerCamera = playerCamera;
        check.traveller = traveller;
        check.portalObjects = new[] { portalAObject, portalBObject };

        // Signed distance to portal A is -z, so the approach steps read 3.0 m down to 0.02 m.
        // crossBefore and crossAfter are the two frames the player actually sees either side of
        // the teleport: four centimetres apart, so they must look the same.
        check.steps = new[]
        {
            Step("farThrough", -3f, true),
            new ColorCheck.Step
            {
                name = "farDirect", pose = new Vector3(30f, 0.1f, -3f), portalsEnabled = false
            },
            Step("approach20", -2f, true),
            Step("approach15", -1.5f, true),
            Step("approach10", -1f, true),
            Step("approach05", -0.5f, true),
            Step("crossBefore", -0.02f, true),
            new ColorCheck.Step
            {
                name = "crossAfter", pose = new Vector3(30f, 0.1f, 0.02f), portalsEnabled = true
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildColorCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "ColorCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[ColorCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static ColorCheck.Step Step(string name, float z, bool portalsEnabled)
    {
        return new ColorCheck.Step
        {
            name = name,
            pose = new Vector3(0f, 0.1f, z),
            portalsEnabled = portalsEnabled
        };
    }

    private static void BuildRoom(float x, Material floorMaterial, Material accentMaterial, bool withDetail)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor_" + x;
        floor.transform.position = new Vector3(x, -0.5f, 0f);
        floor.transform.localScale = new Vector3(24f, 1f, 40f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;

        if (!withDetail)
        {
            return;
        }

        // Detail straight ahead of the viewpoint, which is what the centre sample lands on.
        for (int i = 0; i < 3; i++)
        {
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = "Pillar_" + x + "_" + i;
            pillar.transform.position = new Vector3(x - 1f + i, 1.2f, 5f + i * 2f);
            pillar.transform.localScale = new Vector3(0.8f, 2.4f, 0.8f);
            pillar.GetComponent<MeshRenderer>().sharedMaterial =
                i == 1 ? accentMaterial : floorMaterial;
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall_" + x;
        wall.transform.position = new Vector3(x, 3f, 14f);
        wall.transform.localScale = new Vector3(24f, 6f, 1f);
        wall.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
    }

    private static void CreateGlobalVolume()
    {
        VolumeProfile profile = NewProfile("ColorCheckGlobal");

        // Fixed exposure: automatic adaptation would drift between the two shots and hide the
        // difference the test is measuring.
        Exposure exposure = AddOverride<Exposure>(profile);
        exposure.mode.overrideState = true;
        exposure.mode.value = ExposureMode.Fixed;
        exposure.fixedExposure.overrideState = true;
        exposure.fixedExposure.value = 11f;

        Tonemapping tonemapping = AddOverride<Tonemapping>(profile);
        tonemapping.mode.overrideState = true;
        tonemapping.mode.value = TonemappingMode.ACES;

        ScreenSpaceReflection reflection = AddOverride<ScreenSpaceReflection>(profile);
        reflection.enabled.overrideState = true;
        reflection.enabled.value = true;

        ScreenSpaceAmbientOcclusion occlusion = AddOverride<ScreenSpaceAmbientOcclusion>(profile);
        occlusion.intensity.overrideState = true;
        occlusion.intensity.value = 0.8f;

        AssetDatabase.SaveAssets();

        var volumeObject = new GameObject("Global Volume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.sharedProfile = profile;
    }

    private static void CreateRoomVolume(string name, Vector3 centre, Color filter, Color fogColour)
    {
        VolumeProfile profile = NewProfile(name);

        ColorAdjustments adjustments = AddOverride<ColorAdjustments>(profile);
        adjustments.colorFilter.overrideState = true;
        adjustments.colorFilter.value = filter;

        Fog fog = AddOverride<Fog>(profile);
        fog.enabled.overrideState = true;
        fog.enabled.value = true;
        fog.albedo.overrideState = true;
        fog.albedo.value = fogColour;
        fog.meanFreePath.overrideState = true;
        fog.meanFreePath.value = 25f;

        AssetDatabase.SaveAssets();

        var volumeObject = new GameObject(name);
        volumeObject.transform.position = centre;
        var collider = volumeObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = new Vector3(28f, 20f, 60f);

        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = false;
        volume.blendDistance = 0f;
        volume.priority = 1f;
        volume.sharedProfile = profile;
    }

    private static VolumeProfile NewProfile(string name)
    {
        Directory.CreateDirectory(ProfileDirectory);
        string path = ProfileDirectory + "/" + name + ".asset";
        AssetDatabase.DeleteAsset(path);
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);
        return profile;
    }

    /// <summary>
    /// VolumeProfile.Add creates the component in memory only. Without adding it as a sub-asset
    /// the profile saves empty and every override on it silently does nothing.
    /// </summary>
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
