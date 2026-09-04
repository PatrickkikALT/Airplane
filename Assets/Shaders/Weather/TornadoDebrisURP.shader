Shader "Airplane/Weather/Tornado Debris URP"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (0.32, 0.28, 0.23, 0.75)
        _TopColor("Top Color", Color) = (0.5, 0.5, 0.54, 0.5)
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        _Size("Size", Float) = 0.9
        _VelocityStretch("Velocity Stretch", Float) = 2.5
        _GroundY("Ground Y", Float) = 0
        _TintHeight("Tint Height", Float) = 400
        _Softness("Softness", Range(0.01, 4)) = 1.4
        _DepthFade("Depth Fade", Float) = 1.5
        _DepthBias("Depth Bias", Float) = 0.05
        _NearFade("Near Fade", Float) = 3
        _FarFade("Far Fade", Float) = 900
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend("Dst Blend", Float) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "TornadoDebrisForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend [_SrcBlend] [_DstBlend]

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma instancing_options procedural:setup

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            struct Particle
            {
                float3 position;
                float3 velocity;
                float scale;
                float seed;
            };

            StructuredBuffer<Particle> _Particles;

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TopColor;
                float4 _BaseMap_ST;
                float _Size;
                float _VelocityStretch;
                float _GroundY;
                float _TintHeight;
                float _Softness;
                float _DepthFade;
                float _DepthBias;
                float _NearFade;
                float _FarFade;
            CBUFFER_END

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Particle FetchParticle()
            {
                Particle p;
                p.position = 0;
                p.velocity = float3(0, 1, 0);
                p.scale = 1;
                p.seed = 0;
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                p = _Particles[unity_InstanceID];
                #endif
                return p;
            }

            void setup()
            {
                #ifdef UNITY_PROCEDURAL_INSTANCING_ENABLED
                Particle p = _Particles[unity_InstanceID];
                float3 pos = p.position;
                unity_ObjectToWorld = float4x4(
                    1, 0, 0, pos.x,
                    0, 1, 0, pos.y,
                    0, 0, 1, pos.z,
                    0, 0, 0, 1);
                unity_WorldToObject = float4x4(
                    1, 0, 0, -pos.x,
                    0, 1, 0, -pos.y,
                    0, 0, 1, -pos.z,
                    0, 0, 0, 1);
                #endif
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                Particle p = FetchParticle();
                float2 corner = input.positionOS.xy;
                float size = max(p.scale * _Size, 1e-4);

                float3 camRight = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 camUp = UNITY_MATRIX_I_V._m01_m11_m21;

                // Debris is thrown sideways by the vortex, so smear along velocity
                // rather than straight down like precipitation does.
                float3 vel = p.velocity;
                float velLen = length(vel);
                float3 velDir = velLen > 0.01 ? vel / velLen : camUp;
                float3 view = _WorldSpaceCameraPos - p.position;
                float3 side = cross(velDir, view);
                float sideLen = length(side);
                side = sideLen > 1e-5 ? side / sideLen : camRight;

                float smear = saturate(velLen / max(_VelocityStretch * 20.0, 1e-4));
                float3 axisX = normalize(lerp(camRight, side, smear));
                float3 axisY = normalize(lerp(camUp, velDir, smear));
                float width = size;
                float height = size * (1.0 + _VelocityStretch * smear);

                // never let a chunk shrink below a pixel or the column looks empty
                float3 viewPos = TransformWorldToView(p.position);
                float pixelWs = abs(viewPos.z) / max(abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y * 0.5, 1e-4);
                width = max(width, pixelWs * 1.2);
                height = max(height, pixelWs * 1.2);

                float3 worldPos = p.position + axisX * (corner.x * width) + axisY * (corner.y * height);

                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                // heavy dirt near the intake, pale vapour once it is lofted
                float lift = saturate((p.position.y - _GroundY) / max(_TintHeight, 1e-3));
                float4 tint = lerp(_BaseColor, _TopColor, lift);
                float flicker = lerp(0.7, 1.0, frac(p.seed * 0.173));
                output.color = tint * float4(flicker.xxx, 1.0);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 centered = input.uv * 2.0 - 1.0;
                float shape = saturate(1.0 - dot(centered, centered));
                shape = pow(shape, _Softness);

                float4 col = input.color * SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                col.a *= shape;

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float particleEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                col.a *= saturate((sceneEye - particleEye - _DepthBias) / max(_DepthFade, 1e-4));

                float nearFade = saturate((particleEye - _NearFade) / max(_NearFade, 1e-4));
                float farFade = 1.0 - saturate((particleEye - _FarFade * 0.7) / max(_FarFade * 0.3, 1e-4));
                col.a *= nearFade * farFade;

                clip(col.a - 0.003);
                col.rgb = MixFog(col.rgb, input.fogFactor);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
