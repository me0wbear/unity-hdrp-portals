using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Собирает сцену для проверки портала с чужим контроллером вида.
///
/// Пара здесь намеренно повёрнута на девяносто градусов. Прямая пара ничего не
/// проверяет: она не меняет направление взгляда, и контроллер, который держит
/// угол у себя, отработает на ней ровно так же, как без портала. Ломается всё
/// на повороте, поэтому сцена и построена вокруг него.
///
/// В сцене лежит <see cref="ExampleLookPortalBridge"/>. Чтобы увидеть, что
/// именно он чинит, выключите его в инспекторе и пройдите сквозь портал ещё раз:
/// игрок окажется на той стороне, но смотреть будет в прежнюю мировую сторону,
/// то есть в стену.
/// </summary>
public static class PortalIntegrationScene
{
    public const string ScenePath = "Assets/portal/Examples/PortalIntegration.unity";

    private const string Folder = "Assets/portal/Examples";
    private const string ProfilePath = Folder + "/IntegrationVolume.asset";
    private const string MaterialFolder = Folder + "/Materials";

    /// <summary>Насколько разнесены комнаты. Дальше, чем видно из одной в другую.</summary>
    private const float Separation = 40f;

    [MenuItem("Tools/Portals/Build Integration Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLighting();
        CreateGround();
        CreateRoomA();
        CreateRoomB();

        // Вход смотрит на игрока, выход развёрнут на девяносто градусов
        // относительно входа: пройдя сквозь пару, игрок обязан выйти глядя вдоль
        // другой оси, и контроллер обязан это принять.
        Portal entrance = PlacePortal("Portal_Entrance", new Vector3(0f, 1.5f, 6f), 180f);
        Portal exit = PlacePortal("Portal_Exit", new Vector3(Separation, 1.5f, 0f), 90f);
        entrance.exitPortal = exit;
        exit.exitPortal = entrance;

        Camera camera = CreatePlayer();

        foreach (Portal portal in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None))
        {
            portal.playerCamera = camera;
            EditorUtility.SetDirty(portal);
        }

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[PortalIntegration] built " + ScenePath);
    }

    /// <summary>Точка входа для headless-сборки: собрать и выйти.</summary>
    public static void BuildAndExit()
    {
        Build();
        EditorApplication.Exit(0);
    }

    private static void CreateLighting()
    {
        var sunObject = new GameObject("Sun");
        sunObject.transform.rotation = Quaternion.Euler(48f, 30f, 0f);
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sunObject.AddComponent<HDAdditionalLightData>().SetIntensity(60000f, LightUnit.Lux);

        AssetDatabase.DeleteAsset(ProfilePath);
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);

        var environment = AddOverride<VisualEnvironment>(profile);
        environment.skyType.overrideState = true;
        environment.skyType.value = (int)SkyType.PhysicallyBased;

        AddOverride<PhysicallyBasedSky>(profile);

        var exposure = AddOverride<Exposure>(profile);
        exposure.mode.overrideState = true;
        exposure.mode.value = ExposureMode.Fixed;
        exposure.fixedExposure.overrideState = true;
        exposure.fixedExposure.value = 11f;

        var tonemapping = AddOverride<Tonemapping>(profile);
        tonemapping.mode.overrideState = true;
        tonemapping.mode.value = TonemappingMode.ACES;

        AssetDatabase.SaveAssets();

