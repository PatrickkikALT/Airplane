using Airplane.FlightSimulation;
using Airplane.Weapons;
using UnityEngine;

namespace Airplane.AI
{
    /// <summary>
    /// Pins a server-owned aircraft at its spawn pose. Simulation stays on so gun hits and vitality
    /// still run; without this the airframe would fly off the moment it spawned with no pilot.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(150)]
    [AddComponentMenu("Airplane/AI/Aircraft Dummy Hold")]
    public sealed class AircraftDummyHold : MonoBehaviour
    {
        private PlaneRigidbody _body;
        private AircraftFlightController _controller;
        private AircraftWeaponsController _weapons;
        private Vector3 _position;
        private Quaternion _orientation = Quaternion.identity;
        private bool _captured;

        public void Capture(Vector3 comWorld, Quaternion orientation)
        {
            _position = comWorld;
            _orientation = orientation;
            _captured = true;
        }

        private void Awake()
        {
            _body = GetComponent<PlaneRigidbody>();
            _controller = GetComponent<AircraftFlightController>();
            _weapons = GetComponent<AircraftWeaponsController>();
        }

        private void FixedUpdate()
        {
            if (!_captured && _body)
            {
                _position = _body.Position;
                _orientation = _body.Orientation;
                _captured = true;
            }

            if (_body && _body.SimulationEnabled)
                _body.Teleport(_position, _orientation, Vector3.zero, Vector3.zero);

            if (_controller)
            {
                _controller.ApplyExternalControls(
                    0f,
                    _controller.ElevatorTrim,
                    0f,
                    0f,
                    0f,
                    0f,
                    0f);
            }

            if (_weapons)
                _weapons.ApplyExternalFire(0f, 0f);
        }
    }
}
