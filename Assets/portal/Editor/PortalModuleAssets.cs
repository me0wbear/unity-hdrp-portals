using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Собирает ассеты модуля, которые нельзя держать в репозитории как текст:
/// материал ссылается на шейдер по GUID, а GUID выдаёт импортёр.
/// Поэтому материал создаётся из кода, а не пишется руками.
/// </summary>
public static class PortalModuleAssets
{
    private const string ShaderName = "Portals/PortalScreen";
    private const string MaterialPath = "Assets/portal/PortalScreenMat.mat";

    private const string DepthShaderName = "Hidden/Portals/ContentDepth";

    // В Resources, потому что скрытый шейдер, на который не ссылается ни один
    // ассет сцены, в сборку плеера не попадает. Так модуль остаётся
    // самодостаточным и не требует правок в настройках графики проекта.
    private const string DepthMaterialPath = "Assets/portal/Resources/PortalContentDepthMat.mat";

    [MenuItem("Tools/Portals/Rebuild Module Assets")]
    public static void Build()
    {
        Shader shader = Shader.Find(ShaderName);
        if (shader == null)
        {
            Debug.LogError("[PortalModuleAssets] shader " + ShaderName + " not found");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));

        Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }
        else
        {
            material.shader = shader;
        }

        BuildDepthMaterial();

        // Пока вид не назначен, квад заливается цветом заглушки. Тот же цвет
        // приходит на последнем уровне рекурсии.
        material.SetFloat("_HasTexture", 0f);
        material.SetColor("_FallbackColor", new Color(0.02f, 0.02f, 0.03f, 1f));

        // Квад непрозрачный и рисуется в общей очереди с остальной геометрией:
        // так его подхватывают предпроход глубины и пост-обработка кадра.
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry;

        EditorUtility.SetDirty(material);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[PortalModuleAssets] saved " + MaterialPath
            + " on shader " + material.shader.name);
    }

    /// <summary>
    /// Материал прохода, который подменяет глубину и вектор движения в буферах
    /// главной камеры. Настраивать в нём нечего: все параметры приходят из блока
    /// свойств квада, материал нужен только как ссылка, удерживающая шейдер в сборке.
    /// </summary>
    private static void BuildDepthMaterial()
    {
        Shader depthShader = Shader.Find(DepthShaderName);
        if (depthShader == null)
        {
            Debug.LogError("[PortalModuleAssets] shader " + DepthShaderName + " not found");
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(DepthMaterialPath));

        Material depthMaterial = AssetDatabase.LoadAssetAtPath<Material>(DepthMaterialPath);
        if (depthMaterial == null)
        {
            depthMaterial = new Material(depthShader);
            AssetDatabase.CreateAsset(depthMaterial, DepthMaterialPath);
        }
        else
        {
            depthMaterial.shader = depthShader;
        }

        EditorUtility.SetDirty(depthMaterial);
        Debug.Log("[PortalModuleAssets] saved " + DepthMaterialPath);
    }

    /// <summary>Точка входа для headless-сборки: собрать и выйти.</summary>
    public static void BuildAndExit()
    {
        Build();
        EditorApplication.Exit(0);
    }
}
