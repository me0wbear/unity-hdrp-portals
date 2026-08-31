using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Копирует буфер глубины виртуальной камеры нулевого уровня в текстуру глубины
/// содержимого портала.
///
/// Это замена запросу AOV. Тот выглядел как копирование готового буфера, но HDRP
/// выполняет для каждого запроса AOV отдельный полный рендер камеры: сцена
/// нулевого уровня считалась дважды за кадр, и второй проход стоил около
/// миллисекунды GPU на ровном месте. Здесь глубина снимается с кадра, который
/// камера и так уже посчитала.
///
/// Точка впрыска — перед пост-обработкой виртуальной камеры. К этому моменту
/// прозрачная геометрия уже записала свою глубину, то есть содержимое буфера
/// совпадает с тем, что раньше отдавал AOV DepthStencil; кодировка тоже его —
/// аппаратная глубина в проекции камеры, как её и ждёт композит.
/// </summary>
public sealed class PortalContentDepthCopyPass : CustomPass
{
    /// <summary>
    /// Шейдер лежит в Resources, а не ищется по имени: скрытый шейдер, на
    /// который не ссылается ни один ассет, в сборку плеера не попадает, и
    /// Shader.Find вернул бы там пусто.
    /// </summary>
    private const string ShaderResource = "PortalDepthCopy";

    private static readonly int SourceDepthId = Shader.PropertyToID("_PortalSourceDepth");
    private static readonly int CopyOriginId = Shader.PropertyToID("_PortalCopyOrigin");

    // Камеры, глубину которых нужно снимать, и рендеры, которым она
    // принадлежит. Заполняется рендерами порталов при создании камер нулевого
    // уровня; текстура назначения и область копии запрашиваются у рендера в
    // момент выполнения — область меняется каждый кадр вместе со следом проёма.
    private static readonly Dictionary<Camera, PortalRenderer> Sources =
        new Dictionary<Camera, PortalRenderer>();

    private Material _material;
    private MaterialPropertyBlock _block;

    /// <summary>
    /// Сбрасывает статическое состояние при запуске: при выключенной
    /// перезагрузке домена словарь пережил бы выход из режима игры.
    /// </summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Sources.Clear();
    }

    /// <summary>Подписывает камеру уровня на копирование её глубины.</summary>
    internal static void Register(Camera camera, PortalRenderer renderer)
    {
        if (camera != null && renderer != null)
        {
            Sources[camera] = renderer;
        }
    }

    /// <summary>Снимает подписку. Вызывать до уничтожения текстуры назначения.</summary>
    public static void Unregister(Camera camera)
    {
        if (camera != null)
        {
            Sources.Remove(camera);
        }
    }

    protected override void Setup(ScriptableRenderContext context, CommandBuffer cmd)
    {
        Shader shader = Resources.Load<Shader>(ShaderResource);

        if (shader == null)
        {
            Debug.LogError("[Portal] " + ShaderResource
                + " not found in Resources: content depth will not be captured");
            return;
        }

        _material = CoreUtils.CreateEngineMaterial(shader);
        _block = new MaterialPropertyBlock();
    }

    protected override void Execute(CustomPassContext context)
    {
        if (_material == null)
        {
            return;
        }

        // Проход глобальный и вызывается для каждой камеры кадра; работа есть
        // только у виртуальных камер нулевого уровня порталов.
        if (!Sources.TryGetValue(context.hdCamera.camera, out PortalRenderer renderer)
            || renderer == null)
        {
            return;
        }

        RTHandle destination = renderer.ContentDepthTarget;
        Rect viewport = renderer.ContentCopyViewport;
        if (destination == null || viewport.width < 1f || viewport.height < 1f)
        {
            return;
        }

        // Камера, ограниченная областью проёма, рисует в свой вьюпорт внутри
        // таргета, а внутренний буфер глубины пайплайна начинается с нулевого
        // пикселя. Копия кладётся в тот же вьюпорт, что и цвет, со сдвигом
        // выборки источника на его начало — так глубина совпадает с цветом
        // пиксель в пиксель. Масштабирования нет: плотность пикселей одна.
        CoreUtils.SetRenderTarget(context.cmd, destination, ClearFlag.None);
        context.cmd.SetViewport(viewport);
        _block.SetTexture(SourceDepthId, context.cameraDepthBuffer);
        _block.SetVector(CopyOriginId, new Vector4(viewport.x, viewport.y, 0f, 0f));
        context.cmd.DrawProcedural(
            Matrix4x4.identity, _material, 0, MeshTopology.Triangles, 3, 1, _block);
    }

    protected override void Cleanup()
    {
        CoreUtils.Destroy(_material);
        _material = null;
    }
}
