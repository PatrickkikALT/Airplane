Shader "Airplane/Water"
{
    Properties
    {
        [Header(Water Colors)]
        _ShallowColor("Shallow Color", Color) = (0.04, 0.34, 0.42, 1)
        _DeepColor("Deep Color", Color) = (0.008, 0.045, 0.11, 1)
        _ScatterColor("Subsurface Scatter Color", Color) = (0.04, 0.48, 0.44, 1)
        _FoamColor("Foam Color", Color) = (0.92, 0.96, 0.98, 1)
        _SkyHorizonColor("Sky Horizon Reflection", Color) = (0.55, 0.68, 0.78, 1)
        _SkyZenithColor("Sky Zenith Reflection", Color) = (0.14, 0.32, 0.56, 1)

        [Header(Infinite Grid)]
        _NearGridRadius("Near Grid Radius", Float) = 1400.0
        _HorizonDistance("Horizon Distance", Float) = 65000.0

        [Header(Waves)]
        _WaveHeight("Wave Height", Float) = 8
        _WavelengthScale("Wavelength", Float) = 1.35
        _WaveChoppiness("Choppiness", Range(0, 1.5)) = 0.85
        _WaveSpeed("Wave Speed", Float) = 1
        _Storminess("Storminess", Range(0, 1)) = 0.2
        _WindDirection("Wind Direction XZ", Vector) = (0.85, 0.52, 0, 0)
        _RippleStrength("Ripple Detail", Range(0, 2)) = 1.15
        _RippleScale("Ripple Scale", Float) = 1.0

        [Header(Surface Optics)]
        _SurfaceRoughness("Surface Roughness", Range(0.015, 0.35)) = 0.055
        _SunGlintIntensity("Sun Specular Intensity", Float) = 1.6
        _RefractionDistortion("Refraction Distortion", Range(0, 0.25)) = 0.04
        _SubsurfaceScatter("Subsurface Scattering", Range(0, 2)) = 1.1
        _DepthAbsorption("Depth Absorption", Float) = 0.085
        _ColorTransitionDepth("Color Transition Depth", Float) = 14.0

        [Header(Foam)]
        _Whitecaps("Whitecap Intensity", Range(0, 2)) = 1.0
        _WhitecapThreshold("Whitecap Threshold", Range(0.05, 0.8)) = 0.28
        _ShoreFoamWidth("Shore Foam Width", Float) = 8.0
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
            Name "WaterForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor;
                float4 _DeepColor;
                float4 _ScatterColor;
                float4 _FoamColor;
                float4 _SkyHorizonColor;
                float4 _SkyZenithColor;
                float4 _WindDirection;
                float _NearGridRadius;
                float _HorizonDistance;
                float _WaveHeight;
                float _WavelengthScale;
                float _WaveChoppiness;
                float _WaveSpeed;
                float _Storminess;
                float _RippleStrength;
                float _RippleScale;
                float _SurfaceRoughness;
                float _SunGlintIntensity;
                float _RefractionDistortion;
                float _SubsurfaceScatter;
                float _DepthAbsorption;
                float _ColorTransitionDepth;
                float _Whitecaps;
                float _WhitecapThreshold;
                float _ShoreFoamWidth;
            CBUFFER_END

            float _WaterLevelY;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float4 screenPos : TEXCOORD1;
                float4 normalAndHeight : TEXCOORD2;
                float jacobian : TEXCOORD3;
            };

            float2 Rotate2D(float2 v, float c, float s)
            {
                return float2(v.x * c - v.y * s, v.x * s + v.y * c);
            }

            float2 Hash22(float2 p)
            {
                p = p - floor(p * (1.0 / 289.0)) * 289.0;
                float2 q = float2(
                    dot(p, float2(127.1, 311.7)),
                    dot(p, float2(269.5, 183.3)));
                return -1.0 + 2.0 * frac(sin(q) * 43758.5453123);
            }

            float3 GradientNoiseDeriv(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);

                float2 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
                float2 du = 30.0 * f * f * (f * (f - 2.0) + 1.0);

                float2 ga = Hash22(i + float2(0.0, 0.0));
                float2 gb = Hash22(i + float2(1.0, 0.0));
                float2 gc = Hash22(i + float2(0.0, 1.0));
                float2 gd = Hash22(i + float2(1.0, 1.0));

                float va = dot(ga, f - float2(0.0, 0.0));
                float vb = dot(gb, f - float2(1.0, 0.0));
                float vc = dot(gc, f - float2(0.0, 1.0));
                float vd = dot(gd, f - float2(1.0, 1.0));

                float value = va + u.x * (vb - va) + u.y * (vc - va) + u.x * u.y * (va - vb - vc + vd);
                float2 deriv = ga + u.x * (gb - ga) + u.y * (gc - ga) + u.x * u.y * (ga - gb - gc + gd)
                    + du * (u.yx * (va - vb - vc + vd) + float2(vb, vc) - va);

                return float3(value * 0.5 + 0.5, deriv);
            }

            void AccumulateGerstner(
                float2 xz,
                float time,
                float2 dir,
                float wavelength,
                float amplitude,
                float steepness,
                inout float3 offset,
                inout float3 dPdx,
                inout float3 dPdz)
            {
                float k = 6.28318530718 / max(wavelength, 0.5);
                float omega = sqrt(9.81 * k) * _WaveSpeed;
                float phase = k * dot(dir, xz) - omega * time;
                phase = (phase * 0.15915494309 - floor(phase * 0.15915494309)) * 6.28318530718;
                float s, c;
                sincos(phase, s, c);

                float qa = steepness * _WaveChoppiness * amplitude;
                offset.x += dir.x * qa * c;
                offset.y += amplitude * s;
                offset.z += dir.y * qa * c;

                float kqaS = k * qa * s;
                float kaC = k * amplitude * c;

                dPdx += float3(
                    -dir.x * dir.x * kqaS,
                    dir.x * kaC,
                    -dir.x * dir.y * kqaS);

                dPdz += float3(
                    -dir.x * dir.y * kqaS,
                    dir.y * kaC,
                    -dir.y * dir.y * kqaS);
            }

            void EvaluateOceanSpectrum(
                float2 xz,
                float time,
                float attenuation,
                float minWavelength,
                out float3 offset,
                out float3 normalWS,
                out float jacobian)
            {
                float2 wind = length(_WindDirection.xy) > 1e-4 ? normalize(_WindDirection.xy) : float2(0.85, 0.52);
                float storm = saturate(_Storminess);
                float heightScale = lerp(1.0, 2.6, storm);
                float chopScale = lerp(1.0, 1.35, storm);
                float lengthScale = max(_WavelengthScale, 0.2) * lerp(1.0, 1.65, storm);

                static const float wavelengths[8] = { 240.0, 145.0, 88.0, 54.0, 32.0, 19.0, 11.5, 7.2 };
                static const float weights[8]     = { 1.00,  0.55,  0.34, 0.22, 0.14, 0.09,  0.055, 0.032 };
                static const float steepnesses[8] = { 0.36,  0.40,  0.44, 0.46, 0.48, 0.44,  0.40,  0.35 };
                static const float anglesCos[8]   = { 0.996, 0.940, 0.970, 0.819, 0.899, 0.669, 0.766, 0.500 };
                static const float anglesSin[8]   = { 0.087, -0.342, 0.242, 0.574, -0.438, 0.743, -0.643, -0.866 };

                offset = 0.0;
                float3 dPdx = float3(1.0, 0.0, 0.0);
                float3 dPdz = float3(0.0, 0.0, 1.0);

                [unroll]
                for (int i = 0; i < 8; i++)
                {
                    float wavelength = wavelengths[i] * lengthScale;
                    float nyquistFade = saturate((wavelength - minWavelength) / max(minWavelength, 1.0));
                    float octaveFade = saturate(attenuation * (1.25 - i * 0.09)) * nyquistFade;
                    float2 dir = Rotate2D(wind, anglesCos[i], anglesSin[i]);
                    float amp = _WaveHeight * heightScale * weights[i] * octaveFade;
                    AccumulateGerstner(xz, time, dir, wavelength, amp, steepnesses[i] * chopScale, offset, dPdx, dPdz);
                }

                normalWS = normalize(cross(dPdz, dPdx));
                jacobian = dPdx.x * dPdz.z - dPdx.z * dPdz.x;
            }

            float2 SampleCapillarySlope(float2 xz, float time, float2 wind, float viewDist)
            {
                float2 p = xz * max(_RippleScale, 0.1);
                float fade0 = exp(-viewDist * 0.00018);
                float fade1 = exp(-viewDist * 0.00035);
                float fade2 = exp(-viewDist * 0.00075);
                float fade3 = exp(-viewDist * 0.0016);
                float fade4 = exp(-viewDist * 0.0035);

                float2 flow0 = wind * (time * 0.42);
                float2 flow1 = Rotate2D(wind, 0.940, -0.342) * (time * 0.68);
                float2 flow2 = Rotate2D(wind, 0.766, 0.643) * (time * 1.05);
                float2 flow3 = Rotate2D(wind, 0.500, -0.866) * (time * 1.55);
                float2 flow4 = Rotate2D(wind, -0.342, 0.940) * (time * 2.20);

                float2 slope = 0.0;
                if (fade0 > 0.02)
                    slope += GradientNoiseDeriv(p * 0.045 + flow0).yz * (0.45 * fade0);
                if (fade1 > 0.02)
                    slope += GradientNoiseDeriv(p * 0.115 - flow1 + 17.3).yz * (0.38 * fade1);
                if (fade2 > 0.02)
                    slope += GradientNoiseDeriv(p * 0.280 + flow2 + 43.1).yz * (0.30 * fade2);
                if (fade3 > 0.02)
                    slope += GradientNoiseDeriv(p * 0.680 - flow3 + 89.7).yz * (0.22 * fade3);
                if (fade4 > 0.02)
                    slope += GradientNoiseDeriv(p * 1.650 + flow4 + 137.9).yz * (0.15 * fade4);
                return slope * _RippleStrength;
            }

            float DistributionGGX(float ndoth, float roughness)
            {
                float a = roughness * roughness;
                float a2 = max(a * a, 1e-4);
                float d = (ndoth * a2 - ndoth) * ndoth + 1.0;
                return a2 / max(PI * d * d, 1e-6);
            }

            float GeometrySmithGGXCorrelated(float ndotv, float ndotl, float roughness)
            {
                float a2 = roughness * roughness;
                a2 *= a2;
                float gv = ndotl * sqrt(ndotv * (ndotv - ndotv * a2) + a2);
                float gl = ndotv * sqrt(ndotl * (ndotl - ndotl * a2) + a2);
                return 0.5 / max(gv + gl, 1e-5);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;

                float2 g = input.uv * 2.0 - 1.0;
                float2 disk = g * sqrt(1.0 - 0.5 * g.yx * g.yx);
                float r2 = dot(disk, disk);

                float waterLevelY = abs(_WaterLevelY) > 1e-4 ? _WaterLevelY : UNITY_MATRIX_M._m13;
                float camHeight = abs(_WorldSpaceCameraPos.y - waterLevelY);
                float nearRadius = max(_NearGridRadius, 250.0) + camHeight * 0.65;
                float maxFarDist = max(_ProjectionParams.z * 0.85, nearRadius * 2.5);
                float maxClipHoriz = sqrt(max(maxFarDist * maxFarDist - camHeight * camHeight, nearRadius * nearRadius * 4.0));
                float horizonRadius = min(max(_HorizonDistance, nearRadius * 2.0), maxClipHoriz);

                float radialSpan = max(horizonRadius - nearRadius, 100.0);
                float2 radialOffset = disk * (nearRadius + radialSpan * (r2 * r2));
                float3 restWS = float3(_WorldSpaceCameraPos.x + radialOffset.x, waterLevelY, _WorldSpaceCameraPos.z + radialOffset.y);

                float localStep = (nearRadius + 5.0 * radialSpan * (r2 * r2)) * (1.0 / 64.0);
                float minVertexWavelength = localStep * 2.5;
                float viewDist = length(_WorldSpaceCameraPos - restWS);
                float stormFade = lerp(0.00007, 0.000025, saturate(_Storminess));
                float vertAtten = exp(-viewDist * stormFade) * (1.0 - smoothstep(0.70, 0.95, r2));

                float3 offset;
                float3 normalWS;
                float jacobian;
                EvaluateOceanSpectrum(restWS.xz, _Time.y, vertAtten, minVertexWavelength, offset, normalWS, jacobian);

                float3 displacedWS = restWS + offset;
                output.positionWS = displacedWS;
                output.positionCS = TransformWorldToHClip(displacedWS);
                output.screenPos = ComputeScreenPos(output.positionCS);
                output.normalAndHeight = float4(normalWS, offset.y);
                output.jacobian = jacobian;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float2 sampleXZ = input.positionWS.xz;
                float3 toCam = _WorldSpaceCameraPos - input.positionWS;
                float viewDist = length(toCam);
                float3 viewWS = toCam / max(viewDist, 1e-4);

                float swellAtten = exp(-viewDist * 0.00018);
                float detailAtten = exp(-viewDist * 0.00045);

                float3 normalWS = normalize(input.normalAndHeight.xyz);
                float swellHeight = input.normalAndHeight.w;
                float jacobian = input.jacobian;

                float2 wind = length(_WindDirection.xy) > 1e-4 ? normalize(_WindDirection.xy) : float2(0.85, 0.52);
                float2 detailSlope = SampleCapillarySlope(sampleXZ, _Time.y, wind, viewDist);
                normalWS = normalize(float3(
                    normalWS.x - detailSlope.x * 0.45,
                    normalWS.y,
                    normalWS.z - detailSlope.y * 0.45));

                float ndotv = saturate(dot(normalWS, viewWS));
                float2 screenUV = input.screenPos.xy / max(input.screenPos.w, 1e-5);
                float waterEyeDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);

                float rawDepthCenter = SampleSceneDepth(screenUV);
                float eyeDepthCenter = LinearEyeDepth(rawDepthCenter, _ZBufferParams);
                #if UNITY_REVERSED_Z
                    bool centerIsSky = rawDepthCenter <= 1e-5;
                #else
                    bool centerIsSky = rawDepthCenter >= 1.0 - 1e-5;
                #endif
                float centerOpticalDepth = centerIsSky ? 1000.0 : max(eyeDepthCenter - waterEyeDepth, 0.0);

                float refractScale = saturate(centerOpticalDepth / 6.0) / max(1.0 + waterEyeDepth * 0.02, 1.0);
                float2 refractOffset = normalWS.xz * (_RefractionDistortion * refractScale);
                float2 refractedUV = saturate(screenUV + refractOffset);

                float rawDepthRefract = SampleSceneDepth(refractedUV);
                float eyeDepthRefract = LinearEyeDepth(rawDepthRefract, _ZBufferParams);

                float refractValid = step(waterEyeDepth + 0.05, eyeDepthRefract);
                float2 finalRefractUV = lerp(screenUV, refractedUV, refractValid);
                float rawDepth = lerp(rawDepthCenter, rawDepthRefract, refractValid);
                float sceneEyeDepth = lerp(eyeDepthCenter, eyeDepthRefract, refractValid);

                #if UNITY_REVERSED_Z
                    bool isSkyDepth = rawDepth <= 1e-5;
                #else
                    bool isSkyDepth = rawDepth >= 1.0 - 1e-5;
                #endif

                float opticalDepth = isSkyDepth ? 1000.0 : max(sceneEyeDepth - waterEyeDepth, 0.0);
                float viewPathDepth = opticalDepth / max(ndotv, 0.25);

                float colorDepthT = 1.0 - exp(-viewPathDepth / max(_ColorTransitionDepth, 0.1));
                float crestColorLift = saturate((swellHeight + _WaveHeight * 0.25) / max(_WaveHeight * 1.5, 0.1)) * 0.35 * swellAtten;
                float3 waterBaseColor = lerp(_DeepColor.rgb, _ShallowColor.rgb, saturate((1.0 - colorDepthT) + crestColorLift));

                float3 sceneColor = SampleSceneColor(finalRefractUV);
                float3 absorptionCoeff = max(1.0 - _ShallowColor.rgb, float3(0.08, 0.03, 0.02)) * max(_DepthAbsorption, 0.001) * 5.5;
                float3 transmittance = isSkyDepth ? 0.0 : exp(-opticalDepth * absorptionCoeff) * saturate(1.0 - opticalDepth / 18.0);

                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                float3 lightDir = mainLight.direction;
                float shadowFade = saturate((viewDist - 35.0) / 12.0);
                float shadow = lerp(lerp(0.35, 1.0, mainLight.shadowAttenuation), 1.0, shadowFade);

                float ndotl = saturate(dot(normalWS, lightDir));
                float wrapDiffuse = saturate(dot(normalWS, lightDir) * 0.55 + 0.45);
                float3 ambientSH = SampleSH(normalWS);

                float lightLum = max(dot(mainLight.color, float3(0.2126, 0.7152, 0.0722)), 0.25);
                float3 waterTintLight = lerp(float3(lightLum, lightLum, lightLum), mainLight.color, 0.32);
                float ambientLum = max(dot(ambientSH, float3(0.2126, 0.7152, 0.0722)), 0.15);
                float3 waterAmbient = lerp(float3(ambientLum, ambientLum, ambientLum), ambientSH, 0.35);

                float3 waterVolumeColor = waterBaseColor * (waterTintLight * (wrapDiffuse * shadow * 0.85) + waterAmbient * 0.75);
                float3 underwaterColor = lerp(waterVolumeColor, sceneColor * _ShallowColor.rgb, transmittance);

                float stormHeight = _WaveHeight * lerp(1.0, 2.6, saturate(_Storminess));
                float crestHeight = saturate((swellHeight + stormHeight * 0.35) / max(stormHeight * 1.4, 0.1));
                float backLight = pow(saturate(dot(viewWS, -lightDir + normalWS * 0.5)), 3.0);
                float waveSlopeScatter = saturate(1.0 - normalWS.y) * 2.2;
                float sssFactor = (backLight * 0.75 + waveSlopeScatter * 0.35 + (1.0 - ndotv) * 0.2) * crestHeight * _SubsurfaceScatter * shadow * swellAtten;
                underwaterColor += _ScatterColor.rgb * waterTintLight * sssFactor;

                float baseRough = clamp(_SurfaceRoughness + saturate(_Storminess) * 0.08, 0.02, 0.28);
                float distanceRoughness = saturate(baseRough + (1.0 - detailAtten) * 0.06);
                float3 reflectVec = reflect(-viewWS, normalWS);
                reflectVec.y = max(reflectVec.y, 0.005);

                float skyGradient = pow(saturate(reflectVec.y), 0.55);
                float3 proceduralSky = lerp(_SkyHorizonColor.rgb, _SkyZenithColor.rgb, skyGradient);
                float3 probeSky = GlossyEnvironmentReflection(reflectVec, input.positionWS, distanceRoughness, 1.0);
                float3 envReflection = lerp(proceduralSky, probeSky, 0.30);

                float f0 = 0.02;
                float fresnel = f0 + (1.0 - f0) * pow(1.0 - ndotv, 5.0);
                fresnel = saturate(fresnel * 0.88);

                float3 halfDir = SafeNormalize(lightDir + viewWS);
                float ndoth = saturate(dot(normalWS, halfDir));
                float ldoth = saturate(dot(lightDir, halfDir));
                float specFresnel = f0 + (1.0 - f0) * pow(1.0 - ldoth, 5.0);

                float D_sharp = DistributionGGX(ndoth, distanceRoughness);
                float V_sharp = GeometrySmithGGXCorrelated(max(ndotv, 1e-3), max(ndotl, 1e-3), distanceRoughness);

                float broadRough = saturate(distanceRoughness * 2.6 + 0.06);
                float D_broad = DistributionGGX(ndoth, broadRough);
                float V_broad = GeometrySmithGGXCorrelated(max(ndotv, 1e-3), max(ndotl, 1e-3), broadRough);

                float3 sunSpec = (D_sharp * V_sharp + D_broad * V_broad * 0.35) * specFresnel * ndotl * mainLight.color * (_SunGlintIntensity * shadow);

                float3 surfaceColor = lerp(underwaterColor, envReflection, fresnel) + sunSpec;

                float fold = saturate((1.0 - jacobian) - (1.0 - _WhitecapThreshold) * 0.45);
                float slopeCrest = smoothstep(_WhitecapThreshold * 0.4, _WhitecapThreshold * 1.1, (1.0 - normalWS.y) * 1.35 + fold * 0.75);

                float2 foamUV = sampleXZ * 0.14 + wind * (_Time.y * 0.32);
                float n0 = GradientNoiseDeriv(foamUV).x;
                float n1 = GradientNoiseDeriv(foamUV * 2.35 - wind * (_Time.y * 0.48) + 19.7).x;
                float n2 = GradientNoiseDeriv(foamUV * 5.10 + Rotate2D(wind, 0.6, 0.8) * (_Time.y * 0.7) + 53.1).x;
                float ridges = 1.0 - abs(n1 * 2.0 - 1.0);
                float foamTexture = saturate(n0 * 0.45 + ridges * 0.35 + n2 * 0.20);

                float whitecaps = smoothstep(0.28, 0.72, slopeCrest * (foamTexture + 0.30)) * _Whitecaps * lerp(1.0, 1.8, saturate(_Storminess)) * detailAtten;

                float shoreMask = isSkyDepth ? 0.0 : saturate(1.0 - opticalDepth / max(_ShoreFoamWidth, 0.1));
                float shoreWave = sin(opticalDepth * 2.2 - _Time.y * 2.6 + n0 * 4.0) * 0.5 + 0.5;
                float shoreFoam = smoothstep(0.15, 0.85, shoreMask * shoreMask * lerp(0.45, 1.0, shoreWave) * foamTexture);
                float contactEdge = isSkyDepth ? 0.0 : smoothstep(0.82, 1.0, shoreMask) * 0.85;

                float totalFoam = saturate(max(whitecaps, max(shoreFoam, contactEdge)));
                float3 foamLit = _FoamColor.rgb * (waterTintLight * (wrapDiffuse * shadow * 0.75) + waterAmbient * 0.45);
                surfaceColor = lerp(surfaceColor, foamLit, totalFoam);

                float fogFactor = ComputeFogFactorZ0ToFar(waterEyeDepth);
                surfaceColor = MixFog(surfaceColor, fogFactor);

                return float4(surfaceColor, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
