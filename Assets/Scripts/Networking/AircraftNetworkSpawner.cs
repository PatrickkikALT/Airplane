using System.Collections;
using System.Collections.Generic;
using Airplane.FlightSimulation;
using Unity.Netcode;
using UnityEngine;

namespace Airplane.Multiplayer
{
    /// <summary>
    /// Server-side owner of the aircraft population: one aircraft per connected client, spawned with
    /// ownership so that client becomes the simulating peer, plus respawn after a validated crash.
    ///
    /// Deliberately a plain MonoBehaviour rather than a NetworkBehaviour. It sends no RPCs and holds
    /// no NetworkVariables, and it is meant to live on the NetworkManager GameObject, which cannot
    /// carry a NetworkObject. A NetworkBehaviour there would never spawn and would silently do
    /// nothing at all.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Networking/Aircraft Network Spawner")]
    public sealed class AircraftNetworkSpawner : MonoBehaviour
    {
        [Header("Aircraft")]
        [Tooltip("Aircraft prefab. Must carry a NetworkObject and be registered in the network prefab list.")]
        [SerializeField] private NetworkObject aircraftPrefab;

        [Tooltip("Clear NetworkManager's PlayerPrefab on start. Leaving it set spawns a second aircraft " +
                 "per client at the prefab's own position, on top of the one this spawner places.")]
        [SerializeField] private bool clearPlayerPrefab = true;

        [Header("Spawn Points")]
        [Tooltip("Optional hand-placed spawn poses, used in order. Falls back to the generated line below.")]
        [SerializeField] private Transform[] spawnPoints;

        [Tooltip("First generated spawn position when no spawn points are assigned.")]
        [SerializeField] private Vector3 generatedOrigin = new Vector3(0f, 600f, 0f);

        [Tooltip("Offset between consecutive generated spawns, metres. Keep it wider than the wingspan.")]
        [SerializeField] private Vector3 generatedSpacing = new Vector3(0f, 0f, 60f);

        [Tooltip("Heading of generated spawns, degrees about world up.")]
        [SerializeField] private float generatedHeadingDeg;

        [Header("Initial State")]
        [Tooltip("Airspeed handed to a freshly spawned aircraft along its own forward (+X body) axis, m/s.")]
        [SerializeField] private float spawnAirspeed = 60f;

        [Header("Respawn")]
        [Tooltip("Delay between a validated crash and the replacement aircraft, seconds.")]
        [SerializeField] private float respawnDelay = 3f;

        [Tooltip("Spawn the next aircraft at the next free spawn point instead of reusing the crash site's slot.")]
        [SerializeField] private bool rotateSpawnPoints = true;

        private readonly Dictionary<ulong, NetworkObject> _aircraftByClient = new Dictionary<ulong, NetworkObject>();
        private readonly Dictionary<ulong, int> _slotByClient = new Dictionary<ulong, int>();
        private int _nextSlot;
        private bool _subscribed;

        /// <summary>The spawner in the active scene, if any.</summary>
        public static AircraftNetworkSpawner Instance { get; private set; }

        public IReadOnlyDictionary<ulong, NetworkObject> AircraftByClient => _aircraftByClient;

        private static NetworkManager Manager => NetworkManager.Singleton;

        private bool IsServerActive => Manager != null && Manager.IsListening && Manager.IsServer;
        
        private void Start()
        {
            Instance = this;

            if (!aircraftPrefab)
                Debug.LogError("no aircraft prefab assigned, nobody will spawn.");

            if (clearPlayerPrefab && Manager.NetworkConfig != null && Manager.NetworkConfig.PlayerPrefab != null)
            {
                Manager.NetworkConfig.PlayerPrefab = null;
            }

            Manager.OnServerStarted += HandleServerStarted;
            Manager.OnServerStopped += HandleServerStopped;
            Manager.OnClientConnectedCallback += HandleClientConnected;
            Manager.OnClientDisconnectCallback += HandleClientDisconnected;
            _subscribed = true;

            if (IsServerActive)
                HandleServerStarted();
        }

        private void OnDestroy()
        {
            if (_subscribed && Manager != null)
            {
                Manager.OnServerStarted -= HandleServerStarted;
                Manager.OnServerStopped -= HandleServerStopped;
                Manager.OnClientConnectedCallback -= HandleClientConnected;
                Manager.OnClientDisconnectCallback -= HandleClientDisconnected;
                _subscribed = false;
            }

            if (Instance == this)
                Instance = null;
        }

        private void HandleServerStarted()
        {
            if (!IsServerActive)
                return;
            
            foreach (ulong clientId in Manager.ConnectedClientsIds)
                SpawnFor(clientId);
        }

