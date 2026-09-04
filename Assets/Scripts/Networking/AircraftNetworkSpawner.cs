using System.Collections;
using System.Collections.Generic;
using Airplane.AI;
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
        [Tooltip("How long the explosion is allowed to play before the wreck is despawned, seconds.")]
        [SerializeField] private float despawnDelay = 1.5f;

        [Tooltip("Delay between a validated crash and the replacement aircraft, seconds.")]
        [SerializeField] private float respawnDelay = 3f;

        [Tooltip("Spawn the next aircraft at the next free spawn point instead of reusing the crash site's slot.")]
        [SerializeField] private bool rotateSpawnPoints = true;

        [Header("Bots")]
        [Tooltip("Bot-flown aircraft the server keeps in the air. Owned and simulated by the server, " +
                 "so they cost the same bandwidth as an extra player each.")]
        [SerializeField] [Range(0, 16)] private int botCount = 4;

        [Tooltip("Competence spread of the squadron, 0 = rookie, 1 = ace. Each bot rolls in this band.")]
        [SerializeField] [Range(0f, 1f)] private float botMinSkill = 0.3f;

        [SerializeField] [Range(0f, 1f)] private float botMaxSkill = 0.85f;

        [Tooltip("Centre of the airspace bots patrol while they have nothing to chase.")]
        [SerializeField] private Vector3 botPatrolCentre = new Vector3(0f, 700f, 0f);

        [SerializeField] private float botPatrolRadius = 2500f;
        [SerializeField] private float botPatrolMinAltitude = 400f;
        [SerializeField] private float botPatrolMaxAltitude = 1300f;

        [Tooltip("Bots enter on a ring of this radius around the patrol centre so they never spawn " +
                 "into each other the way a shared spawn point would.")]
        [SerializeField] private float botSpawnRingRadius = 1100f;

        [SerializeField] private float botSpawnAltitude = 800f;
        [SerializeField] private float botSpawnAltitudeJitter = 250f;

        [Tooltip("Airspeed a bot enters with, m/s. Bots start at cruise rather than at the player's launch speed.")]
        [SerializeField] private float botSpawnAirspeed = 105f;

        [Tooltip("Delay between a bot going down and its replacement, seconds.")]
        [SerializeField] private float botRespawnDelay = 8f;

        private readonly Dictionary<ulong, NetworkObject> _aircraftByClient = new Dictionary<ulong, NetworkObject>();
        private readonly Dictionary<ulong, int> _slotByClient = new Dictionary<ulong, int>();
        private readonly List<NetworkObject> _bots = new List<NetworkObject>();
        private readonly List<NetworkObject> _dummies = new List<NetworkObject>();
        private int _nextSlot;
        private int _nextBotIndex;
        private int _nextDummyIndex;
        private int _desiredBots;
        private bool _subscribed;
        private NetworkManager _manager;

        /// <summary>The spawner in the active scene, if any.</summary>
        public static AircraftNetworkSpawner Instance { get; private set; }

        public IReadOnlyDictionary<ulong, NetworkObject> AircraftByClient => _aircraftByClient;

        /// <summary>Bots currently in the air.</summary>
        public int LiveBotCount => _bots.Count;

        /// <summary>Bots the server is trying to keep in the air, including any waiting to respawn.</summary>
        public int DesiredBotCount => _desiredBots;

        private static NetworkManager Manager => NetworkManager.Singleton;

        private bool IsServerActive => Manager != null && Manager.IsListening && Manager.IsServer;
        
        private void Start()
        {
            Instance = this;
            _manager = Manager;

            if (!aircraftPrefab)
                Debug.LogError("no aircraft prefab assigned, nobody will spawn.");

            if (clearPlayerPrefab && _manager != null && _manager.NetworkConfig != null && _manager.NetworkConfig.PlayerPrefab != null)
                _manager.NetworkConfig.PlayerPrefab = null;

            if (_manager == null)
                return;

            _manager.OnServerStarted += HandleServerStarted;
            _manager.OnServerStopped += HandleServerStopped;
            _manager.OnClientConnectedCallback += HandleClientConnected;
            _manager.OnClientDisconnectCallback += HandleClientDisconnected;
            _manager.OnPreShutdown += HandlePreShutdown;
            _subscribed = true;

            if (IsServerActive)
                HandleServerStarted();
        }

        private void OnDestroy()
        {
            // Singleton is already cleared by the time scene objects are destroyed on Play-stop,
            // so unsubscribe from the instance we actually bound to.
            if (_subscribed && _manager)
            {
                _manager.OnServerStarted -= HandleServerStarted;
                _manager.OnServerStopped -= HandleServerStopped;
                _manager.OnClientConnectedCallback -= HandleClientConnected;
                _manager.OnClientDisconnectCallback -= HandleClientDisconnected;
                _manager.OnPreShutdown -= HandlePreShutdown;
            }

            _subscribed = false;
            _manager = null;

            if (Instance == this)
                Instance = null;
        }

        private void HandlePreShutdown()
        {
            if (!this)
                return;
            StopAllCoroutines();
        }

        private void HandleServerStarted()
        {
            if (!IsServerActive)
                return;
            
            foreach (ulong clientId in Manager.ConnectedClientsIds)
                SpawnFor(clientId);

            SetBotCount(botCount);
        }

        private void HandleServerStopped(bool wasHost)
        {
            _aircraftByClient.Clear();
            _slotByClient.Clear();
            _bots.Clear();
            _dummies.Clear();
            _nextSlot = 0;
            _nextBotIndex = 0;
            _nextDummyIndex = 0;
            _desiredBots = 0;
            BotCallsigns.Reset();
            AdminSession.Reset();
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (!IsServerActive)
                return;
            SpawnFor(clientId);
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            _slotByClient.Remove(clientId);

            if (!_aircraftByClient.Remove(clientId, out NetworkObject aircraft))
                return;

            // NGO is already tearing spawned objects down. A second Despawn here races
            // NetworkSpawnManager.DespawnAndDestroyNetworkObjects on Play-stop.
            if (Manager != null && Manager.ShutdownInProgress)
                return;

            if (aircraft && aircraft.IsSpawned)
                aircraft.Despawn();
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

        /// <summary>
        /// Server-only. Grows or trims the bot squadron. Safe to call while a session is running,
        /// which is how the session UI adds and removes opposition mid-flight.
        /// </summary>
        public void SetBotCount(int count)
        {
            if (!IsServerActive)
                return;

            _desiredBots = Mathf.Clamp(count, 0, 9999);

            while (_bots.Count > _desiredBots)
                DespawnLastBot();

            while (_bots.Count < _desiredBots)
            {
                if (!SpawnBot())
                    break;
            }
        }

        /// <summary>
        /// Spawns one bot: a stock aircraft, owned by the server so the server simulates it, with a
        /// pilot component bolted on at runtime. The pilot deliberately does not live on the prefab,
        /// so a client's replica of the same aircraft stays a brainless replay proxy.
        /// </summary>
        public bool SpawnBot()
        {
            if (!IsServerActive || !aircraftPrefab)
                return false;

            int index = _nextBotIndex++;
            ResolveBotSpawnPose(index, out Vector3 position, out Quaternion rotation);
            Vector3 velocity = rotation * new Vector3(Mathf.Max(40f, botSpawnAirspeed), 0f, 0f);

            NetworkObject aircraft = Instantiate(aircraftPrefab, position, rotation);
            string callsign = BotCallsigns.Next();
            aircraft.name = $"Bot Aircraft ({callsign})";

            NetworkedAircraft networked = aircraft.GetComponent<NetworkedAircraft>();
            if (networked)
                networked.ConfigureAsBot(callsign);

            AircraftBotPilot pilot = aircraft.gameObject.GetComponent<AircraftBotPilot>();
            if (!pilot)
                pilot = aircraft.gameObject.AddComponent<AircraftBotPilot>();

            float low = Mathf.Min(botMinSkill, botMaxSkill);
            float high = Mathf.Max(botMinSkill, botMaxSkill);
            pilot.Initialize(
                BotSkillProfile.FromSkill(Random.Range(low, high)),
                botPatrolCentre,
                botPatrolRadius,
                botPatrolMinAltitude,
                botPatrolMaxAltitude);

            PlaneRigidbody body = aircraft.GetComponent<PlaneRigidbody>();
            Vector3 comWorld = position;
            if (body)
            {
                comWorld = position + rotation * body.CenterOfMassBody;
                body.Teleport(comWorld, rotation, velocity, Vector3.zero);
            }

            aircraft.Spawn();
            _bots.Add(aircraft);

            if (networked)
                networked.TeleportRpc(comWorld, rotation, velocity, Vector3.zero);

            return true;
        }

        /// <summary>
        /// Spawns a still, AI-less aircraft in front of the local player so gun hits can be tested
        /// against a real networked victim. Server-only. Does not count toward the bot squadron.
        /// </summary>
        public bool SpawnDummy()
        {
            ulong clientId = Manager != null ? Manager.LocalClientId : 0;
            return SpawnDummyFor(clientId);
        }

        /// <summary>
        /// Same as <see cref="SpawnDummy"/>, but places the dummy in front of
        /// <paramref name="clientId"/>'s aircraft so a client admin sees it in their windscreen.
        /// </summary>
        public bool SpawnDummyFor(ulong clientId)
        {
            if (!IsServerActive || !aircraftPrefab)
                return false;

            ResolveDummySpawnPoseFor(clientId, out Vector3 position, out Quaternion rotation);

            NetworkObject aircraft = Instantiate(aircraftPrefab, position, rotation);
            int index = ++_nextDummyIndex;
            string callsign = index == 1 ? "Dummy" : $"Dummy {index}";
            aircraft.name = $"Dummy Aircraft ({callsign})";

            NetworkedAircraft networked = aircraft.GetComponent<NetworkedAircraft>();
            if (networked)
                networked.ConfigureAsBot(callsign);

            AircraftBotPilot existingPilot = aircraft.GetComponent<AircraftBotPilot>();
            if (existingPilot)
                Destroy(existingPilot);

            PlaneRigidbody body = aircraft.GetComponent<PlaneRigidbody>();
            Vector3 comWorld = position;
            if (body)
            {
                comWorld = position + rotation * body.CenterOfMassBody;
                body.Teleport(comWorld, rotation, Vector3.zero, Vector3.zero);
            }

            AircraftDummyHold hold = aircraft.gameObject.GetComponent<AircraftDummyHold>();
            if (!hold)
                hold = aircraft.gameObject.AddComponent<AircraftDummyHold>();
            hold.Capture(comWorld, rotation);

            aircraft.Spawn();
            _dummies.Add(aircraft);

            if (networked)
            {
                networked.TeleportRpc(comWorld, rotation, Vector3.zero, Vector3.zero);
                networked.ServerSetScale(15f);
            }

            return true;
        }

        private void ResolveDummySpawnPoseFor(ulong clientId, out Vector3 position, out Quaternion rotation)
        {
            if (_aircraftByClient.TryGetValue(clientId, out NetworkObject aircraft) && aircraft)
            {
                NetworkedAircraft networked = aircraft.GetComponent<NetworkedAircraft>();
                if (networked && networked.Body != null)
                {
                    rotation = networked.Body.Orientation;
                    position = networked.Body.Position + rotation * new Vector3(140f, 0f, 25f);
                    return;
                }
            }

            ResolveDummySpawnPose(out position, out rotation);
        }

        private void ResolveDummySpawnPose(out Vector3 position, out Quaternion rotation)
        {
            NetworkedAircraft local = NetworkedAircraft.Local;
            if (local && local.Body != null)
            {
                rotation = local.Body.Orientation;
                // Body +X is the nose. Sit it ahead and a little to the right so a host does not
                // spawn it inside their own propeller.
                position = local.Body.Position + rotation * new Vector3(140f, 0f, 25f);
                return;
            }

            ResolveSpawnPose(_nextSlot, out position, out rotation);
            position += rotation * new Vector3(80f, 0f, 40f);
        }

        private void DespawnLastBot()
        {
            int last = _bots.Count - 1;
            if (last < 0)
                return;

            NetworkObject bot = _bots[last];
            _bots.RemoveAt(last);
            if (bot && bot.IsSpawned)
                bot.Despawn();
        }

        /// <summary>
        /// Bots enter on a golden-angle ring, tangentially, so consecutive spawns are spread around
        /// the circle instead of stacking on the handful of player spawn points.
        /// </summary>
        private void ResolveBotSpawnPose(int index, out Vector3 position, out Quaternion rotation)
        {
            float angle = index * 137.508f + Random.Range(-10f, 10f);
            float radius = Mathf.Max(100f, botSpawnRingRadius) * Random.Range(0.75f, 1.15f);
            float radians = angle * Mathf.Deg2Rad;

            position = new Vector3(
                botPatrolCentre.x + Mathf.Sin(radians) * radius,
                botSpawnAltitude + Random.Range(-botSpawnAltitudeJitter, botSpawnAltitudeJitter),
                botPatrolCentre.z + Mathf.Cos(radians) * radius);

            // Body +X is the nose, so this heading puts the bot on a tangent to the ring.
            rotation = Quaternion.Euler(0f, angle, 0f);
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

            NetworkObject netObject = aircraft.NetworkObject;
            Vector3 origin = aircraft.Body ? aircraft.Body.Position : aircraft.transform.position;
            aircraft.PlayCrashExplosion(origin);

            if (_dummies.Remove(netObject))
            {
                StartCoroutine(DespawnDummy(netObject));
                return;
            }

            // A bot is owned by the server, so its OwnerClientId collides with the host's own
            // aircraft. It has to be tracked and replaced on its own list.
            if (aircraft.IsBot)
            {
                _bots.Remove(netObject);
                StartCoroutine(DespawnThenRespawnBot(netObject));
                return;
            }

            ulong clientId = aircraft.OwnerClientId;
            if (_aircraftByClient.TryGetValue(clientId, out NetworkObject tracked) && tracked == netObject)
                _aircraftByClient.Remove(clientId);

            StartCoroutine(DespawnThenRespawn(clientId, netObject));
        }

        private IEnumerator DespawnThenRespawnBot(NetworkObject wreck)
        {
            float hideFor = Mathf.Max(0f, despawnDelay);
            if (hideFor > 0f)
                yield return new WaitForSeconds(hideFor);

            if (wreck && wreck.IsSpawned)
                wreck.Despawn();

            float remaining = Mathf.Max(0f, botRespawnDelay - hideFor);
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);

            if (!IsServerActive)
                yield break;

            // Someone may have trimmed the squadron while this one was burning.
            if (_bots.Count < _desiredBots)
                SpawnBot();
        }

        private IEnumerator DespawnDummy(NetworkObject wreck)
        {
            float hideFor = Mathf.Max(0f, despawnDelay);
            if (hideFor > 0f)
                yield return new WaitForSeconds(hideFor);

            if (wreck && wreck.IsSpawned)
                wreck.Despawn();
        }

        private IEnumerator DespawnThenRespawn(ulong clientId, NetworkObject wreck)
        {
            float hideFor = Mathf.Max(0f, despawnDelay);
            if (hideFor > 0f)
                yield return new WaitForSeconds(hideFor);

            if (wreck && wreck.IsSpawned)
                wreck.Despawn();

            float remaining = Mathf.Max(0f, respawnDelay - hideFor);
            if (remaining > 0f)
                yield return new WaitForSeconds(remaining);

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
