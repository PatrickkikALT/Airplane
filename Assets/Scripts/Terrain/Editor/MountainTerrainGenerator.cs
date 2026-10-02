using UnityEngine;

namespace Airplane.Terrain.Editor
{
    public readonly struct MountainTerrainSettings
    {
        public readonly int Seed;
        public readonly float BaseElevationMeters;
        public readonly float PeakHeightMeters;
        public readonly float ContinentScale;
        public readonly float RidgeScale;
        public readonly int Octaves;
        public readonly float Persistence;
        public readonly float Lacunarity;
        public readonly float PeakSharpness;
        public readonly float ValleyFloor;
        public readonly float WarpStrength;
        public readonly float RangeCoverage;

        public MountainTerrainSettings(
            int seed,
            float baseElevationMeters,
            float peakHeightMeters,
            float continentScale,
            float ridgeScale,
            int octaves,
            float persistence,
            float lacunarity,
            float peakSharpness,
            float valleyFloor,
            float warpStrength,
            float rangeCoverage)
        {
            Seed = seed;
            BaseElevationMeters = baseElevationMeters;
            PeakHeightMeters = peakHeightMeters;
            ContinentScale = Mathf.Max(1f, continentScale);
            RidgeScale = Mathf.Max(1f, ridgeScale);
            Octaves = Mathf.Clamp(octaves, 1, 10);
            Persistence = Mathf.Clamp01(persistence);
            Lacunarity = Mathf.Max(1.1f, lacunarity);
            PeakSharpness = Mathf.Max(0.5f, peakSharpness);
            ValleyFloor = Mathf.Clamp01(valleyFloor);
            WarpStrength = Mathf.Max(0f, warpStrength);
            RangeCoverage = Mathf.Clamp01(rangeCoverage);
        }

        public float MaxMeters => BaseElevationMeters + PeakHeightMeters;
    }

    public static class MountainTerrainGenerator
    {
        public static float[,] BuildHeights(UnityEngine.Terrain terrain, MountainTerrainSettings settings)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            Vector3 origin = terrain.GetPosition();
            Vector3 size = data.size;
            float[,] heights = new float[res, res];
            float denom = Mathf.Max(1f, settings.MaxMeters);

            Vector2 seed = SeedOffset(settings.Seed);

            for (int z = 0; z < res; z++)
            {
                float tz = z / (res - 1f);
                float worldZ = origin.z + tz * size.z;
                for (int x = 0; x < res; x++)
                {
                    float tx = x / (res - 1f);
                    float worldX = origin.x + tx * size.x;
                    float meters = SampleMeters(worldX, worldZ, seed, settings);
                    heights[z, x] = Mathf.Clamp01(meters / denom);
                }
            }

            return heights;
        }

        public static float SampleMeters(float worldX, float worldZ, Vector2 seed, MountainTerrainSettings settings)
        {
            float continentFreq = 1f / settings.ContinentScale;
            float hillFreq = 1f / settings.RidgeScale;

            float warp = settings.WarpStrength;
            float wx = worldX + (Fbm(worldX * continentFreq + seed.x, worldZ * continentFreq + seed.y, 2, 0.5f, 2f) - 0.5f) * warp;
            float wz = worldZ + (Fbm(worldX * continentFreq + seed.x + 41.2f, worldZ * continentFreq + seed.y + 17.8f, 2, 0.5f, 2f) - 0.5f) * warp;

            float massif = Fbm(wx * continentFreq + seed.x, wz * continentFreq + seed.y, 3, 0.55f, 2f);
            float hills = Fbm(wx * hillFreq + seed.x + 19.1f, wz * hillFreq + seed.y + 73.4f, settings.Octaves, settings.Persistence, settings.Lacunarity);

            float coverageLow = Mathf.Lerp(0.12f, 0.42f, 1f - settings.RangeCoverage);
            float region = SmoothStep(coverageLow, coverageLow + 0.5f, massif);

            float roundedHills = Mathf.Pow(Mathf.Clamp01(hills), settings.PeakSharpness);
            float roundedMassif = Mathf.Pow(Mathf.Clamp01(massif), Mathf.Min(1f, settings.PeakSharpness));
            float body = roundedMassif * 0.62f + roundedHills * 0.38f;
            float combined = Mathf.Lerp(roundedMassif * 0.35f, body, 0.4f + 0.6f * region);

            float detail = (Fbm(wx * hillFreq * 2.2f + 8.4f, wz * hillFreq * 2.2f + 3.1f, 2, 0.35f, 2f) - 0.5f) * 0.05f;
            combined = Mathf.Clamp01(combined + detail);
            combined = settings.ValleyFloor + combined * (1f - settings.ValleyFloor);

            return settings.BaseElevationMeters + combined * settings.PeakHeightMeters;
        }

