using UnityEditor;
using UnityEngine;

namespace Airplane.Terrain.Editor
{
    public class MountainTerrainEditorWindow : EditorWindow
    {
        private int seed = 1337;
        private float baseElevationMeters = 40f;
        private float peakHeightMeters = 900f;
        private float continentScale = 10000f;
        private float hillScale = 4500f;
        private int octaves = 4;
        private float persistence = 0.42f;
        private float lacunarity = 2f;
        private float slopeRoundness = 0.55f;
        private float valleyFloor = 0.08f;
        private float warpStrength = 180f;
        private float rangeCoverage = 0.75f;
        private bool applyToSelectedOnly;
        private bool autoFitHeight = true;
        private Vector2 scroll;

        [MenuItem("Tools/Terrain/Mountain Terrain Generator")]
        public static void Open()
        {
            MountainTerrainEditorWindow window = GetWindow<MountainTerrainEditorWindow>("Mountain Terrain");
            window.minSize = new Vector2(360f, 420f);
            window.Show();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);

            EditorGUILayout.LabelField("Mountain Terrain Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Writes broad rolling hills and rounded mountains into the Terrain heightmaps. Sampling uses world XZ so neighboring tiles stitch. Undo is supported. Slope roundness below 1 makes wider foothills.",
                MessageType.Info);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            applyToSelectedOnly = EditorGUILayout.Toggle("Selected terrains only", applyToSelectedOnly);
            UnityEngine.Terrain[] terrains = GatherTerrains();
            EditorGUILayout.LabelField("Terrains found", terrains.Length.ToString());

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Shape", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            seed = EditorGUILayout.IntField("Seed", seed);
            if (GUILayout.Button("Random", GUILayout.Width(70f)))
                seed = Random.Range(1, 999999);
            EditorGUILayout.EndHorizontal();

            baseElevationMeters = EditorGUILayout.FloatField("Base elevation (m)", baseElevationMeters);
            peakHeightMeters = EditorGUILayout.Slider("Peak height (m)", peakHeightMeters, 50f, 4000f);
            continentScale = EditorGUILayout.Slider("Massif scale (m)", continentScale, 2000f, 25000f);
            hillScale = EditorGUILayout.Slider("Hill scale (m)", hillScale, 800f, 12000f);
            rangeCoverage = EditorGUILayout.Slider("Range coverage", rangeCoverage, 0.1f, 1f);
            octaves = EditorGUILayout.IntSlider("Octaves", octaves, 1, 6);
            persistence = EditorGUILayout.Slider("Persistence", persistence, 0.2f, 0.7f);
            lacunarity = EditorGUILayout.Slider("Lacunarity", lacunarity, 1.5f, 2.6f);
            slopeRoundness = EditorGUILayout.Slider("Slope roundness", slopeRoundness, 0.3f, 1.2f);
            valleyFloor = EditorGUILayout.Slider("Valley floor", valleyFloor, 0f, 0.4f);
            warpStrength = EditorGUILayout.Slider("Domain warp (m)", warpStrength, 0f, 1200f);
            autoFitHeight = EditorGUILayout.Toggle("Raise TerrainData height if needed", autoFitHeight);

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(terrains.Length == 0))
            {
                if (GUILayout.Button("Generate Mountains", GUILayout.Height(32f)))
                    Generate(terrains);

                if (GUILayout.Button("Flatten to base elevation"))
                    Flatten(terrains);
            }

            EditorGUILayout.EndScrollView();
        }

        private MountainTerrainSettings BuildSettings()
        {
            return new MountainTerrainSettings(
                seed,
                Mathf.Max(0f, baseElevationMeters),
                Mathf.Max(1f, peakHeightMeters),
                continentScale,
                hillScale,
                octaves,
                persistence,
                lacunarity,
                slopeRoundness,
                valleyFloor,
                warpStrength,
                rangeCoverage);
        }

        private void Generate(UnityEngine.Terrain[] terrains)
        {
            MountainTerrainSettings settings = BuildSettings();
            float heightScale = settings.MaxMeters;
            int applied = 0;

            try
            {
                for (int i = 0; i < terrains.Length; i++)
                {
                    UnityEngine.Terrain terrain = terrains[i];
                    if (terrain == null || terrain.terrainData == null)
                        continue;

                    EditorUtility.DisplayProgressBar(
                        "Mountain Terrain",
                        $"Sculpting {terrain.name} ({i + 1}/{terrains.Length})",
                        (i + 0.5f) / terrains.Length);

                    Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Generate Mountain Terrain");

                    if (autoFitHeight && heightScale > terrain.terrainData.size.y)
                    {
                        Vector3 size = terrain.terrainData.size;
                        terrain.terrainData.size = new Vector3(size.x, heightScale, size.z);
                    }

                    float[,] heights = MountainTerrainGenerator.BuildHeights(terrain, settings);
                    float applyScale = autoFitHeight ? heightScale : terrain.terrainData.size.y;
                    MountainTerrainGenerator.ApplyHeights(terrain, heights, applyScale);
                    EditorUtility.SetDirty(terrain.terrainData);
                    applied++;
                }

                MountainTerrainGenerator.ConnectNeighbors(terrains);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Debug.Log($"Mountain Terrain Generator: sculpted {applied} terrain tile(s).");
        }

        private void Flatten(UnityEngine.Terrain[] terrains)
        {
            MountainTerrainSettings settings = BuildSettings();
            for (int i = 0; i < terrains.Length; i++)
            {
                UnityEngine.Terrain terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null)
                    continue;

                Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Flatten Terrain");
                float denom = Mathf.Max(1f, terrain.terrainData.size.y);
                MountainTerrainGenerator.Flatten(terrain, settings.BaseElevationMeters / denom);
                EditorUtility.SetDirty(terrain.terrainData);
            }

            MountainTerrainGenerator.ConnectNeighbors(terrains);
        }

        private UnityEngine.Terrain[] GatherTerrains()
        {
            if (applyToSelectedOnly)
            {
                Object[] selected = Selection.GetFiltered(typeof(UnityEngine.Terrain), SelectionMode.Editable | SelectionMode.ExcludePrefab);
                UnityEngine.Terrain[] picked = new UnityEngine.Terrain[selected.Length];
                for (int i = 0; i < selected.Length; i++)
                    picked[i] = (UnityEngine.Terrain)selected[i];
                return picked;
            }

            return FindObjectsByType<UnityEngine.Terrain>();
        }
    }
}
