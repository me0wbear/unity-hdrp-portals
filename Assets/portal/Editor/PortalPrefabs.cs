using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Собирает готовые к перетаскиванию префабы модуля.
///
/// Собираются кодом по той же причине, что и материалы в
/// <see cref="PortalModuleAssets"/>: связи между объектами хранятся ссылками на
/// внутренние идентификаторы, и написанный руками текст префаба ломается от
/// любой опечатки в них молча. Сгенерированный собирается заново одной командой
/// и всегда согласован с текущими полями компонентов.
/// </summary>
public static class PortalPrefabs
{
    private const string PortalPath = "Assets/portal/Portal.prefab";
    private const string PairPath = "Assets/portal/PortalPair.prefab";
    private const string PlayerPath = "Assets/portal/PortalPlayer.prefab";

    /// <summary>
    /// На сколько разносится пара по умолчанию. Пара, стоящая вплотную, видна
    /// сама себе: каждый проём попадает в кадр виртуальной камеры другого, и
    /// вместо двух видов получается коридор из отражений. Тридцать метров
    /// заведомо больше любой комнаты, в которой пару ставят на пробу.
    /// </summary>
    private const float PairSeparation = 30f;

    /// <summary>Высота центра проёма над полом. Проём три метра, пол на нуле.</summary>
    private const float OpeningHeight = 1.5f;

    [MenuItem("Tools/Portals/Rebuild Prefabs")]
    public static void Build()
    {
        GameObject portal = AssetDatabase.LoadAssetAtPath<GameObject>(PortalPath);
        if (portal == null)
        {
            Debug.LogError("[PortalPrefabs] " + PortalPath + " not found");
            return;
        }

        BuildPair(portal);
        BuildPlayer();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    /// <summary>Точка входа для headless-сборки: собрать и выйти.</summary>
    public static void BuildAndExit()
    {
        Build();
        EditorApplication.Exit(0);
    }

    /// <summary>
    /// Пара порталов, уже связанных друг с другом в обе стороны.
    ///
    /// Оба конца — вложенные экземпляры одиночного префаба, поэтому правка
    /// одиночного расходится и сюда. Остаётся расставить концы по местам и выдать
    /// им камеру: это делает пункт меню Wire Scene.
    /// </summary>
    private static void BuildPair(GameObject portalPrefab)
    {
        var root = new GameObject("PortalPair");

        Portal first = AddEnd(portalPrefab, root.transform, "Portal_A", 0f);
        Portal second = AddEnd(portalPrefab, root.transform, "Portal_B", PairSeparation);

        first.exitPortal = second;
        second.exitPortal = first;

        PrefabUtility.SaveAsPrefabAsset(root, PairPath);
        Object.DestroyImmediate(root);

        Debug.Log("[PortalPrefabs] built " + PairPath);
    }

    private static Portal AddEnd(
        GameObject portalPrefab, Transform parent, string name, float offsetX)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(portalPrefab, parent);
        instance.name = name;

        // Разворот на пол-оборота, потому что лицевая сторона портала — его
        // локальная ось +Z, а пара, поставленная лицом в одну сторону, смотрит
        // в затылок сама себе.
        instance.transform.SetLocalPositionAndRotation(
            new Vector3(offsetX, OpeningHeight, 0f), Quaternion.Euler(0f, 180f, 0f));

        return instance.GetComponent<Portal>();
    }

    /// <summary>
    /// Игрок, которым можно ходить сквозь порталы сразу после перетаскивания.
    ///
    /// Собран из того же, что описано в SETUP.md: контроллер персонажа,
    /// путешественник, мост камеры и камера на уровне глаз. Управление —
    /// демонстрационное, для проверки; в своём проекте его меняют на своё, а
    /// остальные компоненты остаются как есть.
    /// </summary>
    private static void BuildPlayer()
    {
        var root = new GameObject("PortalPlayer");

        CharacterController controller = root.AddComponent<CharacterController>();
        controller.height = 1.8f;
        controller.radius = 0.3f;
        controller.center = new Vector3(0f, 0.9f, 0f);

        var headObject = new GameObject("Head");
        headObject.transform.SetParent(root.transform, false);
        headObject.transform.localPosition = new Vector3(0f, 1.65f, 0f);

        Camera camera = headObject.AddComponent<Camera>();
        camera.tag = "MainCamera";
        camera.nearClipPlane = 0.05f;
        headObject.AddComponent<HDAdditionalCameraData>();

        PortalTraveller traveller = root.AddComponent<PortalTraveller>();
        PortalCameraBridge bridge = root.AddComponent<PortalCameraBridge>();
        PortalDemoController demo = root.AddComponent<PortalDemoController>();

        // Поля закрытые, поэтому связываются через сериализованное представление.
        // Оставлять их пустыми нельзя: часть из них компоненты добирают сами
        // только в редакторе, при добавлении компонента вручную, и в собранном
        // префабе так и остались бы пустыми.
        SetReference(traveller, "viewPoint", headObject.transform);
        SetReference(demo, "head", headObject.transform);
        SetReference(bridge, "traveller", traveller);
        SetReference(bridge, "gameplayCamera", camera);

        PrefabUtility.SaveAsPrefabAsset(root, PlayerPath);
        Object.DestroyImmediate(root);

        Debug.Log("[PortalPrefabs] built " + PlayerPath);
    }

    private static void SetReference(Object target, string field, Object value)
    {
        var serialized = new SerializedObject(target);
        SerializedProperty property = serialized.FindProperty(field);

        if (property == null)
        {
            Debug.LogError("[PortalPrefabs] " + target.GetType().Name
                + " has no serialized field " + field);
            return;
        }

        property.objectReferenceValue = value;
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }
}
