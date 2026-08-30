using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;
using UnityEngine.SceneManagement;

/// <summary>
/// Собирает игровую сцену-песочницу: открыть, нажать Play и ходить.
///
/// Ничего не замеряет и ничего не пишет на диск во время игры — этим занимается
/// лаборатория в LabTools. Здесь только то, на что нужно посмотреть глазами:
/// проход между двумя разными по цвету и свету комнатами и предметы, которые
/// можно протолкнуть сквозь проём.
/// </summary>
public static class PortalSandboxScene
{
    public const string ScenePath = "Assets/portal/Examples/PortalSandbox.unity";

    private const string Folder = "Assets/portal/Examples";
    private const string ProfilePath = Folder + "/SandboxVolume.asset";
    private const string MaterialFolder = Folder + "/Materials";

    /// <summary>Насколько далеко вторая комната. Дальше видимости пешком.</summary>
    private const float RoomSeparation = 40f;

    [MenuItem("Tools/Portals/Build Sandbox Scene")]
    public static void Build()
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();

        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        CreateLightingAndSky();
        CreateGround();

        CreateRoomA();
        CreateRoomB();

        Portal walkThroughA = PlacePortal(
            "Portal_ToRoomB", new Vector3(0f, 1.5f, 6f), 180f);
        Portal walkThroughB = PlacePortal(
            "Portal_ToRoomA", new Vector3(RoomSeparation, 1.5f, -6f), 0f);
        LinkPair(walkThroughA, walkThroughB);

        CreateProps();

        Camera playerCamera = CreatePlayer();
        WireCameraToEveryPortal(playerCamera);

