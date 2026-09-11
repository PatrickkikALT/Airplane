using Airplane.FlightSimulation;
using Unity.Netcode;
using UnityEngine;

namespace Airplane.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    [AddComponentMenu("Airplane/Networking/Networked Atmosphere")]
    public sealed class NetworkedAtmosphere : NetworkBehaviour
    {
        [Header("Gusts")]
        [Tooltip("If true the server slowly drifts wind around the authored value and replicates it.")]
        [SerializeField] private bool simulateGusts;

        [Tooltip("Peak deviation from the authored wind, m/s.")]
        [SerializeField] private float gustAmplitude = 4f;

        [Tooltip("Seconds for one full gust cycle. Longer feels like weather, shorter like turbulence.")]
        [SerializeField] private float gustPeriod = 25f;

        private readonly NetworkVariable<Vector3> _wind = new NetworkVariable<Vector3>(
            Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private AtmosphericModel _model;
        private Vector3 _baseWind;

        public Vector3 Wind => _wind.Value;

        public override void OnNetworkSpawn()
        {
            _model = AtmosphericModel.Instance;
            if (!_model)
                _model = GetComponent<AtmosphericModel>();

            _wind.OnValueChanged += HandleWindChanged;

            if (IsServer)
            {
                _baseWind = _model ? _model.WindWorld : Vector3.zero;
                _wind.Value = _baseWind;
            }
            else
            {
                HandleWindChanged(Vector3.zero, _wind.Value);
            }
        }

        public override void OnNetworkDespawn()
        {
            _wind.OnValueChanged -= HandleWindChanged;
        }

        private void HandleWindChanged(Vector3 previous, Vector3 current)
        {
            if (!_model)
                _model = AtmosphericModel.Instance;
            if (_model)
                _model.SetWind(current);
        }

        private void Update()
        {
            if (!IsServer || !simulateGusts || gustPeriod <= 0.01f)
                return;

            float phase = (float)NetworkManager.ServerTime.Time * (2f * Mathf.PI / gustPeriod);
            Vector3 gust = new Vector3(
                Mathf.Sin(phase),
                0f,
                Mathf.Sin(phase * 0.73f + 1.3f)) * gustAmplitude;

            Vector3 target = _baseWind + gust;
            if (Vector3.SqrMagnitude(target - _wind.Value) > 0.01f)
                _wind.Value = target;
        }
    }
}
