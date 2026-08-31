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

// Прямоугольник кадра наблюдателя, в котором лежит содержимое _MainTex:
// xy — угол, zw — размер. Уровень, ограниченный областью проёма, заполняет
// только её; полный кадр — (0, 0, 1, 1).
float4 _PortalContentRect;

// Прямоугольник кадра, который рисует текущая камера. Полный кадр для главной
// камеры; для камеры уровня, ограниченной областью проёма, — её область.
// Задаётся системой порталов перед рендером каждой камеры.
float4 _PortalCameraRect;

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
    // Выборка по нормализованным экранным координатам, а не по номеру текселя:
    // делитель разрешения портала — открытая настройка, и при значении больше
    // единицы таргет меньше экрана, а по номеру текселя выборка ушла бы за его
    // границы почти на всей площади проёма.
    //
    // Координаты камеры уровня переводятся в доли полного кадра наблюдателя:
    // её вьюпорт занимает лишь область проёма, и содержимое всех уровней
    // адресуется в одних и тех же долях кадра. Выборка прижимается к границе
    // области содержимого, чтобы фильтрация на краю не поднимала пиксели,
    // которых уровень не рисовал.
    float2 screenUv = _PortalCameraRect.xy + posInput.positionNDC.xy * _PortalCameraRect.zw;
    float2 guard = 0.5 * _MainTex_TexelSize.xy;
    screenUv = clamp(
        screenUv,
        _PortalContentRect.xy + guard,
        _PortalContentRect.xy + _PortalContentRect.zw - guard);
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