        EditorSceneManager.SaveScene(scene, ScenePath);
        Debug.Log("[PortalSandbox] built " + ScenePath);
    }

    /// <summary>Точка входа для headless-сборки: собрать и выйти.</summary>
    public static void BuildAndExit()
    {
        Build();
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Солнце и небо. Экспозиция намеренно фиксированная: автоматическая ползёт
    /// от того, куда смотришь, и на глаз уже не понять, портал изменил яркость
    /// или адаптация. Тонемап ACES — тот же, что в большинстве проектов на HDRP.
    /// </summary>
    private static void CreateLightingAndSky()
    {
        var sunObject = new GameObject("Sun");
        sunObject.transform.rotation = Quaternion.Euler(48f, 30f, 0f);
        Light sun = sunObject.AddComponent<Light>();
        sun.type = LightType.Directional;
        sun.color = new Color(1f, 0.96f, 0.9f);
        HDLightData(sun, 60000f, LightUnit.Lux);

        AssetDatabase.DeleteAsset(ProfilePath);
        VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, ProfilePath);

        // Небо физическое, а не градиентное. Градиент задаётся обычными цветами,
        // а они попадают в кадр как абсолютная яркость: при фиксированной
        // экспозиции 11 такие значения делятся на две тысячи и небо выходит
        // чёрным. Физическое небо считает яркость само и с экспозицией согласуется.
        var environment = AddOverride<VisualEnvironment>(profile);
        environment.skyType.overrideState = true;
        environment.skyType.value = (int)SkyType.PhysicallyBased;

        var sky = AddOverride<PhysicallyBasedSky>(profile);
        sky.groundTint.overrideState = true;
        sky.groundTint.value = new Color(0.22f, 0.21f, 0.2f);

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

        // Потолок уровней вынесен в сцену, чтобы его было где подкрутить: со
        // значением по умолчанию хватает и на пару порталов, и на запас, но
        // добавив свои проёмы, поднимать его придётся именно здесь.
        var budgetObject = new GameObject("Portal Budget");
        SetPrivateInt(budgetObject.AddComponent<PortalBudget>(), "levels", 16);
    }

    private static void SetPrivateInt(Object target, string field, int value)
    {
        var serialized = new SerializedObject(target);
        serialized.FindProperty(field).intValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void CreateGround()
    {
        GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(12f, 1f, 12f);
        ground.transform.position = new Vector3(RoomSeparation * 0.5f, 0f, 0f);
        Paint(ground, "Ground", new Color(0.22f, 0.22f, 0.24f));
    }

    /// <summary>
    /// Комната отправления: холодная, серо-синяя, свет сверху.
    /// </summary>
    private static void CreateRoomA()
    {
        var room = new GameObject("Room_A_Cold");
        Color wall = new Color(0.28f, 0.33f, 0.42f);

        Wall(room.transform, "Wall_Left", new Vector3(-5f, 2f, 0f), new Vector3(0.4f, 4f, 12f), wall);
        Wall(room.transform, "Wall_Right", new Vector3(5f, 2f, 0f), new Vector3(0.4f, 4f, 12f), wall);
        Wall(room.transform, "Wall_Back", new Vector3(0f, 2f, -6f), new Vector3(10f, 4f, 0.4f), wall);

        // Стена с проёмом: два простенка по бокам от портала. Портал шириной два
        // метра, поэтому простенки по четыре.
        Wall(room.transform, "Wall_Front_Left", new Vector3(-3f, 2f, 6f), new Vector3(4f, 4f, 0.4f), wall);
        Wall(room.transform, "Wall_Front_Right", new Vector3(3f, 2f, 6f), new Vector3(4f, 4f, 0.4f), wall);
        Wall(room.transform, "Wall_Front_Top", new Vector3(0f, 3.5f, 6f), new Vector3(2f, 1f, 0.4f), wall);

        PointLight(room.transform, "Light_A", new Vector3(0f, 3.4f, -1f),
            new Color(0.75f, 0.85f, 1f), 900f, 14f);

        // Ориентир: по нему видно, что в проёме именно другая комната.
        Prop(room.transform, PrimitiveType.Capsule, "Marker_A",
            new Vector3(-3f, 1f, -3f), Vector3.one, "Marker_Cold", new Color(0.2f, 0.5f, 0.9f));
    }

    /// <summary>
    /// Комната назначения: тёплая, с другим светом и другими предметами. Разница
    /// нужна затем, чтобы в проёме было очевидно, что там другое место.
    /// </summary>
    private static void CreateRoomB()
    {
        var room = new GameObject("Room_B_Warm");
        room.transform.position = new Vector3(RoomSeparation, 0f, 0f);
        Color wall = new Color(0.45f, 0.3f, 0.22f);

        Wall(room.transform, "Wall_Left", new Vector3(-5f, 2f, 0f), new Vector3(0.4f, 4f, 12f), wall);
        Wall(room.transform, "Wall_Right", new Vector3(5f, 2f, 0f), new Vector3(0.4f, 4f, 12f), wall);
        Wall(room.transform, "Wall_Far", new Vector3(0f, 2f, 6f), new Vector3(10f, 4f, 0.4f), wall);

        Wall(room.transform, "Wall_Near_Left", new Vector3(-3f, 2f, -6f), new Vector3(4f, 4f, 0.4f), wall);
        Wall(room.transform, "Wall_Near_Right", new Vector3(3f, 2f, -6f), new Vector3(4f, 4f, 0.4f), wall);
        Wall(room.transform, "Wall_Near_Top", new Vector3(0f, 3.5f, -6f), new Vector3(2f, 1f, 0.4f), wall);

        PointLight(room.transform, "Light_B", new Vector3(0f, 3.4f, 1f),
            new Color(1f, 0.72f, 0.45f), 1400f, 14f);

        // Три разноцветных столба у дальней стены. Их хорошо видно из проёма и по
        // ним сразу понятно, честно ли портал показывает ту сторону.
        Prop(room.transform, PrimitiveType.Cube, "Pillar_Red",
            new Vector3(-2.5f, 1.2f, 4f), new Vector3(0.6f, 2.4f, 0.6f),
            "Pillar_Red", new Color(0.8f, 0.15f, 0.12f));
        Prop(room.transform, PrimitiveType.Cube, "Pillar_Green",
            new Vector3(0f, 1.2f, 4f), new Vector3(0.6f, 2.4f, 0.6f),
            "Pillar_Green", new Color(0.15f, 0.7f, 0.25f));
        Prop(room.transform, PrimitiveType.Cube, "Pillar_Yellow",
            new Vector3(2.5f, 1.2f, 4f), new Vector3(0.6f, 2.4f, 0.6f),
            "Pillar_Yellow", new Color(0.9f, 0.75f, 0.1f));
    }    /// <summary>Предметы с физикой: их можно толкать и проталкивать сквозь проём.</summary>
    private static void CreateProps()
    {
        var box = new GameObject("Throwables");

        for (int i = 0; i < 5; i++)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Crate_" + i;
            cube.transform.SetParent(box.transform, false);
            cube.transform.position = new Vector3(-2f + i, 0.3f, 3f);
            cube.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);

            Rigidbody body = cube.AddComponent<Rigidbody>();
            body.mass = 2f;

            // Путешественник нужен и предметам: без него ящик, брошенный в
            // проём, до другой стороны не доедет.
            cube.AddComponent<PortalTraveller>();

            Paint(cube, "Crate", new Color(0.7f, 0.65f, 0.5f));
        }
    }

    private static Camera CreatePlayer()
    {
        GameObject prefab =
            AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/PortalPlayer.prefab");

        if (prefab == null)
        {
            Debug.LogError("[PortalSandbox] PortalPlayer.prefab not found, run Rebuild Prefabs first");
            return null;
        }

        var player = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        player.name = "Player";
        player.transform.position = new Vector3(0f, 0.1f, -3f);

        return player.GetComponentInChildren<Camera>();
    }

    private static void WireCameraToEveryPortal(Camera camera)
    {
        // Запасной поиск по сцене. Ссылка на камеру — единственное, что портал не
        // может взять из префаба, и портал без неё молча показывает цвет заглушки:
        // ошибка выглядит как чёрный проём, а не как ошибка.
        if (camera == null)
        {
            camera = Object.FindFirstObjectByType<Camera>();
        }

        if (camera == null)
        {
            Debug.LogError("[PortalSandbox] no camera in the scene, portals will stay blank");
            return;
        }

        int wired = 0;
        foreach (Portal portal in Object.FindObjectsByType<Portal>(FindObjectsSortMode.None))
        {
            portal.playerCamera = camera;
            EditorUtility.SetDirty(portal);
            wired++;
        }

        Debug.Log("[PortalSandbox] wired " + wired + " portals to " + camera.name);
    }

    private static Portal PlacePortal(string name, Vector3 position, float yaw)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/portal/Portal.prefab");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = name;
        instance.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
        return instance.GetComponent<Portal>();
    }

    private static void LinkPair(Portal first, Portal second)
    {
        first.exitPortal = second;
        second.exitPortal = first;
    }

    private static void Wall(
        Transform parent, string name, Vector3 localPosition, Vector3 size, Color color)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = name;
        wall.transform.SetParent(parent, false);
        wall.transform.localPosition = localPosition;
        wall.transform.localScale = size;
        Paint(wall, "Wall_" + ColorKey(color), color);
    }

    private static void Prop(
        Transform parent, PrimitiveType type, string name,
        Vector3 localPosition, Vector3 scale, string materialName, Color color)
    {
        GameObject prop = GameObject.CreatePrimitive(type);
        prop.name = name;
        prop.transform.SetParent(parent, false);
        prop.transform.localPosition = localPosition;
        prop.transform.localScale = scale;
        Paint(prop, materialName, color);
    }

    private static void PointLight(
        Transform parent, string name, Vector3 localPosition,
        Color color, float intensity, float range)
    {
        var lightObject = new GameObject(name);
        lightObject.transform.SetParent(parent, false);
        lightObject.transform.localPosition = localPosition;

        Light light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        HDLightData(light, intensity, LightUnit.Lumen);
    }

    /// <summary>
    /// Яркость задаётся через HDRP: обычное поле intensity здесь ничего не значит,
    /// и свет, выставленный мимо HDAdditionalLightData, в игре не совпадёт с тем,
    /// что видно в редакторе. Единицы у типов света разные и подставляются не
    /// автоматически: солнце меряется освещённостью в люксах, лампа — световым
    /// потоком в люменах, и перевод между ними не определён.
    /// </summary>
    private static void HDLightData(Light light, float intensity, LightUnit unit)
    {
        HDAdditionalLightData data = light.gameObject.AddComponent<HDAdditionalLightData>();
        data.SetIntensity(intensity, unit);
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

    private static string ColorKey(Color color)
    {
        return Mathf.RoundToInt(color.r * 255f) + "_"
            + Mathf.RoundToInt(color.g * 255f) + "_"
            + Mathf.RoundToInt(color.b * 255f);
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
