#ifndef AIRPLANE_TREE_BILLBOARD_INCLUDED
#define AIRPLANE_TREE_BILLBOARD_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

float4 _BaseMap_ST;
float4 _BaseColor;
float _Cutoff;
float _Frames;

TEXTURE2D(_BaseMap);
SAMPLER(sampler_BaseMap);

struct BillboardAttributes
{
    float3 positionOS : POSITION;
    float2 uv : TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct BillboardVaryings
{
    float4 positionCS : SV_POSITION;
    float2 uv : TEXCOORD0;
    nointerpolation float frame0 : TEXCOORD1;
    nointerpolation float frameBlend : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

BillboardVaryings BillboardVert(BillboardAttributes input)
{
    BillboardVaryings output;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    float4x4 objectToWorld = GetObjectToWorldMatrix();
    float3 center = mul(objectToWorld, float4(0, 0, 0, 1)).xyz;
    float3 scaleX = mul(objectToWorld, float4(1, 0, 0, 0)).xyz;
    float3 scaleY = mul(objectToWorld, float4(0, 1, 0, 0)).xyz;
    float3 scaleZ = mul(objectToWorld, float4(0, 0, 1, 0)).xyz;
    float width = length(scaleX);
    float height = length(scaleY);
    float3 objRight = scaleX / max(width, 1e-5);
    float3 objForward = scaleZ / max(length(scaleZ), 1e-5);

    float3 toCam = _WorldSpaceCameraPos - center;
    float camLen = max(length(toCam), 1e-5);
    float3 toCamN = toCam / camLen;
    float3 flat = float3(toCam.x, 0, toCam.z);
    float flatLen = length(flat);
    float3 flatDir = flatLen > 1e-4 ? flat / flatLen : objForward;

    float turns = atan2(dot(flatDir, objRight), dot(flatDir, objForward)) * (1.0 / TWO_PI);
    float frame = frac(turns) * max(_Frames, 1);
    output.frame0 = floor(frame);
    output.frameBlend = frame - output.frame0;

    float3 worldUp = abs(toCamN.y) > 0.98 ? float3(0, 0, 1) : float3(0, 1, 0);
    float3 cardRight = normalize(cross(worldUp, toCamN));
    float3 cardUp = normalize(cross(toCamN, cardRight));

    float3 world = center + cardRight * input.positionOS.x * width + cardUp * input.positionOS.y * height;
    output.positionCS = TransformWorldToHClip(world);
    output.uv = input.uv;
    return output;
}

half4 BillboardSample(BillboardVaryings input)
{
    float frames = max(_Frames, 1.0);
    float frame = floor(input.frame0 + input.frameBlend + 0.5);
    frame = fmod(frame, frames);
    float2 uv = input.uv;
    float2 atlasUV = float2((uv.x + frame) / frames, uv.y);
    float2 gradX = ddx(uv) / frames;
    float2 gradY = ddy(uv) / frames;
    return SAMPLE_TEXTURE2D_GRAD(_BaseMap, sampler_BaseMap, atlasUV, gradX, gradY);
}

#endif