        public static void ApplyHeights(UnityEngine.Terrain terrain, float[,] heights, float heightScaleMeters)
        {
            TerrainData data = terrain.terrainData;
            Vector3 size = data.size;
            if (heightScaleMeters > size.y + 0.01f)
                data.size = new Vector3(size.x, heightScaleMeters, size.z);

            data.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        public static void Flatten(UnityEngine.Terrain terrain, float normalizedHeight)
        {
            TerrainData data = terrain.terrainData;
            int res = data.heightmapResolution;
            float[,] heights = new float[res, res];
            float h = Mathf.Clamp01(normalizedHeight);
            for (int z = 0; z < res; z++)
            {
                for (int x = 0; x < res; x++)
                    heights[z, x] = h;
            }

            data.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        public static void ConnectNeighbors(UnityEngine.Terrain[] terrains)
        {
            const float epsilon = 2f;
            for (int i = 0; i < terrains.Length; i++)
            {
                UnityEngine.Terrain terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;

                Vector3 pos = terrain.GetPosition();
                Vector3 size = terrain.terrainData.size;
                UnityEngine.Terrain left = null;
                UnityEngine.Terrain top = null;
                UnityEngine.Terrain right = null;
                UnityEngine.Terrain bottom = null;

                for (int j = 0; j < terrains.Length; j++)
                {
                    if (i == j)
                        continue;

                    UnityEngine.Terrain other = terrains[j];
                    if (other == null || other.terrainData == null)
                        continue;

                    Vector3 otherPos = other.GetPosition();
                    Vector3 otherSize = other.terrainData.size;

                    if (Mathf.Abs(otherPos.x + otherSize.x - pos.x) < epsilon && Mathf.Abs(otherPos.z - pos.z) < epsilon)
                        left = other;
                    if (Mathf.Abs(otherPos.x - (pos.x + size.x)) < epsilon && Mathf.Abs(otherPos.z - pos.z) < epsilon)
                        right = other;
                    if (Mathf.Abs(otherPos.z - (pos.z + size.z)) < epsilon && Mathf.Abs(otherPos.x - pos.x) < epsilon)
                        top = other;
                    if (Mathf.Abs(otherPos.z + otherSize.z - pos.z) < epsilon && Mathf.Abs(otherPos.x - pos.x) < epsilon)
                        bottom = other;
                }

                terrain.SetNeighbors(left, top, right, bottom);
            }
        }

        private static Vector2 SeedOffset(int seed)
        {
            Random.State previous = Random.state;
            Random.InitState(seed);
            Vector2 offset = new Vector2(Random.Range(0f, 4096f), Random.Range(0f, 4096f));
            Random.state = previous;
            return offset;
        }

        private static float Fbm(float x, float y, int octaves, float persistence, float lacunarity)
        {
            float sum = 0f;
            float amplitude = 1f;
            float frequency = 1f;
            float max = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x * frequency, y * frequency) * amplitude;
                max += amplitude;
                amplitude *= persistence;
                frequency *= lacunarity;
            }

            return max > 0f ? sum / max : 0f;
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            if (edge0 >= edge1)
                return value >= edge1 ? 1f : 0f;

            float t = Mathf.Clamp01((value - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }
    }
}
