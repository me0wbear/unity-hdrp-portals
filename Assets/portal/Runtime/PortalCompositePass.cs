using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.HighDefinition;

/// <summary>
/// Записывает в буфер глубины главной камеры расстояние до того, что видно
/// сквозь порталы.
///
/// Векторы движения проход намеренно не трогает. Снять их с виртуальной камеры
/// можно, и модуль это умел, но то, что она отдаёт, оказалось непригодным: на
/// полностью неподвижной сцене значения выходили ненулевыми, и временное
/// сглаживание собирало кадр по несуществующему движению — вид в проёме мылился,
/// пока всё вокруг оставалось резким. Проверено подстановкой нуля: проём
/// становится резким, подмена глубины при этом продолжает работать. Пока
/// движение остаётся тем, что посчитал сам квад. Расплата за это — лёгкий след
/// за содержимым проёма при быстром движении камеры, потому что квад и то, что
/// видно сквозь него, смещаются по экрану по-разному.
///
/// Точка внедрения — после непрозрачной геометрии. Раньше нельзя: квад портала
/// рисуется в проходе непрозрачных и не прошёл бы проверку глубины против уже
/// подменённого буфера. Заодно так правильнее по туману: виртуальная камера уже
/// нарисовала туман своей стороны прямо в содержимом, и накладывать поверх ещё
/// и туман этой стороны значило бы посчитать его дважды.
///
/// Глубина резкости, размытие в движении и временное сглаживание работают в
/// пост-обработке, то есть позже, и подмену видят.
///
/// Параметры содержимого лежат в блоке свойств самого квада: DrawRenderer блока
/// не принимает, а рисуется здесь именно тот рендерер, на котором блок висит.
/// </summary>
public sealed class PortalCompositePass : CustomPass
{
    /// <summary>
    /// Материал лежит в Resources, а не ищется по имени шейдера: скрытый шейдер,
    /// на который не ссылается ни один ассет, в сборку плеера не попадает, и
    /// Shader.Find вернул бы там пусто. Через Resources модуль остаётся
    /// самодостаточным и не требует правок в настройках графики проекта.
    /// </summary>
    private const string MaterialResource = "PortalContentDepthMat";

    private Material _material;

    protected override void Setup(ScriptableRenderContext context, CommandBuffer cmd)
    {
        _material = Resources.Load<Material>(MaterialResource);

        if (_material == null)
        {
            Debug.LogError("[Portal] " + MaterialResource
                + " not found in Resources: content depth will not be written");
        }
    }

    protected override void Execute(CustomPassContext context)
    {
        if (_material == null)
        {
            return;
        }

        IReadOnlyList<Portal> portals = PortalSystem.Active;
        bool targetsBound = false;

        for (int i = 0; i < portals.Count; i++)
        {
            Portal portal = portals[i];
            if (portal == null || !portal.writeContentDepth || portal.screen == null)
            {
                continue;
            }

            // Подменять глубину имеет смысл только той камере, для которой этот
            // портал считался: для чужой камеры содержимое посчитано не с той позы.
            if (!ReferenceEquals(portal.playerCamera, context.hdCamera.camera))
            {
                continue;
            }

            if (!PortalSystem.HasContentBuffers(portal))
            {
                continue;
            }

            if (!targetsBound)
            {
                // Цветом привязан буфер векторов движения, и шейдер в него
                // ничего не пишет: у прохода есть только выход глубины. Именно
                // он, а не буфер цвета: привязка буфера цвета на этой точке
                // впрыска ломает кадр целиком, он уходит в черноту.
                CoreUtils.SetRenderTarget(
                    context.cmd,
                    context.cameraMotionVectorsBuffer,
                    context.cameraDepthBuffer,
                    ClearFlag.None);
                targetsBound = true;
            }

            context.cmd.DrawRenderer(portal.screen, _material, 0, 0);
        }
    }

    protected override void Cleanup()
    {
        // Материал загружен из Resources и принадлежит проекту, уничтожать его нельзя.
        _material = null;
    }
}
