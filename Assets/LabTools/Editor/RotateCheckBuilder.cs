using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene for the "artefacts when turning near the portal" report. A saturated unlit marker sits
/// behind the exit portal, where the clip plane must hide it. If it ever reaches the screen, the
/// near plane stopped clipping.
/// </summary>
public static class RotateCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/RotateCheck.unity";
    private const string MarkerPath = "Assets/LabTools/Materials/LeakMarker.mat";

    public static void BuildPlayer()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sunObject = new GameObject("Sun");
        sunObject.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sunObject.AddComponent<HDAdditionalLightData>().SetIntensity(20000f, LightUnit.Lux);

        var volumeObject = new GameObject("Sky and Fog Volume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            "Assets/Settings/SkyandFogSettingsProfile.asset");

        Material grey = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoStone.mat");
        Material teal = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoTeal.mat");
        Material marker = CreateMarkerMaterial();

        BuildRoom(0f, grey, teal);
        BuildRoom(30f, grey, teal);

        // A thin slab just behind the exit plane. If the oblique near plane is dropped, the
        // fallback projection clips only at the camera near plane, and the band between that and
        // the exit becomes visible: this slab sits exactly in that band.
        GameObject leak = GameObject.CreatePrimitive(PrimitiveType.Cube);
        leak.name = "LeakMarker";
        leak.transform.position = new Vector3(30f, 1.6f, -0.2f);
        leak.transform.localScale = new Vector3(12f, 8f, 0.38f);
        leak.GetComponent<MeshRenderer>().sharedMaterial = marker;

        // Without this the two rooms simply see each other across open ground, and turning far
        // enough to the side shows the marker directly rather than through the portal.
        GameObject divider = GameObject.CreatePrimitive(PrimitiveType.Cube);
        divider.name = "Divider";
        divider.transform.position = new Vector3(15f, 10f, 4f);
        divider.transform.localScale = new Vector3(1f, 30f, 120f);
        divider.GetComponent<MeshRenderer>().sharedMaterial = grey;

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
        player.transform.position = new Vector3(0f, 0.1f, -1f);
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

        // Wide, like a first person controller, which is also the condition that makes the
        // oblique near plane hardest to condition.
        playerCamera.fieldOfView = 85f;
        playerCamera.nearClipPlane = 0.1f;
        cameraObject.AddComponent<HDAdditionalCameraData>();

        SerializedObject travellerObject = new SerializedObject(traveller);
        travellerObject.FindProperty("viewPoint").objectReferenceValue = cameraObject.transform;
        travellerObject.ApplyModifiedPropertiesWithoutUndo();

        portalA.playerCamera = playerCamera;
        portalB.playerCamera = playerCamera;

        var checkObject = new GameObject("RotateCheck");
        RotateCheck check = checkObject.AddComponent<RotateCheck>();
        check.playerRoot = player.transform;
        check.viewPoint = cameraObject.transform;
        check.playerCamera = playerCamera;
        check.traveller = traveller;

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildRotateCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "RotateCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[RotateCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static Material CreateMarkerMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(MarkerPath);
        if (existing != null)
        {
            return existing;
        }

        var material = new Material(Shader.Find("HDRP/Unlit"));
        material.SetColor("_UnlitColor", new Color(1f, 0f, 0f, 1f));
        AssetDatabase.CreateAsset(material, MarkerPath);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static void BuildRoom(float x, Material floorMaterial, Material accentMaterial)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor_" + x;
        floor.transform.position = new Vector3(x, -0.5f, 4f);
        floor.transform.localScale = new Vector3(24f, 1f, 30f);
        floor.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;

        for (int i = 0; i < 3; i++)
        {
            GameObject pillar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pillar.name = "Pillar_" + x + "_" + i;
            pillar.transform.position = new Vector3(x - 1f + i, 1.2f, 5f + i * 2f);
            pillar.transform.localScale = new Vector3(0.8f, 2.4f, 0.8f);
            pillar.GetComponent<MeshRenderer>().sharedMaterial =
                i == 1 ? accentMaterial : floorMaterial;
        }
    }
}
