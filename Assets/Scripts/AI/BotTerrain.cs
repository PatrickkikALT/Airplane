using UnityEngine;

namespace Airplane.AI
{
    /// <summary>
    /// Ground height under a world XZ. Unity Terrain is sampled directly so a single physics ray
    /// that misses a ridge cannot pretend the world is flat.
    /// </summary>
    public static class BotTerrain
    {
        public static float SampleGroundY(float worldX, float worldZ, float fallbackY = 0f)
        {
            Terrain[] terrains = Terrain.activeTerrains;
            if (terrains == null || terrains.Length == 0)
                return fallbackY;

            float best = float.NegativeInfinity;
            bool hit = false;

            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                if (!terrain || !terrain.terrainData)
                    continue;

                Vector3 origin = terrain.GetPosition();
                Vector3 size = terrain.terrainData.size;
                float lx = worldX - origin.x;
                float lz = worldZ - origin.z;
                if (lx < 0f || lz < 0f || lx > size.x || lz > size.z)
                    continue;

                float y = terrain.SampleHeight(new Vector3(worldX, 0f, worldZ)) + origin.y;
                if (y > best)
                    best = y;
                hit = true;
            }

            return hit ? best : fallbackY;
        }

        public static float SampleGroundY(Vector3 world, float fallbackY = 0f)
        {
            return SampleGroundY(world.x, world.z, fallbackY);
        }
    }
}
