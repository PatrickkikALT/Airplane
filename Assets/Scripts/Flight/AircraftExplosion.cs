using UnityEngine;
using UnityEngine.Rendering;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// One-shot crash blast that lives in the world, not on the aircraft, so it survives despawn.
    /// Built at runtime so a crash still looks like an explosion without a wired particle prefab.
    /// </summary>
    public sealed class AircraftExplosion : MonoBehaviour
    {
        private const float Lifetime = 2.4f;

        private static Texture2D _softDisc;
        private static Material _additiveMaterial;
        private static Material _alphaMaterial;

        private Light _flashLight;
        private float _lightIntensity;
        private float _age;

        public static void Play(Vector3 worldPosition)
        {
            GameObject root = new GameObject("AircraftExplosion");
            root.transform.SetPositionAndRotation(worldPosition, Quaternion.identity);
            AircraftExplosion explosion = root.AddComponent<AircraftExplosion>();
            explosion.Build();
        }

        private void Build()
        {
            EnsureAssets();

            AddBurst(
                "Flash",
                _additiveMaterial,
                count: 10,
                lifetime: new Vector2(0.08f, 0.16f),
                speed: new Vector2(2f, 8f),
                size: new Vector2(10f, 18f),
                startColor: new Color(1f, 0.95f, 0.7f, 1f),
                endColor: new Color(1f, 0.45f, 0.05f, 0f),
                gravity: 0f,
                radius: 0.4f,
                sizeGrowth: 2.4f);

            AddBurst(
                "Fire",
                _additiveMaterial,
                count: 56,
                lifetime: new Vector2(0.45f, 0.95f),
                speed: new Vector2(8f, 28f),
                size: new Vector2(2.5f, 7f),
                startColor: new Color(1f, 0.55f, 0.12f, 1f),
                endColor: new Color(0.25f, 0.04f, 0.01f, 0f),
                gravity: -1.2f,
                radius: 1.8f,
                sizeGrowth: 1.7f);

            AddBurst(
                "Smoke",
                _alphaMaterial,
                count: 36,
                lifetime: new Vector2(1.4f, 2.2f),
                speed: new Vector2(3f, 12f),
                size: new Vector2(4f, 10f),
                startColor: new Color(0.18f, 0.16f, 0.14f, 0.85f),
                endColor: new Color(0.08f, 0.08f, 0.08f, 0f),
                gravity: -0.6f,
                radius: 2.2f,
                sizeGrowth: 2.6f);

            _flashLight = gameObject.AddComponent<Light>();
            _flashLight.type = LightType.Point;
            _flashLight.color = new Color(1f, 0.62f, 0.22f);
            _flashLight.range = 80f;
            _lightIntensity = 22f;
            _flashLight.intensity = _lightIntensity;
            _flashLight.shadows = LightShadows.None;

            Destroy(gameObject, Lifetime);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_flashLight)
                _flashLight.intensity = _lightIntensity * Mathf.Clamp01(1f - _age / 0.35f);
        }

        private void AddBurst(
            string name,
            Material material,
            short count,
            Vector2 lifetime,
            Vector2 speed,
            Vector2 size,
            Color startColor,
            Color endColor,
            float gravity,
            float radius,
            float sizeGrowth)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(transform, false);

            ParticleSystem ps = child.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            ParticleSystem.MainModule main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.05f;
            main.startDelay = 0f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetime.x, lifetime.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startColor = startColor;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count + 8;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = radius;
            shape.radiusThickness = 0.35f;

            ParticleSystem.ColorOverLifetimeModule color = ps.colorOverLifetime;
            color.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(startColor, 0f),
                    new GradientColorKey(Color.Lerp(startColor, endColor, 0.55f), 0.45f),
                    new GradientColorKey(endColor, 1f)
                },
                new[]
                {
                    new GradientAlphaKey(startColor.a, 0f),
                    new GradientAlphaKey(startColor.a * 0.8f, 0.25f),
                    new GradientAlphaKey(0f, 1f)
                });
            color.color = gradient;

            ParticleSystem.SizeOverLifetimeModule sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.35f, 1f, sizeGrowth));

            ParticleSystemRenderer renderer = child.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play(true);
        }

        private static void EnsureAssets()
        {
            if (!_softDisc)
                _softDisc = CreateSoftDisc();

            if (!_additiveMaterial)
                _additiveMaterial = CreateParticleMaterial(additive: true);

            if (!_alphaMaterial)
                _alphaMaterial = CreateParticleMaterial(additive: false);
        }

        private static Material CreateParticleMaterial(bool additive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (!shader)
                shader = Shader.Find("Particles/Unlit");
            if (!shader)
                shader = Shader.Find("Sprites/Default");
            if (!shader)
                shader = Shader.Find("Universal Render Pipeline/Unlit");

            Material material = new Material(shader)
            {
                name = additive ? "AircraftExplosionAdditive" : "AircraftExplosionAlpha",
                hideFlags = HideFlags.HideAndDontSave,
                mainTexture = _softDisc
            };

            if (material.HasProperty("_BaseMap"))
                material.SetTexture("_BaseMap", _softDisc);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", Color.white);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", Color.white);
            if (material.HasProperty("_Surface"))
                material.SetFloat("_Surface", 1f);
            if (material.HasProperty("_Blend"))
                material.SetFloat("_Blend", additive ? 2f : 0f);
            if (material.HasProperty("_Cull"))
                material.SetFloat("_Cull", 0f);
            if (material.HasProperty("_ZWrite"))
                material.SetFloat("_ZWrite", 0f);
            if (material.HasProperty("_SrcBlend"))
                material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            if (material.HasProperty("_DstBlend"))
                material.SetInt("_DstBlend", additive ? (int)BlendMode.One : (int)BlendMode.OneMinusSrcAlpha);

            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            if (additive)
                material.EnableKeyword("_BLENDMODE_ADD");
            else
                material.DisableKeyword("_BLENDMODE_ADD");

            material.renderQueue = (int)RenderQueue.Transparent;
            return material;
        }

        private static Texture2D CreateSoftDisc()
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "AircraftExplosionDisc",
                hideFlags = HideFlags.HideAndDontSave,
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear
            };

            float center = (size - 1) * 0.5f;
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x - center) / center;
                    float ny = (y - center) / center;
                    float alpha = Mathf.Clamp01(1f - Mathf.Sqrt(nx * nx + ny * ny));
                    alpha *= alpha;
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, true);
            return texture;
        }
    }
}
