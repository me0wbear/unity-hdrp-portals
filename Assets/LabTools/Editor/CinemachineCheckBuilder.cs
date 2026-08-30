using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Reproduces the camera rig UHFPS uses: the rendering camera is not a child of the player and
/// carries a CinemachineBrain, while a CinemachineCamera follows a camera holder that is a child
/// of the player. Cinemachine damps towards its follow target, so a teleport it has not been
/// told about has to be caught up over several frames.
/// </summary>
public static class CinemachineCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/CinemachineCheck.unity";

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

        BuildRoom(0f, grey, teal);
        BuildRoom(30f, grey, teal);

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");

        var portalAObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalAObject.name = "Portal_A";
        portalAObject.transform.SetPositionAndRotation(
            new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 180f, 0f));

        var portalBObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalBObject.name = "Portal_B";
        portalBObject.transform.SetPositionAndRotation(
            new Vector3(30f, 1.6f, 0f),
            System.Environment.GetEnvironmentVariable("PORTAL_TURN") == "1"
                ? Quaternion.Euler(0f, 90f, 0f)
                : Quaternion.identity);

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

        // What the virtual camera follows, and what the portal tracks for the crossing.
        var holder = new GameObject("CameraHolder");
        holder.transform.SetParent(player.transform, false);
        holder.transform.localPosition = new Vector3(0f, 1.5f, 0f);

        // The rendering camera lives outside the player, exactly as it does under UHFPS.
        var cameraObject = new GameObject("MainCamera");
        cameraObject.transform.SetPositionAndRotation(
            holder.transform.position, holder.transform.rotation);
        Camera playerCamera = cameraObject.AddComponent<Camera>();
        playerCamera.tag = "MainCamera";
        cameraObject.AddComponent<HDAdditionalCameraData>();
        cameraObject.AddComponent<CinemachineBrain>();

        var vcamObject = new GameObject("PlayerVirtualCamera");
        vcamObject.transform.SetPositionAndRotation(
            holder.transform.position, holder.transform.rotation);
        CinemachineCamera vcam = vcamObject.AddComponent<CinemachineCamera>();
        vcam.Follow = holder.transform;

        CinemachineFollow follow = vcamObject.AddComponent<CinemachineFollow>();
        follow.FollowOffset = Vector3.zero;
        follow.TrackerSettings.PositionDamping = new Vector3(0.2f, 0.2f, 0.2f);
        vcamObject.AddComponent<CinemachineRotateWithFollowTarget>();

        SerializedObject travellerObject = new SerializedObject(traveller);
        travellerObject.FindProperty("viewPoint").objectReferenceValue = holder.transform;
        travellerObject.ApplyModifiedPropertiesWithoutUndo();

        PortalCameraBridge bridge = player.AddComponent<PortalCameraBridge>();
        SerializedObject bridgeObject = new SerializedObject(bridge);
        bridgeObject.FindProperty("traveller").objectReferenceValue = traveller;
        bridgeObject.FindProperty("gameplayCamera").objectReferenceValue = playerCamera;
        bridgeObject.ApplyModifiedPropertiesWithoutUndo();

        portalA.playerCamera = playerCamera;
        portalB.playerCamera = playerCamera;

        if (System.Environment.GetEnvironmentVariable("PORTAL_SEAM") == "1")
        {
            UHFPS.Runtime.PlayerStateMachine machine =
                player.AddComponent<UHFPS.Runtime.PlayerStateMachine>();

            var seamObject = new GameObject("SeamCheck");
            SeamCheck seam = seamObject.AddComponent<SeamCheck>();
            seam.playerRoot = player.transform;
            seam.traveller = traveller;
            seam.machine = machine;
            seam.start = new Vector3(0f, 0.1f, -3f);
            seam.speed = 3f;
            seam.frames = 160;
        }
        else
        {
            var checkObject = new GameObject("CrossCheck");
            CrossCheck check = checkObject.AddComponent<CrossCheck>();
            check.playerRoot = player.transform;

            // Measured on the rendering camera: that is the eye the frame is drawn from.
            check.viewPoint = cameraObject.transform;
            check.entrance = portalA;
            check.exit = portalB;
            check.traveller = traveller;
            check.start = new Vector3(0f, 0.1f, -3f);
            check.stepPerFrame = 0.05f;
            check.frames = 90;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildCinemachineCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "CinemachineCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[CinemachineCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static void BuildRoom(float x, Material floorMaterial, Material accentMaterial)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor_" + x;
        floor.transform.position = new Vector3(x, -0.5f, 0f);
        floor.transform.localScale = new Vector3(24f, 1f, 40f);
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
