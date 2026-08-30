using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Generates the ready-made portal prefab that ships with the module, so someone integrating
/// portals into an existing scene drags in one object instead of assembling five components
/// by hand. Everything the prefab can carry is pre-wired; only the scene-specific references
/// (the paired portal and the gameplay camera) are left for the user to fill in.
/// </summary>
public static class PortalPrefabBuilder
{
    private const string PrefabPath = "Assets/portal/Portal.prefab";
    private const float DefaultWidth = 2f;
    private const float DefaultHeight = 3f;

    [MenuItem("PortalLab/Rebuild Portal Prefab")]
    public static void Build()
    {
        var root = new GameObject("Portal");
        try
        {
            BoxCollider trigger = root.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(DefaultWidth, DefaultHeight, 1.2f);

            var screenObject = new GameObject("Screen");
            screenObject.transform.SetParent(root.transform, false);
            screenObject.transform.localPosition = Vector3.zero;
            screenObject.transform.localRotation = Quaternion.identity;
            screenObject.transform.localScale = new Vector3(DefaultWidth, DefaultHeight, 1f);

            MeshFilter filter = screenObject.AddComponent<MeshFilter>();
            filter.sharedMesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            MeshRenderer renderer = screenObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial =
                AssetDatabase.LoadAssetAtPath<Material>("Assets/portal/PortalScreenMat.mat");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Portal portal = root.AddComponent<Portal>();
            portal.screen = renderer;

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[PortalPrefabBuilder] saved " + PrefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    public static void BuildAndExit()
    {
        Build();
        EditorApplication.Exit(0);
    }
}
