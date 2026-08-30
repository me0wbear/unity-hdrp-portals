using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Follows portal/SETUP.md literally, step by step, using only what the document tells a
/// person to click. Nothing here uses knowledge that is not written in that document.
/// If this scene works, the instructions are sufficient; if it does not, the instructions
/// are missing a step and must be corrected.
/// </summary>
public static class SetupCheckBuilder
{
    public const string ScenePath = "Assets/LabTools/SetupCheck.unity";

    public static void Build()
    {
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateEnvironment();

        // --- SETUP.md 3.1-3.3: portal A ---
        // "Создайте пустой GameObject, назовите Portal_A"
        var portalAObject = new GameObject("Portal_A");
        // "Поставьте его туда, где должен быть проём. Точка отсчёта — центр проёма."
        // "Если проём высотой 3 метра стоит на полу, ставьте корень на высоту 1.5."
        portalAObject.transform.position = new Vector3(0f, 1.5f, 0f);
        // "Разверните так, чтобы локальная ось +Z смотрела на игрока."
        // The player stands at -Z, so the portal is turned to face it.
        portalAObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
        // "Добавьте компонент Portal."
        Portal portalA = portalAObject.AddComponent<Portal>();

        // "Добавьте Box Collider. Включите Is Trigger."
        // "Задайте Size: X и Y — по размеру проёма, Z — толщина зоны." "достаточно 1.0-1.5"
        BoxCollider triggerA = portalAObject.AddComponent<BoxCollider>();
        triggerA.isTrigger = true;
        triggerA.size = new Vector3(2f, 3f, 1.2f);

        MeshRenderer screenA = CreateScreenAsDocumented(portalAObject.transform, 2f, 3f);
        // "В компоненте Portal на корне назначьте этот Mesh Renderer в поле Screen."
        portalA.screen = screenA;

        // --- SETUP.md 3.4: portal B ---
        var portalBObject = new GameObject("Portal_B");
        portalBObject.transform.position = new Vector3(30f, 1.5f, 0f);
        // "Ориентация может быть любой."
        portalBObject.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        Portal portalB = portalBObject.AddComponent<Portal>();

        BoxCollider triggerB = portalBObject.AddComponent<BoxCollider>();
        triggerB.isTrigger = true;
        triggerB.size = new Vector3(2f, 3f, 1.2f);

        MeshRenderer screenB = CreateScreenAsDocumented(portalBObject.transform, 2f, 3f);
        portalB.screen = screenB;

        // "Затем свяжите пару в обе стороны."
        portalA.exitPortal = portalB;
        portalB.exitPortal = portalA;

        // --- SETUP.md 4: the player ---
        // "На корневой объект игрока (тот, где CharacterController) добавьте PortalTraveller."
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

        // "В поле View Point назначьте Transform камеры."
        SerializedObject travellerObject = new SerializedObject(traveller);
        travellerObject.FindProperty("viewPoint").objectReferenceValue = cameraObject.transform;
        travellerObject.ApplyModifiedPropertiesWithoutUndo();

        // "На обоих порталах в поле Player Camera назначьте одну и ту же камеру."
        portalA.playerCamera = playerCamera;
        portalB.playerCamera = playerCamera;

        AddCapture(player.transform, playerCamera, screenA, traveller);

        Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
        EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        Debug.Log("[SetupCheck] saved " + ScenePath);
    }

    public static void BuildAndExit()
    {
        Build();
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// SETUP.md 3.3, followed to the letter: child named Screen, zeroed position and rotation,
    /// scale carrying the opening size, built-in Quad, the module material, shadows off.
    /// </summary>
    private static MeshRenderer CreateScreenAsDocumented(Transform portalRoot, float width, float height)
    {
        var screenObject = new GameObject("Screen");
        screenObject.transform.SetParent(portalRoot, false);
        screenObject.transform.localPosition = Vector3.zero;
        screenObject.transform.localRotation = Quaternion.identity;
        screenObject.transform.localScale = new Vector3(width, height, 1f);

        MeshFilter filter = screenObject.AddComponent<MeshFilter>();
        filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        MeshRenderer renderer = screenObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial =
            AssetDatabase.LoadAssetAtPath<Material>("Assets/portal/PortalScreenMat.mat");
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
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
        Material rust = AssetDatabase.LoadAssetAtPath<Material>("Assets/LabTools/Materials/DemoRust.mat");

        Box("Ground_A", new Vector3(0f, -0.5f, -6f), new Vector3(20f, 1f, 20f), grey);
        Box("Ground_B", new Vector3(36f, -0.5f, 0f), new Vector3(20f, 1f, 20f), grey);
        Box("B_Marker_Teal", new Vector3(34f, 1.5f, 2f), new Vector3(1f, 3f, 1f), teal);
        Box("B_Marker_Rust", new Vector3(36f, 0.75f, -2f), new Vector3(1.5f, 1.5f, 1.5f), rust);
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

    private static void AddCapture(
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
        capture.outputDirectory = "SetupCheck";
        capture.shots = new[]
        {
            new LabCapture.Shot { name = "far", position = new Vector3(0f, 0.1f, -6f) },
            new LabCapture.Shot { name = "near", position = new Vector3(0f, 0.1f, -1.5f) },
            new LabCapture.Shot { name = "touching", position = new Vector3(0f, 0.1f, -0.1f) }
        };
    }
}
