// Материал квада портала.
//
// Написан вручную, а не собран в Shader Graph, по двум причинам: граф не
// умеет выбирать текстуру по экранным координатам без обходных путей, и его
// нельзя ни прочитать, ни отревьюить в диффе. Каркас проходов повторяет
// HDRP/Unlit из пакета: те же LightMode, те же include, та же разметка
// стенсила. Отличается только выдача данных поверхности, она в
// PortalScreenData.hlsl.
Shader "Portals/PortalScreen"
{
    Properties
    {
        [MainTexture] _MainTex("Вид через портал", 2D) = "black" {}
        [HideInInspector] _HasTexture("Вид назначен", Float) = 0
        [HDR] _FallbackColor("Цвет за пределом рекурсии", Color) = (0.02, 0.02, 0.03, 1)

        // Дальше — свойства, которые ожидает материальный слой HDRP/Unlit.
        // Квад всегда непрозрачный и всегда виден только с лицевой стороны,
        // поэтому состояние проходов задано константами, а не ссылками на
        // свойства: настраивать тут нечего, а спрятанных переключателей,
        // способных незаметно сломать композит, быть не должно.
        [HideInInspector] _UnlitColor("Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _UnlitColorMap("ColorMap", 2D) = "white" {}
        [HideInInspector] _EmissiveColor("EmissiveColor", Color) = (0, 0, 0)
        [HideInInspector] _EmissiveColorMap("EmissiveColorMap", 2D) = "white" {}
        [HideInInspector] _EmissiveExposureWeight("Emissive Pre Exposure", Float) = 1.0
        [HideInInspector] _DistortionVectorMap("DistortionVectorMap", 2D) = "black" {}
        [HideInInspector] _AlphaCutoff("Alpha Cutoff", Float) = 0.5
        [HideInInspector] _AlphaRemapMin("AlphaRemapMin", Float) = 0.0
        [HideInInspector] _AlphaRemapMax("AlphaRemapMax", Float) = 1.0
        [HideInInspector] _BlendMode("__blendmode", Float) = 0.0
        [HideInInspector] _SurfaceType("__surfacetype", Float) = 0.0
        [HideInInspector] _EmissionColor("Color", Color) = (1, 1, 1)
        [HideInInspector] _IncludeIndirectLighting("Include Indirect Lighting", Float) = 1.0
        [HideInInspector] _DistortionScale("Distortion Scale", Float) = 1
        [HideInInspector] _DistortionVectorScale("Distortion Vector Scale", Float) = 2
        [HideInInspector] _DistortionVectorBias("Distortion Vector Bias", Float) = -1
        [HideInInspector] _DistortionBlurScale("Distortion Blur Scale", Float) = 1
        [HideInInspector] _DistortionBlurRemapMin("DistortionBlurRemapMin", Float) = 0.0
        [HideInInspector] _DistortionBlurRemapMax("DistortionBlurRemapMax", Float) = 1.0
        [HideInInspector] _RenderQueueType("__renderQueueType", Float) = 1.0
    }

    HLSLINCLUDE

    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/FragInputs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPass.cs.hlsl"
    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/UnlitProperties.hlsl"

    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" "RenderType" = "HDUnlitShader" }

        // Cull Front во всех проходах. Встроенный Quad в Unity имеет нормали в
        // минус Z, а локальная ось +Z портала смотрит на игрока, поэтому
        // отсечение передних граней оставляет квад видимым ровно с лицевой
        // стороны портала и убирает его с тыльной.
        //
        // Это не косметика. Выйдя из портала, наблюдатель оказывается позади
        // его плоскости, и портал в этот момент не рендерится. Двусторонний квад
        // встал бы перед лицом чёрным прямоугольником — тем самым швом, ради
        // устранения которого всё и делается. Отсечение решает это само и
        // отдельно для каждой камеры: виртуальная камера, видящая портал с
        // лицевой стороны, его по-прежнему видит, и рекурсия не ломается.

        // Проход глубины. Нужен, чтобы квад попал в предпроход глубины: без него
        // основной проход не пройдёт сравнение с буфером и квад не нарисуется.
        Pass
        {
            Name "DepthForwardOnly"
            Tags { "LightMode" = "DepthForwardOnly" }

            Stencil
            {
                WriteMask 8   // StencilUsage.TraceReflectionRay
                Ref 0
                Comp Always
                Pass Replace
            }

            Cull Front
            ZWrite On

            HLSLPROGRAM

            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH

            #define SHADERPASS SHADERPASS_DEPTH_ONLY

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/ShaderPass/UnlitDepthPass.hlsl"
            #include "Assets/portal/Shaders/PortalScreenData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }

        // Векторы движения. Здесь квад помечается в стенсиле как объект со своим
        // движением, чтобы HDRP не посчитала для него движение одной лишь камеры.
        // Само значение перезаписывает PortalCompositePass: движение того, что
        // видно сквозь проём, не совпадает с движением самого квада.
        Pass
        {
            Name "MotionVectors"
            Tags { "LightMode" = "MotionVectors" }

            Stencil
            {
                // 96 = 32 | 64. Бит движущегося объекта нужен пайплайну, чтобы он
                // не переписал наши векторы движения своими, посчитанными по
                // движению одной лишь камеры. Пользовательский бит помечает
                // видимые пиксели проёма для прохода подмены глубины. Ставятся
                // оба здесь же, потому что этот проход идёт по уже собранному
                // буферу глубины: помечено будет ровно то, что действительно
                // видно, независимо от порядка отрисовки.
                WriteMask 96  // StencilUsage.ObjectMotionVector | StencilUsage.UserBit0
                Ref 96
                Comp Always
                Pass Replace
            }

            Cull Front
            ZWrite On

            HLSLPROGRAM

            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ WRITE_MSAA_DEPTH

            #define SHADERPASS SHADERPASS_MOTION_VECTORS

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/ShaderPass/UnlitSharePass.hlsl"
            #include "Assets/portal/Shaders/PortalScreenData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassMotionVectors.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }

        // Основной проход. Unlit в HDRP всегда идёт в forward.
        Pass
        {
            Name "ForwardOnly"
            Tags { "LightMode" = "ForwardOnly" }

            Blend One Zero
            Blend 1 SrcAlpha OneMinusSrcAlpha  // цель 1 нужна обратной связи виртуального текстурирования
            ZWrite On
            ZTest LEqual

            Stencil
            {
                WriteMask 3   // RequiresDeferredLighting | SubsurfaceScattering
                Ref 0
                Comp Always
                Pass Replace
            }

            Cull Front

            HLSLPROGRAM

            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma multi_compile_instancing
            #pragma multi_compile _ DEBUG_DISPLAY

            #ifdef DEBUG_DISPLAY
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Debug/DebugDisplay.hlsl"
            #endif

            #define SHADERPASS SHADERPASS_FORWARD_UNLIT

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/ShaderPass/UnlitSharePass.hlsl"
            #include "Assets/portal/Shaders/PortalScreenData.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassForwardUnlit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }

        // Выделение объекта в редакторе. Без этого прохода квад нельзя ткнуть
        // мышью в окне сцены.
        Pass
        {
            Name "SceneSelectionPass"
            Tags { "LightMode" = "SceneSelectionPass" }

            Cull Front
            ZWrite On

            HLSLPROGRAM

            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma editor_sync_compilation

            #define SHADERPASS SHADERPASS_DEPTH_ONLY
            #define SCENESELECTIONPASS

            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Material.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/Unlit.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Unlit/ShaderPass/UnlitDepthPass.hlsl"
            #include "Assets/portal/Shaders/PortalScreenData.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/ShaderPass/ShaderPassDepthOnly.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }
    }

    Fallback Off
}


