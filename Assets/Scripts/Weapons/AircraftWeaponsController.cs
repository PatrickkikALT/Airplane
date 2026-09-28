using Airplane.FlightSimulation;
using Airplane.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Airplane.Weapons
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlaneRigidbody))]
    [AddComponentMenu("Airplane/Weapons/Aircraft Weapons Controller")]
    public sealed class AircraftWeaponsController : MonoBehaviour
    {
        private PlaneRigidbody _body;
        private AircraftGun[] _guns = System.Array.Empty<AircraftGun>();

        private float _fire01;
        private float _fireSecondary01;
        private bool _inputEnabled = true;

        public float Fire01 => _fire01;
        public float FireSecondary01 => _fireSecondary01;

        public bool InputEnabled => _inputEnabled;

        public AircraftGun[] Guns => _guns;

        public void SetInputEnabled(bool enable)
        {
            _inputEnabled = enable;
        }

        public void ApplyExternalFire(float firePrimary, float fireSecondary)
        {
            _fire01 = FlightSimMath.Saturate(firePrimary);
            _fireSecondary01 = FlightSimMath.Saturate(fireSecondary);
        }

        public float ReadTrigger(GunTriggerChannel channel)
        {
            if (CheatFlags.BlockPlayerInput && _inputEnabled)
                return 0f;
            return channel == GunTriggerChannel.Secondary ? _fireSecondary01 : _fire01;
        }

        public void OnFire(InputAction.CallbackContext context)
        {
            if (_inputEnabled && !CheatFlags.BlockPlayerInput)
                _fire01 = FlightSimMath.Saturate(ReadAxis(context));
        }

        public void OnFireSecondary(InputAction.CallbackContext context)
        {
            if (_inputEnabled && !CheatFlags.BlockPlayerInput)
                _fireSecondary01 = FlightSimMath.Saturate(ReadAxis(context));
        }

        private void Awake()
        {
            _body = GetComponent<PlaneRigidbody>();
            CacheGuns();
        }

        private void OnEnable()
        {
            CacheGuns();
        }

        private void CacheGuns()
        {
            _guns = GetComponentsInChildren<AircraftGun>(true);
        }

        public void PrePhysicsTick(float dt)
        {
            TickGuns(dt, visualOnly: false);
        }

        public void TickVisual(float dt)
        {
            TickGuns(dt, visualOnly: true);
        }

        private void LateUpdate()
        {
            if (_inputEnabled)
                return;
            if (!_body || _body.SimulationEnabled)
                return;

            TickVisual(Time.deltaTime);
        }

        private void TickGuns(float dt, bool visualOnly)
        {
            if (_guns == null)
                CacheGuns();

            for (int i = 0; i < _guns.Length; i++)
            {
                AircraftGun gun = _guns[i];
                if (!gun || !gun.isActiveAndEnabled)
                    continue;
                gun.Tick(this, _body, dt, visualOnly);
            }
        }

        private static float ReadAxis(InputAction.CallbackContext context)
        {
            if (context.canceled)
                return 0f;
            return context.ReadValue<float>();
        }
    }
}
