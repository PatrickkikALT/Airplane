using Airplane.FlightSimulation;
using UnityEngine;

namespace Airplane.Multiplayer
{
    /// <summary>
    /// Keeps the chase camera pointed at whichever aircraft this peer currently owns. Because a crash
    /// despawns the aircraft and the server spawns a replacement a few seconds later, the camera has
    /// to rebind rather than hold a reference from the scene.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Networking/Local Aircraft Camera Binder")]
    public sealed class LocalAircraftCameraBinder : MonoBehaviour
    {
        [Tooltip("Chase camera to retarget. Defaults to one on this GameObject, then to any in the scene.")]
        [SerializeField] private AircraftChaseCamera chaseCamera;

        [Tooltip("While no owned aircraft exists, keep looking at the last one's final position.")]
        [SerializeField] private bool holdLastPositionOnDespawn = true;

        private Transform _placeholder;

        private void Awake()
        {
            if (!chaseCamera)
                chaseCamera = GetComponent<AircraftChaseCamera>();
            if (!chaseCamera)
                chaseCamera = FindAnyObjectByType<AircraftChaseCamera>();
        }

        private void OnEnable()
        {
            NetworkedAircraft.LocalAircraftSpawned += HandleSpawned;
            NetworkedAircraft.LocalAircraftDespawned += HandleDespawned;

            if (NetworkedAircraft.Local)
                HandleSpawned(NetworkedAircraft.Local);
        }

        private void OnDisable()
        {
            NetworkedAircraft.LocalAircraftSpawned -= HandleSpawned;
            NetworkedAircraft.LocalAircraftDespawned -= HandleDespawned;
        }

        private void OnDestroy()
        {
            if (_placeholder)
                Destroy(_placeholder.gameObject);
        }

        private void HandleSpawned(NetworkedAircraft aircraft)
        {
            if (!chaseCamera || !aircraft)
                return;

            chaseCamera.SetTarget(aircraft.transform);

            if (_placeholder)
                _placeholder.gameObject.SetActive(false);
        }

        private void HandleDespawned(NetworkedAircraft aircraft)
        {
            if (!chaseCamera)
                return;

            if (!holdLastPositionOnDespawn || !aircraft)
            {
                chaseCamera.SetTarget(null);
                return;
            }

            if (!_placeholder)
            {
                GameObject holder = new GameObject("Chase Camera Hold");
                holder.hideFlags = HideFlags.NotEditable;
                _placeholder = holder.transform;
            }

            _placeholder.gameObject.SetActive(true);
            _placeholder.SetPositionAndRotation(aircraft.transform.position, aircraft.transform.rotation);
            chaseCamera.SetTarget(_placeholder);
        }
    }
}
