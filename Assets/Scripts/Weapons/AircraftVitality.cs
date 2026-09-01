using System;
using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.UI;
using UnityEngine;

namespace Airplane.Weapons
{
    /// <summary>
    /// Hit-point pool on an aircraft. <see cref="AircraftGun"/> delivers damage through
    /// <c>OnGunHit</c> 
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Weapons/Aircraft Vitality")]
    public sealed class AircraftVitality : MonoBehaviour
    {
        [Tooltip("Hit points at spawn. Each gun's Damage is subtracted on a local, simulated hit.")]
        [SerializeField] private float hitPoints = 100f;

        [Tooltip("Impact true airspeed reported to the crash path, km/h. The existing spawner only cares that it clears the threshold.")]
        [SerializeField] private float reportedCrashSpeedKmh = 80f;

        public Action<GunHit> OnDeathEvent;
        private float _hp;
        private PlaneRigidbody _body;
        private NetworkedAircraft _networked;

        public float HitPoints => _hp;
        public float MaxHitPoints => hitPoints;

        public void Restore()
        {
            _hp = hitPoints;
        }

        private void Awake()
        {
            _body = GetComponent<PlaneRigidbody>();
            _networked = GetComponent<NetworkedAircraft>();
            _hp = hitPoints;
        }

        private void OnEnable()
        {
            _hp = hitPoints;
        }

        /// <summary>
        /// Gets called by <see cref="AircraftGun"/>'s SendMessage on a locally simulated victim.
        /// Remote proxies never run this: the owning peer applies the hit after the weapon RPC.
        /// </summary>
        private void OnGunHit(GunHit hit)
        {
            if (hit.Damage <= 0f)
                return;
            if (_body && !_body.SimulationEnabled)
                return;
            if (CheatFlags.GodMode && CheatFlags.AppliesTo(_body))
                return;

            _hp -= hit.Damage;
            if (_hp > 0f)
                return;

            _hp = 0f;
            Vector3 point = hit.Point;
            if (_networked && _networked.IsSpawned)
            {
                OnDeathEvent?.Invoke(hit);
                _networked.ReportCrash(point, reportedCrashSpeedKmh);
                return;
            }
        }
    }
}
