using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds the portal demo scene from code so it can be rebuilt headlessly.
///
/// Layout: a closed, warm-lit hall on the -Z side holds portal A inside a framed arch.
/// Portal B sits 40 units away in a cool, open courtyard, rotated 90 degrees so the
/// mapping between the pair is not a trivial identity. A second, independent pair (C/D)
/// stands beside the first to exercise multiple simultaneous pairs.
/// </summary>
public static class LabSceneBuilder
{
    public const string ScenePath = "Assets/LabTools/PortalLab.unity";
    private const string MaterialDirectory = "Assets/LabTools/Materials";

    private const float PortalWidth = 2.2f;
    private const float PortalHeight = 3.2f;
    private const float FrameThickness = 0.22f;

    [MenuItem("PortalLab/Build Demo Scene")]
    public static void Build()
    {
        Build(false);
    }

    /// <summary>
    /// <paramref name="forCapture"/> true builds the scene the automated run uses: LabCapture
    /// drives the camera and the walk-around controller is off. False builds the scene a person
    /// plays: the controller is live and LabCapture is off.
    /// </summary>
    public static void Build(bool forCapture)
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        Palette palette = CreatePalette();
        CreateLighting();

        BuildHall(palette);
        BuildCourtyard(palette);

        Material portalMaterial = LoadPortalMaterial();

        // A portal's local +Z is its front face. Portal A faces -Z so it looks back at the
        // player; portal B faces +X so the exit view is rotated relative to the entrance.
        Portal portalA = CreatePortal(
            "Portal_A",
            new Vector3(0f, PortalHeight * 0.5f, 0f),
            Quaternion.Euler(0f, 180f, 0f),
            portalMaterial,
            palette.Brass);
        Portal portalB = CreatePortal(
            "Portal_B",
            new Vector3(40f, PortalHeight * 0.5f, 0f),
            Quaternion.Euler(0f, 90f, 0f),
            portalMaterial,
            palette.Brass);

        Portal portalC = CreatePortal(
            "Portal_C",
            new Vector3(5.5f, PortalHeight * 0.5f, 0f),
            Quaternion.Euler(0f, 180f, 0f),
            portalMaterial,
            palette.Copper);
        Portal portalD = CreatePortal(
            "Portal_D",
            new Vector3(46f, PortalHeight * 0.5f, -9f),
            Quaternion.identity,
            portalMaterial,
            palette.Copper);

        GameObject player = CreatePlayer(out Camera playerCamera, out PortalTraveller traveller);

        LinkPortal(portalA, portalB, playerCamera);
        LinkPortal(portalB, portalA, playerCamera);
        LinkPortal(portalC, portalD, playerCamera);
        LinkPortal(portalD, portalC, playerCamera);

        SerializedObject travellerObject = new SerializedObject(traveller);
        travellerObject.FindProperty("viewPoint").objectReferenceValue = playerCamera.transform;
        travellerObject.ApplyModifiedPropertiesWithoutUndo();

