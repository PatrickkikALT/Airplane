using UnityEngine;

namespace Airplane.FlightSimulation
{
    public sealed class AircraftExplosion : MonoBehaviour
    {
        private const float Lifetime = 3.2f;
        private const string ResourcePath = "Particles/AircraftExplosion";

        private static GameObject _prefab;

        private Light _flashLight;
        private float _lightIntensity;
        private float _age;

        public static void Play(Vector3 worldPosition)
        {
            if (!_prefab)
                _prefab = Resources.Load<GameObject>(ResourcePath);
            if (!_prefab)
            {
                Debug.LogError("Missing particle prefab Resources/" + ResourcePath + ". Run Tools/Airplane/Create Particle Prefabs.");
                return;
            }

            Object.Instantiate(_prefab, worldPosition, Quaternion.identity);
        }

        private void Awake()
        {
            _flashLight = GetComponent<Light>();
            if (_flashLight)
                _lightIntensity = _flashLight.intensity;
        }

        private void OnEnable()
        {
            _age = 0f;
            if (_flashLight)
                _flashLight.intensity = _lightIntensity;
            if (Application.isPlaying)
                Destroy(gameObject, Lifetime);
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_flashLight)
                _flashLight.intensity = _lightIntensity * Mathf.Clamp01(1f - _age / 0.35f);
        }
    }
}
