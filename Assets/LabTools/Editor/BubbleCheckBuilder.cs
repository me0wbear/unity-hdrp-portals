using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Depth of field is the test here. The portal screen writes the depth of its own surface, not
/// the depth of what it shows, so every depth driven effect on the player camera treats the
/// opening as a sheet held in front of the face. Approaching the portal should therefore blur
/// the view through it while the same geometry seen directly stays sharp, and crossing should
/// snap it back the instant the surface stops existing.
/// </summary>
public static class BubbleCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/BubbleCheck.unity";
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

        // A covered corridor on the near side, so the two rooms differ in brightness enough for
        // automatic exposure to have something to adapt to on the way through.
        if (System.Environment.GetEnvironmentVariable("PORTAL_AUTOEXP") == "1")
        {
            BuildFloor(0f, grey, new Vector3(0f, 4.2f, -10f), new Vector3(14f, 1f, 20f), "Roof_A");
            BuildFloor(0f, grey, new Vector3(-6.5f, 2f, -10f), new Vector3(1f, 5f, 20f), "WallLeft_A");
            BuildFloor(0f, grey, new Vector3(6.5f, 2f, -10f), new Vector3(1f, 5f, 20f), "WallRight_A");
        }

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
            seam.frames = 110;

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            BuildAndExit();
            return;
        }

        var checkObject = new GameObject("ColorCheck");
        ColorCheck check = checkObject.AddComponent<ColorCheck>();
        check.playerRoot = player.transform;
        check.playerCamera = playerCamera;
        check.traveller = traveller;
        check.portalObjects = new[] { portalAObject, portalBObject };
        check.outputDirectory = "BubbleCheck";

        // Signed distance to portal A is -z. The last step before the plane and the first one
        // after it are four centimetres apart and must therefore look the same.
        check.steps = new[]
        {
            Step("d300", -3f), Step("d150", -1.5f), Step("d080", -0.8f),
            Step("d040", -0.4f), Step("d020", -0.2f), Step("crossBefore", -0.02f),
            new ColorCheck.Step
            {
                name = "crossAfter", pose = new Vector3(30f, 0.1f, 0.02f), portalsEnabled = true
            }
        };

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();

        BuildAndExit();
    }

    private static void BuildAndExit()
    {
        string root = Path.Combine(Directory.GetCurrentDirectory(), "BuildBubbleCheck");
        Directory.CreateDirectory(root);

        var options = new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = Path.Combine(root, "BubbleCheck.exe"),
            target = BuildTarget.StandaloneWindows64,
            targetGroup = BuildTargetGroup.Standalone,
            options = BuildOptions.Development
        };

        UnityEditor.Build.Reporting.BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log("[BubbleCheck] build result=" + report.summary.result);
        EditorApplication.Exit(
            report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded ? 0 : 1);
    }

    private static ColorCheck.Step Step(string name, float z)
    {
        return new ColorCheck.Step
        {
            name = name,
            pose = new Vector3(0f, 0.1f, z),
            portalsEnabled = true
        };
    }

    private static void BuildFloor(float x, Material material)
    {
        BuildFloor(x, material, new Vector3(x, -0.5f, 0f), new Vector3(24f, 1f, 40f), "Floor_" + x);
    }

    private static void BuildFloor(
        float x, Material material, Vector3 position, Vector3 scale, string name)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = name;
        block.transform.position = position;
        block.transform.localScale = scale;
        block.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void BuildDetail(float x, Material floorMaterial, Material accentMaterial)
    {
        // Fine, high contrast structure well beyond the portal, so it is firmly in focus and any
        // softening measured on it comes from the portal surface rather than from its own depth.
        for (int i = 0; i < 12; i++)
        {
            GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
            bar.name = "Bar_" + i;
            bar.transform.position = new Vector3(x - 1.1f + i * 0.2f, 1.6f, 8f);
            bar.transform.localScale = new Vector3(0.08f, 2.6f, 0.08f);
            bar.GetComponent<MeshRenderer>().sharedMaterial =
                (i & 1) == 0 ? accentMaterial : floorMaterial;
        }

        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall";
        wall.transform.position = new Vector3(x, 3f, 11f);
        wall.transform.localScale = new Vector3(24f, 6f, 1f);
        wall.GetComponent<MeshRenderer>().sharedMaterial = floorMaterial;
    }

    private static void CreateGlobalVolume()
    {
        Directory.CreateDirectory(ProfileDirectory);
        string path = ProfileDirectory + "/BubbleCheckGlobal.asset";
        AssetDatabase.DeleteAsset(path);
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        Exposure exposure = AddOverride<Exposure>(profile);
        exposure.mode.overrideState = true;

        // Every colour measurement so far pinned exposure so that adaptation could not pollute
        // it. That also excluded the one thing a flash at the crossing would come from, so this
        // switch puts a real game's automatic exposure back in.
        if (System.Environment.GetEnvironmentVariable("PORTAL_AUTOEXP") == "1")
        {
            exposure.mode.value = ExposureMode.AutomaticHistogram;
            exposure.meteringMode.overrideState = true;
            exposure.meteringMode.value = MeteringMode.CenterWeighted;
            exposure.adaptationMode.overrideState = true;
            exposure.adaptationMode.value = AdaptationMode.Progressive;
            exposure.limitMin.overrideState = true;
            exposure.limitMin.value = 4f;
            exposure.limitMax.overrideState = true;
            exposure.limitMax.value = 18f;
        }
        else
        {
            exposure.mode.value = ExposureMode.Fixed;
            exposure.fixedExposure.overrideState = true;
            exposure.fixedExposure.value = 11f;
        }

        Tonemapping tonemapping = AddOverride<Tonemapping>(profile);
        tonemapping.mode.overrideState = true;
        tonemapping.mode.value = TonemappingMode.ACES;

        // None of the seam measurements so far had motion blur in the profile, which means the
        // module's own two frame suppression of it had nothing to suppress and could not show up.
        if (System.Environment.GetEnvironmentVariable("PORTAL_BLUR") == "1")
        {
            MotionBlur motionBlur = AddOverride<MotionBlur>(profile);
            motionBlur.intensity.overrideState = true;
            motionBlur.intensity.value = 0.6f;
        }

        if (System.Environment.GetEnvironmentVariable("PORTAL_NODOF") == "1")
        {
            AssetDatabase.SaveAssets();
            var plainVolumeObject = new GameObject("Global Volume");
            Volume plainVolume = plainVolumeObject.AddComponent<Volume>();
            plainVolume.isGlobal = true;
            plainVolume.sharedProfile = profile;
            return;
        }

        // Everything nearer than two metres goes soft. The portal content is eight metres away,
        // so it should stay sharp; the portal surface is not.
        DepthOfField depthOfField = AddOverride<DepthOfField>(profile);
        depthOfField.focusMode.overrideState = true;
        depthOfField.focusMode.value = DepthOfFieldMode.Manual;
        bool farBlur = System.Environment.GetEnvironmentVariable("PORTAL_FARDOF") == "1";

        depthOfField.nearFocusStart.overrideState = true;
        depthOfField.nearFocusStart.value = 0f;
        depthOfField.nearFocusEnd.overrideState = true;
        depthOfField.nearFocusEnd.value = farBlur ? 0f : 2f;

        // Far blur is the correctness check rather than the bug: the content is eight metres out,
        // so a portal that reports the right depth has to blur it exactly as much as looking at
        // the same geometry directly does. A depth that is merely "far enough" would not.
        depthOfField.farFocusStart.overrideState = true;
        depthOfField.farFocusStart.value = farBlur ? 4f : 200f;
        depthOfField.farFocusEnd.overrideState = true;
        depthOfField.farFocusEnd.value = farBlur ? 9f : 300f;

        ScreenSpaceAmbientOcclusion occlusion = AddOverride<ScreenSpaceAmbientOcclusion>(profile);
        occlusion.intensity.overrideState = true;
        occlusion.intensity.value = 0.8f;

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
