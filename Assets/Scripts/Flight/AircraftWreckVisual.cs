using UnityEngine;

namespace Airplane.FlightSimulation
{
    [DisallowMultipleComponent]
    public sealed class AircraftWreckVisual : MonoBehaviour
    {
        private const string ResourcePath = "Particles/AircraftWreckFire";
        private const float PrefabShapeRadius = 2f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int MetallicId = Shader.PropertyToID("_Metallic");
        private static readonly int SmoothnessId = Shader.PropertyToID("_Smoothness");
        private static readonly Color CharColor = new Color(0.045f, 0.038f, 0.032f, 1f);

        private static GameObject _firePrefab;

        private Light _fireLight;
        private float _lightIntensity;
        private GameObject _fire;
        private bool _built;

        public static void Apply(GameObject root)
        {
            if (!root)
                return;

            AircraftWreckVisual visual = root.GetComponent<AircraftWreckVisual>();
            if (!visual)
                visual = root.AddComponent<AircraftWreckVisual>();
            visual.Build();
        }

        public void Stop()
        {
            if (_fire)
            {
                ParticleSystem[] systems = _fire.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < systems.Length; i++)
                {
                    if (systems[i])
                        systems[i].Stop(true, ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (_fireLight)
                _fireLight.enabled = false;
        }

        private void Build()
        {
            if (_built)
                return;
            _built = true;

            Darken();
            Ignite();
        }

        private void Darken()
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer || renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                    continue;

                Material[] materials = renderer.sharedMaterials;
                int count = materials != null ? materials.Length : 0;
                if (count <= 0)
                    continue;

                for (int m = 0; m < count; m++)
                {
                    renderer.GetPropertyBlock(block, m);
                    Material material = materials[m];
                    Color baseColor = ReadColor(material, block, BaseColorId, Color.white);
                    Color tint = ReadColor(material, block, ColorId, Color.white);
                    block.SetColor(BaseColorId, Color.Lerp(baseColor, CharColor, 0.88f));
                    block.SetColor(ColorId, Color.Lerp(tint, CharColor, 0.88f));
                    block.SetColor(EmissionColorId, Color.black);

                    if (material && material.HasProperty(MetallicId))
                        block.SetFloat(MetallicId, 0.05f);
                    if (material && material.HasProperty(SmoothnessId))
                        block.SetFloat(SmoothnessId, 0.08f);

                    renderer.SetPropertyBlock(block, m);
                }
            }
        }

        private static Color ReadColor(Material material, MaterialPropertyBlock block, int id, Color fallback)
        {
            if (block != null)
            {
                Color fromBlock = block.GetColor(id);
                if (fromBlock.maxColorComponent > 0.001f || fromBlock.a > 0.001f)
                    return fromBlock;
            }

            if (material && material.HasProperty(id))
                return material.GetColor(id);

            return fallback;
        }

        private void Ignite()
        {
            if (!_firePrefab)
                _firePrefab = Resources.Load<GameObject>(ResourcePath);
            if (!_firePrefab)
            {
                Debug.LogError("Missing particle prefab Resources/" + ResourcePath + ". Run Tools/Airplane/Create Particle Prefabs.");
                return;
            }

            Bounds bounds = EncapsulateMeshBounds();
            Vector3 localCenter = transform.InverseTransformPoint(bounds.center);
            float worldRadius = Mathf.Clamp(bounds.extents.magnitude * 0.22f, 0.8f, 4.5f);
            float parentScale = Mathf.Max(0.001f, transform.lossyScale.x);
            float localScale = worldRadius / (PrefabShapeRadius * parentScale);

            _fire = Instantiate(_firePrefab, transform);
            _fire.name = "WreckFire";
            _fire.transform.localPosition = localCenter;
            _fire.transform.localRotation = Quaternion.identity;
            _fire.transform.localScale = Vector3.one * localScale;

            _fireLight = _fire.GetComponent<Light>();
            if (_fireLight)
            {
                _fireLight.range = Mathf.Clamp(worldRadius * 18f, 22f, 70f);
                _lightIntensity = _fireLight.intensity;
            }
        }

        private Bounds EncapsulateMeshBounds()
        {
            Bounds bounds = new Bounds(transform.position, Vector3.one * 4f);
            bool any = false;
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (!renderer || !renderer.enabled)
                    continue;
                if (renderer is ParticleSystemRenderer || renderer is TrailRenderer || renderer is LineRenderer)
                    continue;

                if (!any)
                {
                    bounds = renderer.bounds;
                    any = true;
                }
                else
                    bounds.Encapsulate(renderer.bounds);
            }

            return bounds;
        }

        private void Update()
        {
            if (!_fireLight || !_fireLight.enabled)
                return;

            float flicker = 0.72f + 0.28f * Mathf.PerlinNoise(Time.time * 11.4f, 0.37f);
            _fireLight.intensity = _lightIntensity * flicker;
        }
    }
}
