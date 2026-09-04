Shader "Airplane/Weather/Lightning Bolt URP"
{
    Properties
    {
        _CoreColor("Core Color", Color) = (1, 1, 1, 1)
        _GlowColor("Glow Color", Color) = (0.55, 0.7, 1, 1)

        [Header(Channel)]
        _Start("Start", Vector) = (0, 0, 0, 0)
        _End("End", Vector) = (0, -1, 0, 0)
        _Seed("Seed", Float) = 0
        _Jitter("Jitter", Range(0, 0.5)) = 0.075
        _Width("Width", Float) = 4
        _TipTaper("Tip Taper", Range(0, 1)) = 0.35

        [Header(Branch)]
        _BranchT("Branch Start T", Float) = -1
        _BranchSeed("Branch Seed", Float) = 0
        _BranchLength("Branch Length", Range(0, 1)) = 0.35
        _BranchSpread("Branch Spread", Range(0, 3)) = 0.8

        [Header(Look)]
        _Intensity("Intensity", Float) = 1
        _Progress("Progress", Range(0, 1)) = 1
        _DrawSharpness("Draw Sharpness", Float) = 14
        _CoreWidth("Core Width", Range(0.01, 1)) = 0.18
        _GlowPower("Glow Power", Range(0.5, 8)) = 2.5
        _MinPixelWidth("Min Pixel Width", Float) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "LightningForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "LightningBolt.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _CoreColor;
                float4 _GlowColor;
                float4 _Start;
                float4 _End;
                float _Seed;
                float _Jitter;
                float _Width;
                float _TipTaper;
                float _BranchT;
                float _BranchSeed;
                float _BranchLength;
                float _BranchSpread;
                float _Intensity;
                float _Progress;
                float _DrawSharpness;
                float _CoreWidth;
                float _GlowPower;
                float _MinPixelWidth;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0; // x = across ribbon [-1,1], y = along channel [0,1]
                float thickness : TEXCOORD1;
            };

            // A branch hangs off the parent's displaced path, so its root has to be
            // evaluated here rather than passed in from the CPU.
            void ResolveChannel(out float3 start, out float3 end, out float seed)
            {
                start = _Start.xyz;
                end = _End.xyz;
                seed = _Seed;

                if (_BranchT < 0.0)
                    return;

                float3 root = LightningPoint(_BranchT, _Start.xyz, _End.xyz, _Seed, _Jitter);
                float3 axis = _End.xyz - _Start.xyz;
                float len = max(length(axis), 1e-4);
                float3 dir = axis / len;

                float3 nx, ny;
                LightningBasis(dir, nx, ny);

                float angle = LightningHash11(_BranchSeed) * 6.2831853;
                float3 offshoot = normalize(dir + (nx * cos(angle) + ny * sin(angle)) * _BranchSpread);

                start = root;
                end = root + offshoot * len * _BranchLength;
                seed = _BranchSeed;
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                float t = saturate(input.uv.y);
                float across = input.uv.x * 2.0 - 1.0;

                float3 start, end;
                float seed;
                ResolveChannel(start, end, seed);

                float3 position = LightningPoint(t, start, end, seed, _Jitter);
                // finite difference for the ribbon tangent
                float step = 0.02;
                float3 ahead = LightningPoint(min(t + step, 1.0), start, end, seed, _Jitter);
                float3 behind = LightningPoint(max(t - step, 0.0), start, end, seed, _Jitter);
                float3 tangent = ahead - behind;
                float tangentLen = length(tangent);
                tangent = tangentLen > 1e-5 ? tangent / tangentLen : normalize(end - start);

                float3 view = _WorldSpaceCameraPos - position;
                float3 side = cross(tangent, view);
                float sideLen = length(side);
                side = sideLen > 1e-5 ? side / sideLen : float3(1, 0, 0);

                float width = _Width * lerp(1.0, _TipTaper, t);

                // A bolt kilometres away is sub-pixel thin; without this it strobes in and out.
                float3 viewPos = TransformWorldToView(position);
                float pixelWs = abs(viewPos.z) / max(abs(UNITY_MATRIX_P._m11) * _ScaledScreenParams.y * 0.5, 1e-4);
                float minWidth = pixelWs * _MinPixelWidth;
                float widened = max(width, minWidth);
                // keep the energy constant when we widen it, so distant bolts do not bloom out
                output.thickness = width / max(widened, 1e-4);

                float3 worldPos = position + side * (across * widened);

                output.positionCS = TransformWorldToHClip(worldPos);
                output.uv = float2(across, t);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float across = abs(input.uv.x);
                float t = input.uv.y;

                float core = saturate(1.0 - across / max(_CoreWidth, 1e-3));
                core *= core;
                float glow = pow(saturate(1.0 - across), _GlowPower);

                // the channel strikes downward over a few milliseconds
                float draw = saturate((_Progress - t) * _DrawSharpness);

                float energy = (glow * 0.65 + core) * draw * _Intensity * input.thickness;
                float3 color = lerp(_GlowColor.rgb, _CoreColor.rgb, core);

                clip(energy - 0.0015);
                return float4(color * energy, energy);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
