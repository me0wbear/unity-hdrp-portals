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

    /// <summary>Точка входа для headless-сборки: собрать и выйти.</summary>
    public static void BuildAndExit()
    {
        Build();
        EditorApplication.Exit(0);
    }
}
