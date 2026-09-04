#ifndef AIRPLANE_LIGHTNING_BOLT_INCLUDED
#define AIRPLANE_LIGHTNING_BOLT_INCLUDED

// Procedural bolt path. The whole channel is derived from a seed, so the CPU only
// ever needs to hand over two endpoints and branches can attach to the displaced
// path without the CPU knowing where it went.

float LightningHash11(float n)
{
    return frac(sin(n * 91.3458) * 47453.5453123);
}

// 1D value noise along the bolt parameter.
float LightningNoise11(float x)
{
    float i = floor(x);
    float f = frac(x);
    f = f * f * (3.0 - 2.0 * f);
    return lerp(LightningHash11(i), LightningHash11(i + 1.0), f);
}

// Signed multi-octave zigzag in [-1, 1].
float LightningZigZag(float t, float seed)
{
    float sum = 0.0;
    float amp = 1.0;
    float freq = 6.0;
    float norm = 0.0;
    [unroll]
    for (int i = 0; i < 4; i++)
    {
        sum += amp * (LightningNoise11(t * freq + seed) * 2.0 - 1.0);
        norm += amp;
        amp *= 0.5;
        freq *= 2.17;
        seed += 19.73;
    }
    return sum / max(norm, 1e-4);
}

void LightningBasis(float3 dir, out float3 nx, out float3 ny)
{
    float3 up = abs(dir.y) > 0.95 ? float3(1, 0, 0) : float3(0, 1, 0);
    nx = normalize(cross(dir, up));
    ny = cross(dir, nx);
}

// Position along the channel. t = 0 at start, 1 at end.
float3 LightningPoint(float t, float3 start, float3 end, float seed, float jitter)
{
    float3 axis = end - start;
    float len = max(length(axis), 1e-4);
    float3 dir = axis / len;

    float3 nx, ny;
    LightningBasis(dir, nx, ny);

    // Pinned at both ends so the channel actually touches cloud and ground.
    float anchor = sin(saturate(t) * 3.14159265);
    float2 offset = float2(
        LightningZigZag(t, seed),
        LightningZigZag(t, seed + 53.19));
    offset *= jitter * len * anchor;

    return start + dir * (t * len) + nx * offset.x + ny * offset.y;
}

#endif
