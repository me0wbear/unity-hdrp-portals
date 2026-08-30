// Подменяет для пикселей проёма глубину и вектор движения на те, что относятся
// к видимому сквозь портал, а не к самому квадру.
//
// Зачем. Квад стоит в сантиметрах от глаза, а видно сквозь него то, что лежит в
// десятках метров. Пока в буфере глубины стоит расстояние до квада, глубина
// резкости размывает проём в кисель, а временное сглаживание считает, что вся
// картинка в проёме приклеена к плоскости прямо перед лицом. Обе величины
// главная камера берёт из своих буферов, поэтому чинится это записью в них.
Shader "Hidden/Portals/ContentDepth"
{
    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }

        Pass
        {
            Name "PortalContentDepth"

            // Отсечение выключено по той же причине, что и в PortalScreen:
            // встроенный Quad повёрнут нормалями от игрока.
            Cull Off

            // Глубина переписывается безусловно: проверять её против самой себя
            // бессмысленно, а пиксели проёма уже отобраны тем, что рисуется
            // именно геометрия квада.
            ZWrite On
            ZTest Always

            HLSLPROGRAM

            #pragma target 4.5
            #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            TEXTURE2D_X(_ContentDepth);
            TEXTURE2D_X(_ContentMotion);
            float4x4 _PortalInverseProjection;

            struct Attributes
            {
                float3 positionOS : POSITION;
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

                float3 positionRWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(positionRWS);
                return output;
            }

            void Frag(
                Varyings input,
                out float2 outMotion : SV_Target0,
                out float outDepth : SV_Depth)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // Таргеты виртуальной камеры совпадают с экраном пиксель в
                // пиксель, поэтому выборка идёт по номеру пикселя, без фильтрации.
                uint2 pixel = uint2(input.positionCS.xy);

                // Движение виртуальной геометрии на экране совпадает с движением
                // пикселя проёма один в один — по той же причине.
                outMotion = LOAD_TEXTURE2D_X(_ContentMotion, pixel).rg;

                float contentDepth = LOAD_TEXTURE2D_X(_ContentDepth, pixel).r;

                // Проекция виртуальной камеры косая, поэтому её глубина зависит
                // не только от расстояния, и линеаризовать её по ближней и
                // дальней плоскости нельзя. Разворачиваем через обратную матрицу
                // проекции — это работает для любой матрицы.
                float2 positionNDC = input.positionCS.xy * _ScreenSize.zw;
                float3 positionVS = ComputeViewSpacePosition(
                    positionNDC, contentDepth, _PortalInverseProjection);

                // Расстояние переносится один в один: виртуальная камера стоит
                // ровно там, куда игрок попал бы, пройдя сквозь портал.
                float distanceToContent = max(positionVS.z, _ProjectionParams.y);

                // Обратная сторона LinearEyeDepth: 1 / (z * d + w).
                outDepth = (1.0 / distanceToContent - _ZBufferParams.w) / _ZBufferParams.z;
            }

            ENDHLSL
        }
    }

    Fallback Off
}
