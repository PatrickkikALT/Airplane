using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Airplane.Weapons.Editor
{
    public static class AircraftHitSparksCreator
    {
        private const string PrefabPath = "Assets/Resources/Particles/AircraftHitSparks.prefab";
        private const string MaterialPath = "Assets/Resources/Particles/AircraftExplosionAdditive.mat";

        [MenuItem("Tools/Airplane/Create Hit Sparks")]
        public static void Create()
        {
            string directory = Path.GetDirectoryName(PrefabPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            var go = new GameObject("AircraftHitSparks");
            ParticleSystem system = go.AddComponent<ParticleSystem>();
            system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = system.main;
            main.duration = 0.08f;
            main.loop = false;
            main.playOnAwake = true;
            main.stopAction = ParticleSystemStopAction.Destroy;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(8f, 26f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.04f);
            main.gravityModifier = 1.4f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 256;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var startColor = new Gradient();
            startColor.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.97f, 0.82f), 0f),
                    new GradientColorKey(new Color(1f, 0.42f, 0.06f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f)
                });
            main.startColor = new ParticleSystem.MinMaxGradient(startColor);

            ParticleSystem.EmissionModule emission = system.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 18) });

            ParticleSystem.ShapeModule shape = system.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 28f;
            shape.radius = 0.02f;
            shape.radiusThickness = 1f;

            ParticleSystem.ColorOverLifetimeModule colorOverLife = system.colorOverLifetime;
            colorOverLife.enabled = true;
            var lifeColor = new Gradient();
            lifeColor.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.96f, 0.75f), 0f),
                    new GradientColorKey(new Color(1f, 0.55f, 0.08f), 0.35f),
                    new GradientColorKey(new Color(0.35f, 0.08f, 0.02f), 1f)
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 0.25f),
                    new GradientAlphaKey(0f, 1f)
                });
            colorOverLife.color = lifeColor;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = system.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var size = new AnimationCurve();
            size.AddKey(0f, 1f);
            size.AddKey(1f, 0.2f);
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, size);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 1.6f;
            renderer.velocityScale = 0.045f;
            renderer.cameraVelocityScale = 0f;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material)
                renderer.sharedMaterial = material;
            else
                Debug.LogError("Missing material " + MaterialPath + ".");

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            EditorGUIUtility.PingObject(prefab);
            Selection.activeObject = prefab;
        }
    }
}