        LabCapture capture = CreateCapture(player.transform, playerCamera, portalA.screen, traveller);
        capture.enabled = forCapture;
        player.GetComponent<PortalDemoController>().enabled = !forCapture;

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[LabSceneBuilder] saved " + ScenePath);
    }

    private struct Palette
    {
        public Material Stone;
        public Material StoneDark;
        public Material Plaster;
        public Material Brass;
        public Material Copper;
        public Material Teal;
        public Material Sand;
        public Material Rust;
    }

    private static Palette CreatePalette()
    {
        return new Palette
        {
            Stone = CreateLitMaterial("DemoStone", new Color(0.26f, 0.25f, 0.24f), 0.18f, 0f),
            StoneDark = CreateLitMaterial("DemoStoneDark", new Color(0.12f, 0.12f, 0.14f), 0.25f, 0f),
            Plaster = CreateLitMaterial("DemoPlaster", new Color(0.40f, 0.37f, 0.34f), 0.10f, 0f),
            Brass = CreateLitMaterial("DemoBrass", new Color(0.72f, 0.53f, 0.20f), 0.72f, 1f),
            Copper = CreateLitMaterial("DemoCopper", new Color(0.66f, 0.32f, 0.20f), 0.66f, 1f),
            Teal = CreateLitMaterial("DemoTeal", new Color(0.11f, 0.42f, 0.44f), 0.45f, 0f),
            Sand = CreateLitMaterial("DemoSand", new Color(0.76f, 0.62f, 0.36f), 0.20f, 0f),
            Rust = CreateLitMaterial("DemoRust", new Color(0.55f, 0.19f, 0.14f), 0.30f, 0f)
        };
    }

    /// <summary>The enclosed hall the player starts in, lit warm from inside.</summary>
    private static void BuildHall(Palette palette)
    {
        var hall = new GameObject("Hall").transform;

        CreateBox("Hall_Floor", new Vector3(0f, -0.15f, -8f), new Vector3(16f, 0.3f, 20f), palette.Stone, hall);
        // Open beams instead of a solid roof: sunlight falls through in stripes, which both
        // lights the hall and keeps the interior within a few stops of the courtyard outside.
        for (int i = 0; i < 6; i++)
        {
            float z = -0.5f - i * 3.4f;
            CreateBox(
                "Hall_Beam_" + i,
                new Vector3(0f, 6f, z),
                new Vector3(16f, 0.6f, 1.1f),
                palette.StoneDark,
                hall);
        }
        CreateBox("Hall_Wall_Left", new Vector3(-8f, 3f, -8f), new Vector3(0.5f, 6f, 20f), palette.Plaster, hall);
        CreateBox("Hall_Wall_Right", new Vector3(8f, 3f, -8f), new Vector3(0.5f, 6f, 20f), palette.Plaster, hall);
        CreateBox("Hall_Wall_Back", new Vector3(0f, 3f, -18f), new Vector3(16f, 6f, 0.5f), palette.Plaster, hall);

        // The wall the portals are cut into, built as segments so each portal has a real opening.
        BuildPortalWall(hall, palette);

        for (int i = 0; i < 4; i++)
        {
            float z = -3.5f - i * 3.5f;
            CreateBox("Hall_Column_L" + i, new Vector3(-5.5f, 3f, z), new Vector3(0.8f, 6f, 0.8f), palette.Stone, hall);
            CreateBox("Hall_Column_R" + i, new Vector3(5.5f, 3f, z), new Vector3(0.8f, 6f, 0.8f), palette.Stone, hall);
        }

        CreateBox("Hall_Bench", new Vector3(-3.2f, 0.35f, -6f), new Vector3(1.2f, 0.7f, 3.4f), palette.Teal, hall);
        CreateBox("Hall_Crate_A", new Vector3(3.4f, 0.5f, -5.2f), new Vector3(1f, 1f, 1f), palette.Rust, hall);
        CreateBox("Hall_Crate_B", new Vector3(3.9f, 1.3f, -5.6f), new Vector3(0.7f, 0.7f, 0.7f), palette.Rust, hall);
        CreateBox("Hall_Rug", new Vector3(0f, 0.02f, -6f), new Vector3(4f, 0.04f, 8f), palette.Rust, hall);

        CreateLamp("Hall_Lamp_A", new Vector3(-3f, 4.6f, -4f), new Color(1f, 0.80f, 0.56f), 6000f, 14f, hall);
        CreateLamp("Hall_Lamp_B", new Vector3(3f, 4.6f, -9f), new Color(1f, 0.80f, 0.56f), 6000f, 14f, hall);
        CreateLamp("Hall_Lamp_C", new Vector3(0f, 4.6f, -14f), new Color(1f, 0.84f, 0.62f), 5000f, 13f, hall);
    }

    /// <summary>
    /// Builds the front wall as four segments, leaving a clean opening around each portal so
    /// the portal surface reads as a doorway rather than a floating rectangle.
    /// </summary>
    private static void BuildPortalWall(Transform parent, Palette palette)
    {
        const float wallHeight = 6f;
        const float wallZ = 0.35f;
        const float wallDepth = 0.7f;
        const float wallLeft = -8f;
        const float wallRight = 8f;

        float halfWidth = PortalWidth * 0.5f;
        float openingAMin = -halfWidth;
        float openingAMax = halfWidth;
        float openingCMin = 5.5f - halfWidth;
        float openingCMax = 5.5f + halfWidth;

        CreateWallSegment(parent, palette, "Hall_Front_Left", wallLeft, openingAMin, wallHeight, wallZ, wallDepth);
        CreateWallSegment(parent, palette, "Hall_Front_Pier", openingAMax, openingCMin, wallHeight, wallZ, wallDepth);
        CreateWallSegment(parent, palette, "Hall_Front_Right", openingCMax, wallRight, wallHeight, wallZ, wallDepth);

        CreateLintel(parent, palette, "Hall_Lintel_A", 0f, wallHeight, wallZ, wallDepth);
        CreateLintel(parent, palette, "Hall_Lintel_C", 5.5f, wallHeight, wallZ, wallDepth);
    }

    private static void CreateWallSegment(
        Transform parent,
        Palette palette,
        string name,
        float minX,
        float maxX,
        float wallHeight,
        float wallZ,
        float wallDepth)
    {
        float width = maxX - minX;
        if (width <= 0.001f)
        {
            return;
        }

        CreateBox(
            name,
            new Vector3((minX + maxX) * 0.5f, wallHeight * 0.5f, wallZ),
            new Vector3(width, wallHeight, wallDepth),
            palette.Plaster,
            parent);
    }

    private static void CreateLintel(
        Transform parent,
        Palette palette,
        string name,
        float centerX,
        float wallHeight,
        float wallZ,
        float wallDepth)
    {
        float lintelHeight = wallHeight - PortalHeight;
        CreateBox(
            name,
            new Vector3(centerX, PortalHeight + lintelHeight * 0.5f, wallZ),
            new Vector3(PortalWidth, lintelHeight, wallDepth),
            palette.Plaster,
            parent);
    }

    /// <summary>The open courtyard the portals lead into, lit cool and from above.</summary>
    private static void BuildCourtyard(Palette palette)
    {
        var yard = new GameObject("Courtyard").transform;

        CreateBox("Yard_Floor", new Vector3(48f, -0.15f, -2f), new Vector3(26f, 0.3f, 26f), palette.Sand, yard);
        CreateBox("Yard_Step_A", new Vector3(43.5f, 0.15f, 0f), new Vector3(3f, 0.3f, 6f), palette.Stone, yard);
        CreateBox("Yard_Step_B", new Vector3(45f, 0.45f, 0f), new Vector3(2f, 0.3f, 5f), palette.Stone, yard);

        CreateBox("Yard_Wall_Far", new Vector3(58f, 3f, -2f), new Vector3(0.6f, 6f, 26f), palette.Stone, yard);
        CreateBox("Yard_Wall_Side", new Vector3(48f, 3f, 10f), new Vector3(26f, 6f, 0.6f), palette.Stone, yard);

        // Portal B is set into its own free-standing arch so it also reads as a doorway.
        CreateBox("Yard_Arch_Left", new Vector3(40f, 3f, 2.1f), new Vector3(0.7f, 6f, 1.6f), palette.Plaster, yard);
        CreateBox("Yard_Arch_Right", new Vector3(40f, 3f, -2.1f), new Vector3(0.7f, 6f, 1.6f), palette.Plaster, yard);
        CreateBox("Yard_Arch_Top", new Vector3(40f, 4.6f, 0f), new Vector3(0.7f, 2.8f, 2.6f), palette.Plaster, yard);

        CreateBox("Yard_Obelisk", new Vector3(50f, 2.5f, 3.5f), new Vector3(1f, 5f, 1f), palette.Teal, yard);
        CreateBox("Yard_Block_A", new Vector3(46.5f, 0.6f, 4.5f), new Vector3(1.2f, 1.2f, 1.2f), palette.Rust, yard);
        CreateBox("Yard_Block_B", new Vector3(47.4f, 1.5f, 4.2f), new Vector3(0.8f, 0.8f, 0.8f), palette.Copper, yard);
        CreateBox("Yard_Bench", new Vector3(44f, 0.4f, -5.5f), new Vector3(4f, 0.8f, 1f), palette.Stone, yard);
        CreateBox("Yard_Planter", new Vector3(51f, 0.5f, -4f), new Vector3(2f, 1f, 2f), palette.Sand, yard);
        CreateBox("Yard_Shrub", new Vector3(51f, 1.4f, -4f), new Vector3(1.4f, 1.2f, 1.4f), palette.Teal, yard);

        CreateLamp("Yard_Lamp", new Vector3(46f, 4f, 0f), new Color(0.72f, 0.84f, 1f), 2600f, 15f, yard);
    }

    private static void LinkPortal(Portal portal, Portal exit, Camera playerCamera)
    {
        portal.exitPortal = exit;
        portal.playerCamera = playerCamera;
        portal.resolutionDivider = 1;
        portal.recursionDepth = 2;
        portal.clippingOffset = 0.05f;
        portal.clippingSafetyFactor = 2f;
        portal.cullWhenOffscreen = true;
        EditorUtility.SetDirty(portal);
    }

    private static Portal CreatePortal(
        string name,
        Vector3 position,
        Quaternion rotation,
        Material material,
        Material frameMaterial)
    {
        var root = new GameObject(name);
        root.transform.SetPositionAndRotation(position, rotation);

        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.size = new Vector3(PortalWidth, PortalHeight, 1.4f);

        var screenObject = new GameObject("Screen");
        screenObject.transform.SetParent(root.transform, false);
        screenObject.transform.localPosition = Vector3.zero;
        screenObject.transform.localRotation = Quaternion.identity;
        screenObject.transform.localScale = new Vector3(PortalWidth, PortalHeight, 1f);

        MeshFilter filter = screenObject.AddComponent<MeshFilter>();
        filter.sharedMesh = LoadBuiltinMesh("Quad.fbx", PrimitiveType.Quad);

        MeshRenderer renderer = screenObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        CreateFrame(root.transform, frameMaterial);

        Portal portal = root.AddComponent<Portal>();
        portal.screen = renderer;
        return portal;
    }

    /// <summary>
    /// A thin decorative frame around the opening. It is a sibling of the screen, never a
    /// parent, so it cannot disturb the screen transform the aperture drives every frame.
    /// </summary>
    private static void CreateFrame(Transform portalRoot, Material material)
    {
        var frame = new GameObject("Frame").transform;
        frame.SetParent(portalRoot, false);

        float halfWidth = PortalWidth * 0.5f;
        float halfHeight = PortalHeight * 0.5f;
        float outerWidth = PortalWidth + FrameThickness * 2f;

        CreateBox(
            "Frame_Top",
            new Vector3(0f, halfHeight + FrameThickness * 0.5f, 0f),
            new Vector3(outerWidth, FrameThickness, FrameThickness),
            material,
            frame,
            local: true);
        CreateBox(
            "Frame_Bottom",
            new Vector3(0f, -halfHeight - FrameThickness * 0.5f, 0f),
            new Vector3(outerWidth, FrameThickness, FrameThickness),
            material,
            frame,
            local: true);
        CreateBox(
            "Frame_Left",
            new Vector3(-halfWidth - FrameThickness * 0.5f, 0f, 0f),
            new Vector3(FrameThickness, PortalHeight, FrameThickness),
            material,
            frame,
            local: true);
        CreateBox(
            "Frame_Right",
            new Vector3(halfWidth + FrameThickness * 0.5f, 0f, 0f),
            new Vector3(FrameThickness, PortalHeight, FrameThickness),
            material,
            frame,
            local: true);
    }

    private static GameObject CreatePlayer(out Camera camera, out PortalTraveller traveller)
    {
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0.05f, -7f);

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
        camera.nearClipPlane = 0.05f;
        camera.farClipPlane = 500f;
        camera.fieldOfView = 68f;

        HDAdditionalCameraData data = cameraObject.AddComponent<HDAdditionalCameraData>();
        data.antialiasing = HDAdditionalCameraData.AntialiasingMode.SubpixelMorphologicalAntiAliasing;
        data.SMAAQuality = HDAdditionalCameraData.SMAAQualityLevel.High;

        // Walk-around controller. Disabled while LabCapture drives the scripted run; enable it
        // (and disable LabCapture) to play the scene by hand.
        PortalDemoController demoController = player.AddComponent<PortalDemoController>();
        SerializedObject controllerObject = new SerializedObject(demoController);
        controllerObject.FindProperty("head").objectReferenceValue = cameraObject.transform;
        controllerObject.ApplyModifiedPropertiesWithoutUndo();

        return player;
    }

    private static LabCapture CreateCapture(
        Transform playerRoot,
        Camera playerCamera,
        MeshRenderer portalScreen,
        PortalTraveller traveller)
    {
        var captureObject = new GameObject("LabCapture");
        LabCapture capture = captureObject.AddComponent<LabCapture>();
        capture.playerRoot = playerRoot;
        capture.playerCamera = playerCamera;
        capture.portalScreen = portalScreen;
        capture.traveller = traveller;
        capture.shots = new[]
        {
            NewShot("hall_wide", new Vector3(0f, 0.05f, -11f), new Vector3(0f, 0f, 0f)),
            NewShot("two_pairs", new Vector3(2.75f, 0.05f, -8f), new Vector3(0f, 0f, 0f)),
            NewShot("approach", new Vector3(0f, 0.05f, -4f), Vector3.zero),
            NewShot("close", new Vector3(0f, 0.05f, -1.2f), Vector3.zero),
            NewShot("grazing", new Vector3(1.6f, 0.05f, -1.4f), new Vector3(0f, -40f, 0f)),
            NewShot("touching", new Vector3(0f, 0.05f, -0.12f), Vector3.zero),
            NewShot("second_portal", new Vector3(5.5f, 0.05f, -3.5f), Vector3.zero)
        };

        return capture;
    }

    private static LabCapture.Shot NewShot(string name, Vector3 position, Vector3 eulerAngles)
    {
        return new LabCapture.Shot { name = name, position = position, eulerAngles = eulerAngles };
    }

    private static void CreateLighting()
    {
        var sunObject = new GameObject("Sun");
        sunObject.transform.rotation = Quaternion.Euler(52f, 18f, 0f);
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.95f, 0.86f);
        sun.shadows = LightShadows.Soft;

        HDAdditionalLightData sunData = sunObject.AddComponent<HDAdditionalLightData>();
        sunData.SetIntensity(11000f, LightUnit.Lux);
        sunData.EnableShadows(true);
        sunData.SetShadowResolution(2048);

        var volumeObject = new GameObject("Sky and Fog Volume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 0f;
        volume.sharedProfile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            "Assets/Settings/SkyandFogSettingsProfile.asset");

        var gradingObject = new GameObject("Post Processing Volume");
        Volume grading = gradingObject.AddComponent<Volume>();
        grading.isGlobal = true;
        grading.priority = 1f;
        grading.sharedProfile = CreateGradingProfile();
    }

    /// <summary>
    /// Automatic exposure plus ACES tone mapping. Without this the interior sits far above the
    /// default fixed exposure and every surface clips to white, hiding both the material colours
    /// and whether the portal view matches its surroundings.
    /// </summary>
    private static VolumeProfile CreateGradingProfile()
    {
        const string path = "Assets/LabTools/DemoGrading.asset";
        VolumeProfile existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (existing != null)
        {
            return existing;
        }

        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, path);

        Exposure exposure = AddOverride<Exposure>(profile);
        exposure.mode.overrideState = true;
        exposure.mode.value = ExposureMode.AutomaticHistogram;
        exposure.meteringMode.overrideState = true;
        exposure.meteringMode.value = MeteringMode.CenterWeighted;
        exposure.limitMin.overrideState = true;
        exposure.limitMin.value = 4f;
        exposure.limitMax.overrideState = true;
        exposure.limitMax.value = 16f;
        exposure.adaptationMode.overrideState = true;
        exposure.adaptationMode.value = AdaptationMode.Progressive;

        Tonemapping tonemapping = AddOverride<Tonemapping>(profile);
        tonemapping.mode.overrideState = true;
        tonemapping.mode.value = TonemappingMode.ACES;

        // Explicitly on, and strong, so the transition frame is a real test of the smear rather
        // than a scene where motion blur happened to be off.
        MotionBlur motionBlur = AddOverride<MotionBlur>(profile);
        motionBlur.intensity.overrideState = true;
        motionBlur.intensity.value = 0.6f;

        Bloom bloom = AddOverride<Bloom>(profile);
        bloom.intensity.overrideState = true;
        bloom.intensity.value = 0.12f;
        bloom.scatter.overrideState = true;
        bloom.scatter.value = 0.65f;

        // Without baked GI or SSGI the sky ambient probe lights the closed hall as if it had no
        // roof, washing every surface out. Damping indirect diffuse lets the lamps read instead.
        IndirectLightingController indirect = AddOverride<IndirectLightingController>(profile);
        indirect.indirectDiffuseLightingMultiplier.overrideState = true;
        indirect.indirectDiffuseLightingMultiplier.value = 0.5f;

        ScreenSpaceAmbientOcclusion occlusion = AddOverride<ScreenSpaceAmbientOcclusion>(profile);
        occlusion.intensity.overrideState = true;
        occlusion.intensity.value = 0.7f;

        ColorAdjustments color = AddOverride<ColorAdjustments>(profile);
        color.postExposure.overrideState = true;
        color.postExposure.value = -0.2f;
        color.contrast.overrideState = true;
        color.contrast.value = 12f;
        color.saturation.overrideState = true;
        color.saturation.value = 8f;

        AssetDatabase.SaveAssets();
        return profile;
    }

    /// <summary>
    /// VolumeProfile.Add creates the override in memory only. Without adding it to the profile
    /// asset the saved profile comes back empty and every override silently does nothing.
    /// </summary>
    private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        T component = profile.Add<T>(true);
        component.hideFlags = HideFlags.HideInHierarchy;
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }

    private static void CreateLamp(
        string name,
        Vector3 position,
        Color color,
        float lumens,
        float range,
        Transform parent)
    {
        var lampObject = new GameObject(name);
        lampObject.transform.SetParent(parent, false);
        lampObject.transform.position = position;

        Light light = lampObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.shadows = LightShadows.None;

        HDAdditionalLightData data = lampObject.AddComponent<HDAdditionalLightData>();
        data.SetIntensity(lumens, LightUnit.Lumen);
        data.range = range;
    }

    private static GameObject CreateBox(
        string name,
        Vector3 position,
        Vector3 size,
        Material material,
        Transform parent = null,
        bool local = false)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        if (parent != null)
        {
            box.transform.SetParent(parent, false);
        }

        if (local)
        {
            box.transform.localPosition = position;
        }
        else
        {
            box.transform.position = position;
        }

        box.transform.localScale = size;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    private static Material CreateLitMaterial(string name, Color color, float smoothness, float metallic)
    {
        Directory.CreateDirectory(MaterialDirectory);
        string path = MaterialDirectory + "/" + name + ".mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            return existing;
        }

        var material = new Material(Shader.Find("HDRP/Lit"));
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", smoothness);
        material.SetFloat("_Metallic", metallic);
        HDMaterial.ValidateMaterial(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Material LoadPortalMaterial()
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>("Assets/portal/PortalScreenMat.mat");
        if (material == null)
        {
            Debug.LogError("[LabSceneBuilder] Assets/portal/PortalScreenMat.mat is missing.");
        }

        return material;
    }

    private static Mesh LoadBuiltinMesh(string assetName, PrimitiveType fallback)
    {
        Mesh mesh = Resources.GetBuiltinResource<Mesh>(assetName);
        if (mesh != null)
        {
            return mesh;
        }

        GameObject temporary = GameObject.CreatePrimitive(fallback);
        mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        Object.DestroyImmediate(temporary);
        return mesh;
    }
}