        var volumeObject = new GameObject("Global Volume");
        Volume volume = volumeObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.sharedProfile = profile;
    }

    private static void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(12f, 1f, 12f);
        ground.transform.position = new Vector3(Separation * 0.5f, 0f, 0f);
        Paint(ground, "Integration_Ground", new Color(0.2f, 0.2f, 0.22f));
    }

    private static void CreateRoomA()
    {
        var room = new GameObject("Room_Entrance");
        Color wall = new Color(0.28f, 0.33f, 0.42f);

        Wall(room.transform, "Left", new Vector3(-5f, 2f, 0f), new Vector3(0.4f, 4f, 12f), wall);
        Wall(room.transform, "Right", new Vector3(5f, 2f, 0f), new Vector3(0.4f, 4f, 12f), wall);
        Wall(room.transform, "Back", new Vector3(0f, 2f, -6f), new Vector3(10f, 4f, 0.4f), wall);
        Wall(room.transform, "Front_Left", new Vector3(-3f, 2f, 6f), new Vector3(4f, 4f, 0.4f), wall);
        Wall(room.transform, "Front_Right", new Vector3(3f, 2f, 6f), new Vector3(4f, 4f, 0.4f), wall);
        Wall(room.transform, "Front_Top", new Vector3(0f, 3.5f, 6f), new Vector3(2f, 1f, 0.4f), wall);
    }

    /// <summary>
    /// Комната выхода развёрнута вместе с порталом. Столбы у неё стоят по одной
    /// стене и разного цвета: по ним видно, куда именно игрок смотрит после
    /// перехода, а по слову «видно» здесь и проверяется вся работа моста.
    /// </summary>
    private static void CreateRoomB()
    {
        var room = new GameObject("Room_Exit");
        room.transform.position = new Vector3(Separation, 0f, 0f);
        Color wall = new Color(0.45f, 0.3f, 0.22f);

        Wall(room.transform, "Far", new Vector3(6f, 2f, 0f), new Vector3(0.4f, 4f, 12f), wall);
        Wall(room.transform, "Left", new Vector3(0f, 2f, -6f), new Vector3(12f, 4f, 0.4f), wall);
        Wall(room.transform, "Right", new Vector3(0f, 2f, 6f), new Vector3(12f, 4f, 0.4f), wall);
        Wall(room.transform, "Near_Top", new Vector3(-6f, 3.5f, 0f), new Vector3(0.4f, 1f, 2f), wall);
        Wall(room.transform, "Near_A", new Vector3(-6f, 2f, 3.5f), new Vector3(0.4f, 4f, 5f), wall);
        Wall(room.transform, "Near_B", new Vector3(-6f, 2f, -3.5f), new Vector3(0.4f, 4f, 5f), wall);

        Prop(room.transform, "Pillar_Red", new Vector3(5f, 1.2f, -2.5f),
            "Integration_Red", new Color(0.8f, 0.15f, 0.12f));
        Prop(room.transform, "Pillar_Green", new Vector3(5f, 1.2f, 0f),
            "Integration_Green", new Color(0.15f, 0.7f, 0.25f));
        Prop(room.transform, "Pillar_Yellow", new Vector3(5f, 1.2f, 2.5f),
            "Integration_Yellow", new Color(0.9f, 0.75f, 0.1f));

        var lightObject = new GameObject("Light_Exit");
        lightObject.transform.SetParent(room.transform, false);
        lightObject.transform.localPosition = new Vector3(0f, 3.4f, 0f);
        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.72f, 0.45f);
        light.range = 16f;
        lightObject.AddComponent<HDAdditionalLightData>().SetIntensity(1600f, LightUnit.Lumen);
    }

    /// <summary>
    /// Игрок собирается вручную, а не из PortalPlayer.prefab: тот носит
    /// демонстрационный контроллер модуля, а вся суть этой сцены в контроллере
    /// чужой породы, который держит угол взгляда у себя.
    /// </summary>
    private static Camera CreatePlayer()
    {
        var player = new GameObject("Player");
        player.transform.position = new Vector3(0f, 0.1f, -3f);

        CharacterController controller = player.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.9f, 0f);

        var headObject = new GameObject("Head");
        headObject.transform.SetParent(player.transform, false);
        headObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);

        Camera camera = headObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.nearClipPlane = 0.05f;
        headObject.AddComponent<HDAdditionalCameraData>();

        PortalTraveller traveller = player.AddComponent<PortalTraveller>();
        SetReference(traveller, "viewPoint", headObject.transform);

        ExampleLookController look = player.AddComponent<ExampleLookController>();
        look.body = player.transform;
        look.head = headObject.transform;

        ExampleLookPortalBridge bridge = player.AddComponent<ExampleLookPortalBridge>();
        SetReference(bridge, "look", look);
        SetReference(bridge, "traveller", traveller);

        return camera;
    }

    private static Portal PlacePortal(string name, Vector3 position, float yaw)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = name;
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        return instance.GetComponent<Portal>();
    }

    private static void Wall(
        Transform parent, string name, Vector3 localPosition, Vector3 size, Color color)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall_" + name;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = localPosition;
        wall.transform.localScale = size;
        Paint(wall, "Integration_Wall_" + name, color);
    }

    private static void Prop(
        Transform parent, string name, Vector3 localPosition, string materialName, Color color)
    {
        GameObject prop = GameObject.CreatePrimitive(PrimitiveType.Cube);
        prop.name = name;
        prop.transform.SetParent(parent, false);
        prop.transform.localPosition = localPosition;
        prop.transform.localScale = new Vector3(0.6f, 2.4f, 0.6f);
        Paint(prop, materialName, color);
    }

    private static void Paint(GameObject target, string materialName, Color color)
    {
        string path = MaterialFolder + "/" + materialName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (material == null)
        {
            material = new Material(Shader.Find("HDRP/Lit"));
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", 0.15f);
            AssetDatabase.CreateAsset(material, path);
        }

        target.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    private static void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
    {
        T component = ScriptableObject.CreateInstance<T>();
        component.name = typeof(T).Name;
        component.hideFlags = HideFlags.HideInHierarchy;
        profile.components.Add(component);
        AssetDatabase.AddObjectToAsset(component, profile);
        return component;
    }
}
