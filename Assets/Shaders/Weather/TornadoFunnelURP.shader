Shader "Airplane/Weather/Tornado Funnel URP"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (0.28, 0.25, 0.22, 1)
        _TopColor("Top Color", Color) = (0.45, 0.46, 0.5, 1)

        [Header(Shape)]
        _BaseRadius("Base Radius", Float) = 45
        _Height("Height", Float) = 900
        _NeckScale("Neck Scale", Range(0.02, 1)) = 0.22
        _Taper("Taper", Range(0.1, 4)) = 0.65
        _GroundFlare("Ground Flare", Range(0, 3)) = 0.55
        _WobbleAmplitude("Wobble Amplitude", Range(0, 4)) = 0.55
        _WobbleFrequency("Wobble Frequency", Float) = 3.1
        _Seed("Seed", Float) = 0

        [Header(Shells)]
        _ShellIndex("Shell Index", Float) = 0
        _ShellCount("Shell Count", Float) = 1
        _ShellSpacing("Shell Spacing", Float) = 0.35
        _ShellFalloff("Shell Falloff", Range(0, 1)) = 0.25

        [Header(Look)]
        _SpinScroll("Spin Scroll", Float) = 0.55
        _RiseSpeed("Rise Speed", Float) = 0.35
        _NoiseScale("Noise Scale", Float) = 2.6
        _NoiseHeightScale("Noise Height Scale", Float) = 7
        _Density("Density", Range(0, 4)) = 1.5
        _EdgeBoost("Edge Boost", Range(0.1, 6)) = 1.6
        _CoreOpacity("Core Opacity", Range(0, 1)) = 0.35
        _TopFadeStart("Top Fade Start", Range(0, 1)) = 0.78
        _Opacity("Opacity", Range(0, 1)) = 1

        [Header(Fades)]
        _DepthFade("Depth Fade", Float) = 6
        _NearFade("Near Fade", Float) = 4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "TornadoFunnelForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "TornadoFunnel.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _TopColor;
                float _BaseRadius;
                float _Height;
                float _NeckScale;
                float _Taper;
                float _GroundFlare;
                float _WobbleAmplitude;
                float _WobbleFrequency;
                float _Seed;
                float _ShellIndex;
                float _ShellCount;
                float _ShellSpacing;
                float _ShellFalloff;
                float _SpinScroll;
                float _RiseSpeed;
                float _NoiseScale;
                float _NoiseHeightScale;
                float _Density;
                float _EdgeBoost;
                float _CoreOpacity;
                float _TopFadeStart;
                float _Opacity;
                float _DepthFade;
                float _NearFade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float2 shell : TEXCOORD3; // x = normalised shell, y = fog factor
            };

            float ShellT()
            {
                return _ShellCount > 1.0 ? _ShellIndex / (_ShellCount - 1.0) : 0.0;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                // The mesh is a unit tube: xz on the unit circle, y in [0,1].
                float h01 = saturate(input.positionOS.y);
                float shellT = ShellT();

                float radius = TornadoFunnelRadius(h01, _BaseRadius, _NeckScale, _Taper, _GroundFlare);
                radius *= 1.0 + shellT * _ShellSpacing;

                float2 wobble = TornadoWobble(h01, _BaseRadius, _WobbleAmplitude, _WobbleFrequency, _Seed, _Time.y);

                float3 positionOS = float3(
                    input.positionOS.x * radius + wobble.x,
                    h01 * _Height,
                    input.positionOS.z * radius + wobble.y);

                output.positionWS = TransformObjectToWorld(positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                // tilt the normal outward and up a touch so the rim term is not razor thin
                output.normalWS = TransformObjectToWorldNormal(normalize(input.normalOS + float3(0, 0.25, 0)));
                output.shell = float2(shellT, ComputeFogFactor(output.positionCS.z));
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float h01 = saturate(input.uv.y);
                float shellT = input.shell.x;

                // Sample noise on the ring itself so it wraps seamlessly in the angular direction.
                float angle = input.uv.x * 6.2831853 + _Time.y * _SpinScroll * (1.0 - 0.45 * h01) + _Seed;
                float3 noisePos = float3(cos(angle), sin(angle), 0.0) * _NoiseScale;
                noisePos.z = h01 * _NoiseHeightScale - _Time.y * _RiseSpeed;
                // push each shell into a different slice of the field so they do not stack up
                noisePos += shellT * 13.7 + _Seed;

                float body = TornadoFbm(noisePos);
                float wisps = TornadoFbm(noisePos * 2.7 + 11.3);
                float density = saturate(body * 1.7 - 0.22) * lerp(0.55, 1.0, wisps);

                float3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
                float rim = 1.0 - saturate(abs(dot(normalize(input.normalWS), viewDir)));
                // silhouette is where you look through the most vapour
                float facing = lerp(_CoreOpacity, 1.0, pow(rim, _EdgeBoost));

                float bottomFade = smoothstep(0.0, 0.03, h01);
                float topFade = 1.0 - smoothstep(_TopFadeStart, 1.0, h01);
                float verticalMass = lerp(1.0, 0.55, h01);
                float shellFade = lerp(1.0, _ShellFalloff, shellT);

                float alpha = density * facing * bottomFade * topFade * verticalMass * shellFade;
                alpha *= _Density * _Opacity * _BaseColor.a;

                // dirt at the intake, cloud colour up top
                float3 albedo = lerp(_BaseColor.rgb, _TopColor.rgb, saturate(h01 * 1.25));

                Light mainLight = GetMainLight();
                float ndl = saturate(dot(normalize(input.normalWS), mainLight.direction) * 0.5 + 0.5);
                albedo *= lerp(0.5, 1.15, ndl) * lerp(1.0, mainLight.color, 0.65);

                float4 col = float4(albedo, saturate(alpha));

                // soften where the column cuts into terrain
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float surfaceEye = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
                col.a *= saturate((sceneEye - surfaceEye) / max(_DepthFade, 1e-4));
                col.a *= saturate((surfaceEye - _NearFade) / max(_NearFade, 1e-4));

                clip(col.a - 0.004);
                col.rgb = MixFog(col.rgb, input.shell.y);
                return col;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