        private void HandleServerStopped(bool wasHost)
        {
            _aircraftByClient.Clear();
            _slotByClient.Clear();
            _nextSlot = 0;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!IsServerActive)
                return;
            SpawnFor(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (_aircraftByClient.Remove(clientId, out NetworkObject aircraft) && aircraft && aircraft.IsSpawned)
                aircraft.Despawn();

            _slotByClient.Remove(clientId);
        }

        /// <summary>
        /// Spawns an aircraft owned by <paramref name="clientId"/>. Does nothing if that client
        /// already has one unless <paramref name="replaceExisting"/> is set.
        /// </summary>
        public void SpawnFor(ulong clientId, bool replaceExisting = false)
        {
            if (!IsServerActive || !aircraftPrefab)
                return;

            if (_aircraftByClient.TryGetValue(clientId, out NetworkObject existing) && existing && existing.IsSpawned)
            {
                if (!replaceExisting)
                    return;
                existing.Despawn();
            }

            if (!_slotByClient.TryGetValue(clientId, out int slot) || rotateSpawnPoints)
            {
                slot = _nextSlot++;
                _slotByClient[clientId] = slot;
            }

            ResolveSpawnPose(slot, out Vector3 position, out Quaternion rotation);
            Vector3 velocity = rotation * new Vector3(spawnAirspeed, 0f, 0f);

            NetworkObject aircraft = Instantiate(aircraftPrefab, position, rotation);
            aircraft.name = $"Aircraft (Client {clientId})";

            PlaneRigidbody body = aircraft.GetComponent<PlaneRigidbody>();
            Vector3 comWorld = position;
            if (body)
            {
                comWorld = position + rotation * body.CenterOfMassBody;
                body.Teleport(comWorld, rotation, velocity, Vector3.zero);
            }

            aircraft.SpawnWithOwnership(clientId);
            _aircraftByClient[clientId] = aircraft;

            NetworkedAircraft networked = aircraft.GetComponent<NetworkedAircraft>();
            if (networked)
                networked.TeleportRpc(comWorld, rotation, velocity, Vector3.zero);
        }

        private void ResolveSpawnPose(int slot, out Vector3 position, out Quaternion rotation)
        {
            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                Transform point = spawnPoints[((slot % spawnPoints.Length) + spawnPoints.Length) % spawnPoints.Length];
                if (point)
                {
                    position = point.position;
                    rotation = point.rotation;
                    return;
                }
            }

            position = generatedOrigin + generatedSpacing * slot;
            rotation = Quaternion.Euler(0f, generatedHeadingDeg, 0f);
        }

        /// <summary>
        /// Server-side entry point for a crash the owner reported and the server accepted.
        /// </summary>
        internal static void NotifyAircraftDestroyed(NetworkedAircraft aircraft, Vector3 point)
        {
            if (Instance == null || aircraft == null)
                return;
            Instance.HandleAircraftDestroyed(aircraft);
        }

        private void HandleAircraftDestroyed(NetworkedAircraft aircraft)
        {
            if (!IsServerActive)
                return;

            ulong clientId = aircraft.OwnerClientId;
            NetworkObject netObject = aircraft.NetworkObject;

            if (_aircraftByClient.TryGetValue(clientId, out NetworkObject tracked) && tracked == netObject)
                _aircraftByClient.Remove(clientId);

            if (netObject && netObject.IsSpawned)
                netObject.Despawn();

            StartCoroutine(RespawnAfterDelay(clientId));
        }

        private IEnumerator RespawnAfterDelay(ulong clientId)
        {
            yield return new WaitForSeconds(Mathf.Max(0f, respawnDelay));

            if (!IsServerActive)
                yield break;

            if (!Manager.ConnectedClients.ContainsKey(clientId))
                yield break;

            SpawnFor(clientId, true);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.9f);

            if (spawnPoints != null && spawnPoints.Length > 0)
            {
                foreach (Transform point in spawnPoints)
                {
                    if (!point)
                        continue;
                    Gizmos.DrawWireSphere(point.position, 3f);
                    Gizmos.DrawLine(point.position, point.position + point.right * 12f);
                }

                return;
            }

            Quaternion rotation = Quaternion.Euler(0f, generatedHeadingDeg, 0f);
            for (int i = 0; i < 4; i++)
            {
                Vector3 p = generatedOrigin + generatedSpacing * i;
                Gizmos.DrawWireSphere(p, 3f);
                Gizmos.DrawLine(p, p + rotation * Vector3.right * 12f);
            }
        }
    }
}
