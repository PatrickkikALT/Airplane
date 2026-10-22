Shader "Airplane/Tree Billboard"
{
    Properties
    {
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        [MainColor] _BaseColor("Color", Color) = (1, 1, 1, 1)
        _Cutoff("Alpha Cutoff", Range(0, 1)) = 0.35
        _Frames("Frames", Float) = 8
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "Geometry"
            "DisableBatching" = "True"
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "TreeBillboard.hlsl"

            BillboardVaryings Vert(BillboardAttributes input)
            {
                return BillboardVert(input);
            }

            half Frag(BillboardVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(BillboardSample(input).a - _Cutoff);
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            #include "TreeBillboard.hlsl"

            BillboardVaryings Vert(BillboardAttributes input)
            {
                return BillboardVert(input);
            }

            void Frag(BillboardVaryings input, out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                UNITY_SETUP_INSTANCE_ID(input);
                clip(BillboardSample(input).a - _Cutoff);

                half3 normalWS = half3(0, 1, 0);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                outNormalWS = half4(PackFloat2To888(remappedOctNormalWS), 0.0);
            #else
                outNormalWS = half4(NormalizeNormalPerPixel(normalWS), 0.0);
            #endif

            #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
            #endif
            }
            ENDHLSL
        }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "TreeBillboard.hlsl"

            BillboardVaryings Vert(BillboardAttributes input)
            {
                return BillboardVert(input);
            }

            half4 Frag(BillboardVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 albedo = BillboardSample(input);
                clip(albedo.a - _Cutoff);

                Light mainLight = GetMainLight();
                half ndotl = saturate(dot(half3(0, 1, 0), mainLight.direction));
                half3 ambient = SampleSH(half3(0, 1, 0));
                half3 color = albedo.rgb * _BaseColor.rgb * (ambient + mainLight.color * ndotl * 0.15h);
                color = MixFog(color, ComputeFogFactor(input.positionCS.z));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}
