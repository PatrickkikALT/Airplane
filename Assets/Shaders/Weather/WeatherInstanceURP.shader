Shader "Airplane/Weather/Weather Instance URP"
{
    Properties
    {
        [MainColor] _BaseColor("Color", Color) = (0.75, 0.82, 0.9, 0.45)
        [MainTexture] _BaseMap("Albedo", 2D) = "white" {}
        _Size("Size", Float) = 0.08
        _VelocityStretch("Velocity Stretch", Float) = 8
        _DepthFade("Depth Fade", Float) = 0.4
        _DepthBias("Depth Bias", Float) = 0.05
        _NearFade("Near Fade", Float) = 1.5
        _FarFade("Far Fade", Float) = 40
        [HideInInspector] _CloudCeiling("Cloud Ceiling", Float) = 10000000
        [HideInInspector] _CloudFade("Cloud Fade", Float) = 120
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
            Name "WeatherForward"
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
                float4 _BaseMap_ST;
                float _Size;
                float _VelocityStretch;
                float _DepthFade;
                float _DepthBias;
                float _NearFade;
                float _FarFade;
                float _CloudCeiling;
                float _CloudFade;
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
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Particle FetchParticle()
            {
                Particle p;
                p.position = 0;
                p.velocity = float3(0, -1, 0);
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
                float stretch = max(_VelocityStretch, 0.0);

                float3 camRight = UNITY_MATRIX_I_V._m00_m10_m20;
                float3 camUp = UNITY_MATRIX_I_V._m01_m11_m21;
                float3 vel = p.velocity;
                float velLen = length(vel);
                float3 velDir = velLen > 0.01 ? vel / velLen : float3(0, -1, 0);

                float3 view = _WorldSpaceCameraPos - p.position;
                float3 side = cross(velDir, view);
                float sideLen = length(side);
                side = sideLen > 1e-5 ? side / sideLen : camRight;

                // stretch the quad along velocity so rain looks like streaks
                float rainMix = saturate(stretch);
                float3 axisX = normalize(lerp(camRight, side, rainMix));
                float3 axisY = normalize(lerp(camUp, velDir, rainMix));
                float height = size * (1.0 + stretch);

                float3 worldPos = p.position + axisX * (corner.x * size) + axisY * (corner.y * height);

                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);

                float flicker = lerp(0.65, 1.0, frac(p.seed * 0.173));
                float cloudFade = saturate((_CloudCeiling - p.position.y) / max(_CloudFade, 1e-3));
                output.color = _BaseColor * flicker;
                output.color.a *= cloudFade;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.uv;
                float2 centered = uv * 2.0 - 1.0;
                float rainMask = saturate(1.0 - abs(centered.x));
                rainMask *= saturate(1.0 - abs(centered.y));
                rainMask = pow(rainMask, 1.35);
                float snowMask = saturate(1.0 - dot(centered, centered));
                snowMask = pow(max(snowMask, 0.0), 1.45);
                float shape = lerp(snowMask, rainMask, saturate(_VelocityStretch));

                float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                float4 col = input.color * tex;
                col.a *= shape;

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                // grab depth from urp (turn on depth texture on the urp asset)
                float sceneRaw = SampleSceneDepth(screenUV);
                float sceneEye = LinearEyeDepth(sceneRaw, _ZBufferParams);
                float particleEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                float occlude = saturate((sceneEye - particleEye - _DepthBias) / max(_DepthFade, 1e-4));
                col.a *= occlude;

                float nearFade = saturate((particleEye - _NearFade) / max(_NearFade, 1e-4));
                float farFade = 1.0 - saturate((particleEye - _FarFade * 0.65) / max(_FarFade * 0.35, 1e-4));
                col.a *= nearFade * farFade;

                clip(col.a - 0.003);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
