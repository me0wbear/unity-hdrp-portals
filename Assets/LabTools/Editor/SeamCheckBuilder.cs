using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// The whole UHFPS shape at once: a detached camera driven by a CinemachineBrain, a virtual
/// camera following a holder parented to the player, a look controller that rewrites transforms
/// every frame from a stored angle, world space velocity fed to a CharacterController, rooms
/// graded differently, and depth of field on. The portal pair is perpendicular, which is the case
/// that turns the rig. Every fix made so far has to hold at the same time here.
/// </summary>
public static class SeamCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/SeamCheck.unity";
    private const string ProfileDirectory = "Assets/LabTools/Profiles";

    public static void BuildPlayer()
    {
        PrepareScene();
        BuildSavedScene();
    }

    public static void PrepareScene()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        var sunObject = new GameObject("Sun");
        sunObject.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sunObject.AddComponent<HDAdditionalLightData>().SetIntensity(20000f, LightUnit.Lux);

        CreateGlobalVolume();
        CreateRoomVolume("Room_A_Volume", new Vector3(0f, 1.6f, 0f), new Vector3(28f, 20f, 60f),
            new Color(1f, 0.7f, 0.5f));
        CreateRoomVolume("Room_B_Volume", new Vector3(40f, 1.6f, 0f), new Vector3(24f, 20f, 24f),
            new Color(0.5f, 0.7f, 1f));

        Material grey = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoStone.mat");
        Material teal = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoTeal.mat");

        Floor("Floor_A", new Vector3(0f, -0.5f, 0f), new Vector3(24f, 1f, 40f), grey);
        Floor("Floor_B", new Vector3(40f, -0.5f, 0f), new Vector3(24f, 1f, 24f), grey);

        for (int i = 0; i < 10; i++)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar_" + i;
            bar.transform.position = new Vector3(38f, 1.6f, -1.1f + i * 0.24f);
            bar.transform.localScale = new Vector3(0.1f, 2.6f, 0.1f);
            bar.GetComponent<MeshRenderer>().sharedMaterial = (i & 1) == 0 ? teal : grey;
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall_B";
        wall.transform.position = new Vector3(43f, 3f, 0f);
        wall.transform.localScale = new Vector3(1f, 6f, 24f);
        wall.GetComponent<MeshRenderer>().sharedMaterial = grey;

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");

        var portalAObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalAObject.name = "Portal_A";
        portalAObject.transform.SetPositionAndRotation(
            new Vector3(0f, 1.6f, 0f), Quaternion.Euler(0f, 180f, 0f));

        var portalBObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        portalBObject.name = "Portal_B";
        portalBObject.transform.SetPositionAndRotation(
            new Vector3(30f, 1.6f, 0f), Quaternion.Euler(0f, 90f, 0f));

        Portal portalA = portalAObject.GetComponent<Portal>();
        Portal portalB = portalBObject.GetComponent<Portal>();
        portalA.exitPortal = portalB;
        portalB.exitPortal = portalA;

        bool depth = System.Environment.GetEnvironmentVariable("PORTAL_NODEPTH") != "1";
        portalA.writeContentDepth = depth;
        portalB.writeContentDepth = depth;
        bool cinemachine = System.Environment.GetEnvironmentVariable("PORTAL_NOCM") != "1";
        bool blend = System.Environment.GetEnvironmentVariable("PORTAL_NOBLEND") != "1";
        portalA.blendVolumesThroughPortal = blend;
        portalB.blendVolumesThroughPortal = blend;
        bool useLook = System.Environment.GetEnvironmentVariable("PORTAL_NOLOOK") != "1";

        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0.1f, -3f);
        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.9f, 0f);
        PortalTraveller traveller = player.AddComponent<PortalTraveller>();

        var holder = new GameObject("CameraHolder");
        holder.transform.SetParent(player.transform, false);
        holder.transform.localPosition = new Vector3(0f, 1.5f, 0f);

        var cameraObject = new GameObject("MainCamera");
        cameraObject.transform.SetPositionAndRotation(
            holder.transform.position, holder.transform.rotation);
        Camera playerCamera = cameraObject.AddComponent<Camera>();
        playerCamera.tag = "MainCamera";
        playerCamera.fieldOfView = 85f;
        cameraObject.AddComponent<HDAdditionalCameraData>();

        if (cinemachine)
        {
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
        }
        else
        {
            cameraObject.transform.SetParent(holder.transform, false);
        }

        if (useLook)
        {
            UHFPS.Runtime.LookController look = player.AddComponent<UHFPS.Runtime.LookController>();
            look.body = player.transform;
            look.head = holder.transform;
            look.LookRotation = Vector2.zero;
            look.PlayerForward = UHFPS.Runtime.LookController.ForwardStyle.RootForward;
        }

        UHFPS.Runtime.PlayerStateMachine machine =
            player.AddComponent<UHFPS.Runtime.PlayerStateMachine>();

        PortalCameraBridge bridge = player.AddComponent<PortalCameraBridge>();
        SerializedObject bridgeObject = new SerializedObject(bridge);
        bridgeObject.FindProperty("traveller").objectReferenceValue = traveller;
        bridgeObject.FindProperty("gameplayCamera").objectReferenceValue = playerCamera;
        bridgeObject.ApplyModifiedPropertiesWithoutUndo();

        SerializedObject travellerObject = new SerializedObject(traveller);
        travellerObject.FindProperty("viewPoint").objectReferenceValue = holder.transform;
        travellerObject.ApplyModifiedPropertiesWithoutUndo();

        portalA.playerCamera = playerCamera;
        portalB.playerCamera = playerCamera;

        var checkObject = new GameObject("SeamCheck");
        SeamCheck check = checkObject.AddComponent<SeamCheck>();
        check.playerRoot = player.transform;
        check.traveller = traveller;
        check.machine = machine;
        check.start = new Vector3(0f, 0.1f, -3f);
        check.speed = 3f;
        check.frames = 160;

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new UnityEditor.Build.BuildFailedException("Cannot save Seam scene.");
        AssetDatabase.SaveAssets();

        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ValidateSavedScene();
    }

    public static void ValidateSavedScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != ScenePath) throw new UnityEditor.Build.BuildFailedException("Expected saved Seam scene.");
        SeamCheck check = UnityEngine.Object.FindAnyObjectByType<SeamCheck>();
        if (check == null || check.playerRoot == null || check.traveller == null || check.machine == null
            || check.playerRoot.GetComponent<CharacterController>() == null)
            throw new UnityEditor.Build.BuildFailedException("Saved Seam scene is missing required references.");
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MonoBehaviour component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (component == null) throw new UnityEditor.Build.BuildFailedException("Missing script in saved Seam scene.");
                MonoScript script = MonoScript.FromMonoBehaviour(component);
                if (script == null || !EditorUtility.IsPersistent(script) || script.GetClass() != component.GetType()
                    || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(script, out string guid, out long localId)
                    || string.IsNullOrEmpty(guid))
                    throw new UnityEditor.Build.BuildFailedException("Nonpersistent script in saved Seam scene.");
            }
        }
        PortalCameraBridge bridge = check.playerRoot.GetComponent<PortalCameraBridge>();
        if (bridge == null) throw new UnityEditor.Build.BuildFailedException("Saved Seam bridge is missing.");
        var serializedBridge = new SerializedObject(bridge);
        var serializedTraveller = new SerializedObject(check.traveller);
        if (serializedBridge.FindProperty("traveller").objectReferenceValue != check.traveller
            || serializedBridge.FindProperty("gameplayCamera").objectReferenceValue == null
            || serializedTraveller.FindProperty("viewPoint").objectReferenceValue == null)
            throw new UnityEditor.Build.BuildFailedException("Saved Seam camera/traveller references are missing.");
        Portal[] portals = UnityEngine.Object.FindObjectsByType<Portal>();
        if (portals.Length != 2 || System.Array.Exists(portals, portal => portal.exitPortal == null || portal.playerCamera == null))
            throw new UnityEditor.Build.BuildFailedException("Saved Seam portal pair is incomplete.");
    }

    public static void BuildSavedScene()
    {
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        ValidateSavedScene();

        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildSeamCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "SeamCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        options = PortalCheckBuildIdentity.PrepareOptions(options,
            System.Environment.GetEnvironmentVariable("PORTAL_CHECK_NAME"));
        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[SeamCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static void Floor(string name, Vector3 position, Vector3 scale, Material material)
    {
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = name;
        floor.transform.position = position;
        floor.transform.localScale = scale;
        floor.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void CreateGlobalVolume()
    {
        VolumeProfile profile = NewProfile("SeamCheckGlobal");

        Exposure exposure = AddOverride<Exposure>(profile);
        exposure.mode.overrideState = true;
        exposure.mode.value = ExposureMode.Fixed;
        exposure.fixedExposure.overrideState = true;
        exposure.fixedExposure.value = 11f;

        Tonemapping tonemapping = AddOverride<Tonemapping>(profile);
        tonemapping.mode.overrideState = true;
        tonemapping.mode.value = TonemappingMode.ACES;

        DepthOfField depthOfField = AddOverride<DepthOfField>(profile);
        depthOfField.focusMode.overrideState = true;
        depthOfField.focusMode.value = DepthOfFieldMode.Manual;
        depthOfField.nearFocusStart.overrideState = true;
        depthOfField.nearFocusStart.value = 0f;
        depthOfField.nearFocusEnd.overrideState = true;
        depthOfField.nearFocusEnd.value = 2f;
        depthOfField.farFocusStart.overrideState = true;
        depthOfField.farFocusStart.value = 200f;
        depthOfField.farFocusEnd.overrideState = true;
        depthOfField.farFocusEnd.value = 300f;

        AssetDatabase.SaveAssets();

        var volumeObject = new GameObject("Global Volume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.sharedProfile = profile;
    }

    private static void CreateRoomVolume(string name, Vector3 centre, Vector3 size, Color filter)
    {
        VolumeProfile profile = NewProfile(name);

        ColorAdjustments adjustments = AddOverride<ColorAdjustments>(profile);
        adjustments.colorFilter.overrideState = true;
        adjustments.colorFilter.value = filter;

        AssetDatabase.SaveAssets();

        var volumeObject = new GameObject(name);
        volumeObject.transform.position = centre;
        var collider = volumeObject.AddComponent<BoxCollider>();
        collider.isTrigger = true;
        collider.size = size;

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
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, path);
        }
        foreach (VolumeComponent component in profile.components)
        {
            if (component == null) continue;
            component.active = false;
            EditorUtility.SetDirty(component);
        }
        return profile;
    }

    private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        if (profile.TryGet(out T existing))
        {
            existing.active = true;
            EditorUtility.SetDirty(existing);
            return existing;
        }
        T component = ScriptableObject.CreateInstance<T>();
        component.name = typeof(T).Name;
        component.hideFlags = HideFlags.HideInHierarchy;
        profile.components.Add(component);
        AssetDatabase.AddObjectToAsset(component, profile);
        EditorUtility.SetDirty(profile);
        return component;
    }
}
