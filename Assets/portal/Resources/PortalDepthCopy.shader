// Переписывает аппаратную глубину камеры в одноканальную текстуру. Используется
// проходом PortalContentDepthCopyPass, чтобы снять глубину содержимого портала с
// уже посчитанного кадра виртуальной камеры вместо отдельного рендера AOV.
//
// Выборка по номеру пикселя, без масштабирования: текстура назначения и таргет
// камеры создаются одним размером, а область просмотра RTHandle начинается с
// нулевого пикселя.
Shader "Hidden/Portals/DepthCopy"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "PortalDepthCopy"

            Cull Off
            ZWrite Off
            ZTest Always
            Blend Off

            HLSLPROGRAM

            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D_X(_PortalSourceDepth);

            // Начало вьюпорта копии в текстуре назначения, в пикселях. Камера,
            // ограниченная областью проёма, рисует в этот вьюпорт, а внутренний
            // буфер глубины пайплайна начинается с нулевого пикселя: выборка
            // источника сдвигается на начало вьюпорта.
            float4 _PortalCopyOrigin;

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                return output;
            }

            float Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                return LOAD_TEXTURE2D_X(
                    _PortalSourceDepth,
                    uint2(input.positionCS.xy - _PortalCopyOrigin.xy)).r;
            }

            ENDHLSL
        }
    }

    Fallback Off
}
