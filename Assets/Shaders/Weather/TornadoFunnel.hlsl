#ifndef AIRPLANE_TORNADO_FUNNEL_INCLUDED
#define AIRPLANE_TORNADO_FUNNEL_INCLUDED

// Shared between TornadoSimulation.compute and the tornado shaders so the debris
// orbits the same wall the funnel mesh is drawn at. Keep both sides in sync.

// h01: 0 at the ground, 1 at the cloud deck.
float TornadoFunnelRadius(float h01, float baseRadius, float neckScale, float taper, float groundFlare)
{
    float h = saturate(h01);
    float wall = lerp(neckScale, 1.0, pow(h, max(taper, 1e-3)));
    // dust skirt where the funnel chews up the ground
    wall += groundFlare * exp(-h * 12.0);
    return baseRadius * max(wall, 1e-3);
}

// Lateral snake of the whole column, so it does not read as a static cone.
float2 TornadoWobble(float h01, float baseRadius, float amplitude, float frequency, float seed, float time)
{
    float h = saturate(h01);
    float2 offset = float2(
        sin(h * frequency + time * 0.9 + seed),
        cos(h * frequency * 0.83 + time * 1.13 + seed * 1.7));
    // anchored at the ground, free at the top
    return offset * amplitude * baseRadius * (0.12 + h);
}

float TornadoHash11(float n)
{
    return frac(sin(n * 78.233) * 43758.5453123);
}

float TornadoHash31(float3 p)
{
    p = frac(p * 0.3183099 + 0.1);
    p *= 17.0;
    return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float TornadoValueNoise(float3 p)
{
    float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);

    float n000 = TornadoHash31(i + float3(0, 0, 0));
    float n100 = TornadoHash31(i + float3(1, 0, 0));
    float n010 = TornadoHash31(i + float3(0, 1, 0));
    float n110 = TornadoHash31(i + float3(1, 1, 0));
    float n001 = TornadoHash31(i + float3(0, 0, 1));
    float n101 = TornadoHash31(i + float3(1, 0, 1));
    float n011 = TornadoHash31(i + float3(0, 1, 1));
    float n111 = TornadoHash31(i + float3(1, 1, 1));

    float x00 = lerp(n000, n100, f.x);
    float x10 = lerp(n010, n110, f.x);
    float x01 = lerp(n001, n101, f.x);
    float x11 = lerp(n011, n111, f.x);
    return lerp(lerp(x00, x10, f.y), lerp(x01, x11, f.y), f.z);
}

float TornadoFbm(float3 p)
{
    float sum = 0.0;
    float amp = 0.5;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        sum += amp * TornadoValueNoise(p);
        p *= 2.03;
        amp *= 0.5;
    }
    return sum;
}

#endif
