#ifndef PORTAL_SCREEN_DATA_INCLUDED
#define PORTAL_SCREEN_DATA_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Sampling/SampleUVMapping.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/BuiltinUtilities.hlsl"
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/MaterialUtilities.hlsl"

// Вид, посчитанный виртуальной камерой портала. Объявлен вне UnityPerMaterial,
// потому что задаётся через MaterialPropertyBlock: материал у всех порталов общий,
// а картинка у каждого своя.
TEXTURE2D(_MainTex);
float4 _MainTex_TexelSize;
float  _HasTexture;
float4 _FallbackColor;

/// Заполняет данные поверхности для прохода unlit.
///
/// Вид выбирается по экранным координатам фрагмента, а не по развёртке квада.
/// Таргет виртуальной камеры совпадает с экраном, поэтому пиксель квада берёт
/// ровно тот пиксель, который виртуальная камера нарисовала для того же места
/// экрана: ни перспективных искажений, ни зависимости от развёртки.
///
/// Результат кладётся в emissive, а не в color. Проход unlit выводит
/// surfaceData.color с множителем де-экспозиции, а builtinData.emissiveColor —
/// с текущей экспозицией камеры. Виртуальная камера рендерит с фиксированной
/// экспозицией, поэтому её цвет нужно привести именно к экспозиции главной
/// камеры, иначе яркость в проёме разойдётся с яркостью вокруг него.
void GetSurfaceAndBuiltinData(
    FragInputs input,
    float3 V,
    inout PositionInputs posInput,
    out SurfaceData surfaceData,
    out BuiltinData builtinData)
{
    float2 screenUv = posInput.positionNDC.xy;
    float3 view = SAMPLE_TEXTURE2D_LOD(_MainTex, s_linear_clamp_sampler, screenUv, 0).rgb;

    // На последнем уровне рекурсии таргета нет: дальше заданной глубины
    // содержимое всё равно не разглядеть, и проём заливается ровным цветом.
    float3 color = lerp(_FallbackColor.rgb, view, _HasTexture);

    ZERO_INITIALIZE(SurfaceData, surfaceData);
    surfaceData.color = 0.0;
    surfaceData.normalWS = 0.0;

    ZERO_BUILTIN_INITIALIZE(builtinData);
    builtinData.opacity = 1.0;
    builtinData.emissiveColor = color;

#if defined(DEBUG_DISPLAY)
    builtinData.renderingLayers = GetMeshRenderingLayerMask();
#endif

    ApplyDebugToBuiltinData(builtinData);
}

#endif // PORTAL_SCREEN_DATA_INCLUDED
