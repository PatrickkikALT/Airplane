using Unity.Netcode;
using UnityEngine;

namespace Airplane.Weather
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(WeatherManager))]
    [AddComponentMenu("Airplane/Weather/Weather Cycle")]
    public sealed class WeatherCycle : MonoBehaviour
    {
        [SerializeField] private bool cycleEnabled = true;
        [SerializeField] [Min(0f)] private float minHoldSeconds = 75f;
        [SerializeField] [Min(0f)] private float maxHoldSeconds = 180f;
        [SerializeField] [Min(0.1f)] private float blendSeconds = 40f;
        [SerializeField] [Range(0f, 1f)] private float worsenChance = 0.38f;

        private WeatherManager _manager;
        private int _lastPreset = int.MinValue;
        private float _holdRemaining;
        private int[] _order;
        private float[] _severity;

        private void Awake()
        {
            _manager = GetComponent<WeatherManager>();
        }

        private void OnEnable()
        {
            _lastPreset = int.MinValue;
            _holdRemaining = 0f;
        }

        private void Update()
        {
            if (!cycleEnabled || !_manager || !CanDriveCycle())
                return;

            if (_manager.PresetCount <= 1)
                return;

            if (_manager.IsBlending)
                return;

            int current = _manager.CurrentPresetIndex;
            if (current != _lastPreset)
            {
                _lastPreset = current;
                _holdRemaining = RandomHold();
                return;
            }

            _holdRemaining -= Time.deltaTime;
            if (_holdRemaining > 0f)
                return;

            if (!TryPickNext(current, out string nextName))
            {
                _holdRemaining = RandomHold();
                return;
            }

            _manager.TrySetWeather(nextName, blendSeconds);
            _lastPreset = _manager.CurrentPresetIndex;
            _holdRemaining = RandomHold();
        }

        private static bool CanDriveCycle()
        {
            NetworkManager network = NetworkManager.Singleton;
            if (!network || !network.IsListening)
                return true;
            return network.IsServer;
        }

        private float RandomHold()
        {
            float min = Mathf.Min(minHoldSeconds, maxHoldSeconds);
            float max = Mathf.Max(minHoldSeconds, maxHoldSeconds);
            return Random.Range(min, max);
        }

        private bool TryPickNext(int currentIndex, out string nextName)
        {
            nextName = null;
            RebuildOrder();
            if (_order == null || _order.Length <= 1)
                return false;

            int position = IndexOf(_order, currentIndex);
            if (position < 0)
                position = 0;

            int nextPosition;
            if (position <= 0)
                nextPosition = 1;
            else if (position >= _order.Length - 1)
                nextPosition = _order.Length - 2;
            else
                nextPosition = Random.value < worsenChance ? position + 1 : position - 1;

            int nextIndex = _order[nextPosition];
            WeatherPreset preset = _manager.GetPreset(nextIndex);
            if (preset == null || string.IsNullOrEmpty(preset.Name))
                return false;

            nextName = preset.Name;
            return true;
        }

        private void RebuildOrder()
        {
            int count = _manager.PresetCount;
            if (_order == null || _order.Length != count)
            {
                _order = new int[count];
                _severity = new float[count];
            }

            for (int i = 0; i < count; i++)
            {
                _order[i] = i;
                _severity[i] = Severity(_manager.GetPreset(i));
            }

            for (int i = 1; i < count; i++)
            {
                int index = _order[i];
                float value = _severity[index];
                int j = i - 1;
                while (j >= 0 && _severity[_order[j]] > value)
                {
                    _order[j + 1] = _order[j];
                    j--;
                }

                _order[j + 1] = index;
            }
        }

        private static int IndexOf(int[] values, int target)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == target)
                    return i;
            }

            return -1;
        }

        private static float Severity(WeatherPreset preset)
        {
            if (preset == null)
                return 0f;

            float severity = preset.RainCount;
            severity += preset.TornadoCount * 250000f;
            if (preset.LightningEnabled)
                severity += Mathf.Max(0f, preset.StrikesPerMinute) * 4000f;
            severity += preset.DensityMultiplier * 40000f;
            severity += (1.2f - preset.SunLightDimmer) * 30000f;
            return severity;
        }
    }
}
