using System;
using System.Collections.Generic;
using Airplane.FlightSimulation;
using Airplane.UI;
using Airplane.Weapons;
using Airplane.Weather;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Airplane.Multiplayer
{
    /// <summary>
    /// Multiplayer wrapper around one aircraft.
    ///
    /// Authority model: the owning peer is the only one that runs <see cref="PlaneRigidbody"/> for
    /// this aircraft. It publishes its solver state on an unreliable channel; every other peer turns
    /// its copy into a replay proxy that is posed from interpolated snapshots. The server stays
    /// authoritative over spawning, ownership and crash validation, which is where cheating actually
    /// matters, without having to reproduce a substepped aero solve for every client.
    ///
    /// Consequence for contact: a remote proxy is an immovable obstacle to the locally simulated
    /// aircraft. Each peer resolves its own aircraft against the others, so a mid-air collision is
    /// felt on both machines but the impulses are computed independently.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlaneRigidbody))]
    [RequireComponent(typeof(NetworkObject))]
    [AddComponentMenu("Airplane/Networking/Networked Aircraft")]
    public sealed class NetworkedAircraft : NetworkBehaviour
    {
        [Header("Replication")]
        [Tooltip("State updates published per second by the owner. 20-30 is plenty for an aircraft.")]
        [SerializeField] [Range(5f, 60f)] private float sendRate = 25f;

        [Tooltip("How far behind server time remote copies are rendered, seconds. Must exceed typical jitter.")]
        [SerializeField] [Range(0.02f, 0.5f)] private float interpolationDelay = 0.12f;

        [Tooltip("Longest dead-reckoning coast when snapshots stop arriving, seconds.")]
        [SerializeField] [Range(0f, 1f)] private float maxExtrapolation = 0.35f;

        [Tooltip("Skip a send if nothing moved more than this (metres) and nothing rotated more than the angle below.")]
        [SerializeField] private float positionSendThreshold = 0.01f;

        [SerializeField] private float rotationSendThresholdDeg = 0.05f;

        [Header("Crash Reporting")]
        [Tooltip("Impact true airspeed above which the owner reports a crash, km/h.")]
        [SerializeField] private float crashSpeedKmh = 50f;

        [Header("Identity")]
        [Tooltip("Optional label shown by the session UI. Falls back to \"Pilot <clientId>\".")]
        [SerializeField] private string displayNameFallback = "";

        private static readonly List<NetworkedAircraft> Registry = new List<NetworkedAircraft>();

        private readonly AircraftSnapshotBuffer _buffer = new AircraftSnapshotBuffer();
        private readonly NetworkVariable<int> _crashCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Replicated so every peer can label a nametag and skip a bot when picking a camera.</summary>
        private readonly NetworkVariable<bool> _isBot = new NetworkVariable<bool>(
            false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<FixedString64Bytes> _pilotName = new NetworkVariable<FixedString64Bytes>(
            default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private readonly NetworkVariable<float> _visualScale = new NetworkVariable<float>(
            1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlaneRigidbody _body;
        private AircraftEngine _engine;
        private AircraftFlightController _controller;
        private AircraftWeaponsController _weapons;
        private PlayerInput _playerInput;
        private float _sendAccumulator;
        private Vector3 _lastSentPosition;
        private Quaternion _lastSentOrientation = Quaternion.identity;
        private bool _hasSent;
        private bool _crashReported;
        private bool _botLocally;
        private bool _isAlive;
        private string _pendingPilotName;

        /// <summary>Raised when the aircraft this client owns finishes spawning.</summary>
        public static event Action<NetworkedAircraft> LocalAircraftSpawned;

        /// <summary>Raised when the aircraft this client owns is despawned, for any reason.</summary>
        public static event Action<NetworkedAircraft> LocalAircraftDespawned;

        /// <summary>The aircraft owned by this peer, or null between a crash and the respawn.</summary>
        public static NetworkedAircraft Local { get; private set; }

        /// <summary>
        /// Every spawned aircraft on this peer, human or bot. Bot pilots search it instead of doing
        /// their own scene queries, and the nametag overlay draws from it.
        /// </summary>
        public static IReadOnlyList<NetworkedAircraft> All => Registry;

        public PlaneRigidbody Body => _body;
        public AircraftEngine Engine => _controller.Engine;

        public int CrashCount => _crashCount.Value;

        /// <summary>True for a server-flown aircraft with an <c>AircraftBotPilot</c> at the controls.</summary>
        public bool IsBot => _botLocally || (IsSpawned && _isBot.Value);

        /// <summary>False from the moment the wreck is concealed until the replacement spawns.</summary>
        public bool IsAlive => _isAlive;

        /// <summary>
        /// Name shown on this aircraft's nametag. Humans submit their own on spawn, bots are named by
        /// the server; the serialized fallback only matters before either has arrived.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (IsSpawned)
                {
                    FixedString64Bytes replicated = _pilotName.Value;
                    if (replicated.Length > 0)
                        return replicated.ToString();
                }

                if (!string.IsNullOrWhiteSpace(_pendingPilotName))
                    return _pendingPilotName;

                return string.IsNullOrWhiteSpace(displayNameFallback)
                    ? $"Pilot {OwnerClientId}"
                    : displayNameFallback;
            }
        }

        /// <summary>
        /// Server-side, called before <c>Spawn</c>. Marks the aircraft as bot-flown so the input path
        /// and the local-player hooks stay switched off even though the server owns it.
        /// </summary>
        internal void ConfigureAsBot(string callsign)
        {
            _botLocally = true;
            _pendingPilotName = callsign;
        }

        private void Awake()
        {
            _body = GetComponent<PlaneRigidbody>();
            _controller = GetComponent<AircraftFlightController>();
            _weapons = GetComponent<AircraftWeaponsController>();
            _playerInput = GetComponent<PlayerInput>();
            _isAlive = true;
        }

        public override void OnNetworkSpawn()
        {
            _isAlive = true;
            if (!Registry.Contains(this))
                Registry.Add(this);

            if (IsServer)
            {
                _isBot.Value = _botLocally;
                if (!string.IsNullOrWhiteSpace(_pendingPilotName))
                    _pilotName.Value = ToFixedName(_pendingPilotName);
            }

            _visualScale.OnValueChanged += HandleScaleChanged;
            HandleScaleChanged(1f, _visualScale.Value);

            ApplyAuthorityRoles();

            // A bot is owned by the server, so IsOwner is true for it on the host. Claiming Local
            // would hand the chase camera and the session UI to a bot instead of the player.
            if (IsOwner && !IsBot)
            {
                Local = this;
                LocalAircraftSpawned?.Invoke(this);
                SubmitPilotNameRpc(ToFixedName(LocalPlayerIdentity.PilotName));
            }

            // Late joiners missed the weather/timescale broadcast. Push the current world state
            // onto the new human's owner, not onto bots (the host would get it every respawn).
            if (IsServer && !IsBot && OwnerClientId != NetworkManager.LocalClientId)
                AdminSession.SyncToAircraft(this);
        }

        public override void OnNetworkDespawn()
        {
            _visualScale.OnValueChanged -= HandleScaleChanged;
            Registry.Remove(this);
            _isAlive = false;

            if (Local == this)
            {
                Local = null;
                if (NetworkManager != null && !NetworkManager.ShutdownInProgress)
                    LocalAircraftDespawned?.Invoke(this);
            }

            _buffer.Clear();
            _hasSent = false;
            _crashReported = false;
        }

        public override void OnDestroy()
        {
            _visualScale.OnValueChanged -= HandleScaleChanged;
            Registry.Remove(this);
            base.OnDestroy();
        }

        public override void OnGainedOwnership()
        {
            ApplyAuthorityRoles();
            if (IsBot)
                return;

            Local = this;
            LocalAircraftSpawned?.Invoke(this);
        }

        public override void OnLostOwnership()
        {
            ApplyAuthorityRoles();
            if (Local == this)
            {
                Local = null;
                LocalAircraftDespawned?.Invoke(this);
            }
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitPilotNameRpc(FixedString64Bytes pilotName)
        {
            if (pilotName.Length == 0 || _botLocally)
                return;

            _pilotName.Value = pilotName;
        }

        private static FixedString64Bytes ToFixedName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return default;

            string trimmed = value.Trim();
            if (trimmed.Length > 24)
                trimmed = trimmed.Substring(0, 24);

            return new FixedString64Bytes(trimmed);
        }

        /// <summary>
        /// Turns the local copy into either a simulated aircraft or a replay proxy. Everything that
        /// consumes input or integrates forces is switched off on a proxy; visuals and colliders stay.
        /// </summary>
        private void ApplyAuthorityRoles()
        {
            bool simulate = IsOwner;

            // A bot simulates but takes no human input: the pilot component is the only writer of
            // its deflections and triggers, exactly like a remote proxy is written by the wire.
            bool humanInput = simulate && !IsBot;

            if (_body)
                _body.SetSimulationEnabled(simulate);

            if (_controller)
            {
                _controller.SetInputEnabled(humanInput);
                _controller.SetHudVisible(humanInput);
            }

            if (_weapons)
            {
                _weapons.SetInputEnabled(humanInput);
                _weapons.SetHudVisible(humanInput);
            }

            if (_playerInput)
                _playerInput.enabled = humanInput;

            _buffer.Clear();
            _hasSent = false;
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            AircraftChaseCamera.Active?.OnLook(context);
        }

        public void OnOrbitHold(InputAction.CallbackContext context)
        {
            AircraftChaseCamera.Active?.OnOrbitHold(context);
        }

        public void OnZoom(InputAction.CallbackContext context)
        {
            AircraftChaseCamera.Active?.OnZoom(context);
        }

        public void OnResetOrbit(InputAction.CallbackContext context)
        {
            AircraftChaseCamera.Active?.OnResetOrbit(context);
        }

        private void Update()
        {
            if (!IsSpawned)
                return;

            if (IsOwner)
                PublishState();
            else
                ApplyBufferedState();
        }

        private void PublishState()
        {
            float interval = 1f / Mathf.Max(1f, sendRate);
            _sendAccumulator += Time.deltaTime;
            if (_sendAccumulator < interval)
                return;
            _sendAccumulator = 0f;

            Vector3 position = _body.Position;
            Quaternion orientation = _body.Orientation;

            if (_hasSent
                && Vector3.SqrMagnitude(position - _lastSentPosition) < positionSendThreshold * positionSendThreshold
                && Quaternion.Angle(orientation, _lastSentOrientation) < rotationSendThresholdDeg)
                return;

            _lastSentPosition = position;
            _lastSentOrientation = orientation;
            _hasSent = true;

            AircraftStateSnapshot snapshot = new AircraftStateSnapshot
            {
                ServerTime = NetworkManager.ServerTime.Time,
                Position = position,
                Orientation = orientation,
                Velocity = _body.Velocity,
                AngularVelocityBody = _body.AngularVelocityBody,
                Controls = CaptureControls()
            };

            SubmitStateRpc(snapshot);
        }

        private AircraftControlPacket CaptureControls()
        {
            if (!_controller)
                return default;

            return AircraftControlPacket.Create(
                _controller.Aileron01,
                _controller.Elevator01,
                _controller.Rudder01,
                _controller.Throttle01,
                _controller.Flaps01,
                _controller.Airbrake01,
                _controller.WheelBrake01,
                _weapons ? _weapons.Fire01 : 0f,
                _weapons ? _weapons.FireSecondary01 : 0f);
        }

        private void ApplyBufferedState()
        {
            double renderTime = NetworkManager.ServerTime.Time - interpolationDelay;
            if (!_buffer.Sample(renderTime, maxExtrapolation, out AircraftStateSnapshot state))
                return;

            _body.ApplyNetworkState(state.Position, state.Orientation, state.Velocity, state.AngularVelocityBody);

            if (_controller)
            {
                AircraftControlPacket c = state.Controls;
                _controller.ApplyExternalControls(
                    c.Aileron01,
                    c.Elevator01,
                    c.Rudder01,
                    c.Throttle01,
                    c.Flaps01,
                    c.Airbrake01,
                    c.WheelBrake01);
            }

            if (_weapons)
            {
                AircraftControlPacket c = state.Controls;
                _weapons.ApplyExternalFire(c.Fire01, c.FireSecondary01);
            }
        }

        [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitStateRpc(AircraftStateSnapshot snapshot)
        {
            // Restamp on the server so every consumer interpolates against one clock. A client's
            // NetworkManager.ServerTime runs behind the real server tick, so honouring the sender's
            // stamp would leave the host permanently extrapolating stale client states while clients
            // saw the host's aircraft interpolate cleanly.
            snapshot.ServerTime = NetworkManager.ServerTime.Time;

            if (!IsOwner)
                _buffer.Insert(snapshot);

            RelayStateRpc(snapshot);
        }

        [Rpc(SendTo.NotServer, Delivery = RpcDelivery.Unreliable)]
        private void RelayStateRpc(AircraftStateSnapshot snapshot)
        {
            if (IsOwner)
                return;
            _buffer.Insert(snapshot);
        }

        /// <summary>
        /// Called by <see cref="CollisionTest"/> on the simulating peer. The impact speed travels with
        /// the report so the server can reject a client claiming a crash it could not have had.
        /// </summary>
        public void ReportCrash(Vector3 point, float impactSpeedKmh)
        {
            if (!IsSpawned || !IsOwner || _crashReported)
                return;
            if (CheatFlags.GodMode && this == Local)
                return;
            if (impactSpeedKmh < crashSpeedKmh)
                return;

            _crashReported = true;
            SubmitCrashRpc(point, impactSpeedKmh);
        }

        /// <summary>Threshold the owner compares impact speed against, km/h.</summary>
        public float CrashSpeedKmh => crashSpeedKmh;

        /// <summary>
        /// Called by <see cref="AircraftGun"/> on the firing owner when a round hits a remote
        /// aircraft. The server forwards the impulse to the victim's owner so their solver feels it.
        /// </summary>
        public void ReportWeaponHit(NetworkedAircraft victim, Vector3 point, Vector3 impulse, float damage)
        {
            if (!IsSpawned || !IsOwner || !victim)
                return;

            SubmitWeaponHitRpc(victim.NetworkObject, point, impulse, damage);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitWeaponHitRpc(NetworkObjectReference victimRef, Vector3 point, Vector3 impulse, float damage)
        {
            if (!victimRef.TryGet(out NetworkObject victimObject))
                return;

            NetworkedAircraft victim = victimObject.GetComponent<NetworkedAircraft>();
            if (!victim)
                return;

            victim.ReceiveWeaponHitRpc(point, impulse, damage);
        }

        [Rpc(SendTo.Owner)]
        private void ReceiveWeaponHitRpc(Vector3 point, Vector3 impulse, float damage)
        {
            if (!_body || !_body.SimulationEnabled)
                return;

            GunHit hit = new GunHit
            {
                Point = point,
                Normal = -impulse.normalized,
                Impulse = impulse,
                Damage = damage,
                Victim = _body,
                Shooter = null,
                Gun = null
            };
            AircraftGun.ApplyHit(_body, in hit);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitCrashRpc(Vector3 point, float impactSpeedKmh)
        {
            if (impactSpeedKmh < crashSpeedKmh)
                return;

            _crashCount.Value++;
            AircraftNetworkSpawner.NotifyAircraftDestroyed(this, point);
        }

        /// <summary>
        /// Server-only. Plays the crash explosion on every peer and hides this airframe until despawn.
        /// Must run before <see cref="NetworkObject.Despawn"/> so the RPC still has an object to travel on.
        /// </summary>
        internal void PlayCrashExplosion(Vector3 origin)
        {
            if (!IsSpawned || !IsServer)
                return;

            PlayCrashExplosionRpc(origin);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void PlayCrashExplosionRpc(Vector3 origin)
        {
            AircraftExplosion.Play(origin);
            ConcealWreck();
        }

        private void ConcealWreck()
        {
            _isAlive = false;
            _crashReported = true;

            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i])
                    renderers[i].enabled = false;
            }

            Collider[] colliders = GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i])
                    colliders[i].enabled = false;
            }

            if (_body)
                _body.SetSimulationEnabled(false);

            if (_controller)
            {
                _controller.SetInputEnabled(false);
                _controller.SetHudVisible(false);
            }

            if (_weapons)
            {
                _weapons.SetInputEnabled(false);
                _weapons.SetHudVisible(false);
            }

            if (_playerInput)
                _playerInput.enabled = false;
        }

        /// <summary>
        /// Server-side reset used after a respawn or a teleport. Runs on every peer so proxies drop
        /// their stale interpolation window instead of sliding the aircraft across the map.
        /// </summary>
        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        public void TeleportRpc(Vector3 comWorld, Quaternion orientation, Vector3 velocityWorld, Vector3 angularVelocityBody)
        {
            _buffer.Clear();
            _hasSent = false;
            _crashReported = false;
            _body.Teleport(comWorld, orientation, velocityWorld, angularVelocityBody);
        }

        /// <summary>
        /// Server-only. Crashes this aircraft regardless of who owns it, which is what the admin
        /// destroy command needs for a remote human. God mode does not apply: an admin kill is
        /// not an impact.
        /// </summary>
        internal void ForceDestroyFromServer()
        {
            if (!IsSpawned || !IsServer || _crashReported)
                return;

            _crashReported = true;
            _crashCount.Value++;
            Vector3 point = _body ? _body.Position : transform.position;
            AircraftNetworkSpawner.NotifyAircraftDestroyed(this, point);
        }

        internal void ServerSetScale(float scale)
        {
            if (!IsSpawned || !IsServer)
                return;

            _visualScale.Value = Mathf.Clamp(scale, 0f, 50f);
        }

        internal void RequestAdmin(AdminCommand command, string target, float value)
        {
            if (!IsSpawned)
                return;

            SubmitAdminRpc((byte)command, ToFixedName(target), value);
        }

        internal void BroadcastWorldAdmin(AdminCommand command, string payload, float value)
        {
            if (!IsSpawned || !IsServer)
                return;

            ApplyWorldAdminRpc((byte)command, ToFixedName(payload), value);
        }

        internal void ApplyOwnerAdmin(AdminCommand command, float value)
        {
            if (!IsSpawned || !IsServer)
                return;

            ApplyOwnerAdminRpc((byte)command, value);
        }

        internal void SyncWorldState(string weather, float timescale)
        {
            if (!IsSpawned || !IsServer)
                return;

            SyncWorldStateRpc(ToFixedName(weather), timescale);
        }

        internal void ApplyOwnerAdminLocal(AdminCommand command, float value)
        {
            ApplyOwnerAdminState(command, value);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitAdminRpc(byte command, FixedString64Bytes target, float value, RpcParams rpcParams = default)
        {
            AdminSession.ExecuteOnServer(
                (AdminCommand)command,
                target.ToString(),
                value,
                rpcParams.Receive.SenderClientId,
                this);
        }

        [Rpc(SendTo.Everyone, InvokePermission = RpcInvokePermission.Server)]
        private void ApplyWorldAdminRpc(byte command, FixedString64Bytes payload, float value)
        {
            switch ((AdminCommand)command)
            {
                case AdminCommand.Weather:
                    WeatherManager.Instance?.TrySetWeather(payload.ToString());
                    break;
                case AdminCommand.Timescale:
                    Time.timeScale = Mathf.Clamp(value, 0f, 8f);
                    break;
            }
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void ApplyOwnerAdminRpc(byte command, float value)
        {
            ApplyOwnerAdminState((AdminCommand)command, value);
        }

        [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Server)]
        private void SyncWorldStateRpc(FixedString64Bytes weather, float timescale)
        {
            if (weather.Length > 0)
            {
                WeatherManager weatherManager = WeatherManager.Instance;
                string name = weather.ToString();
                if (weatherManager != null
                    && !string.Equals(weatherManager.CurrentWeatherName, name, StringComparison.OrdinalIgnoreCase))
                    weatherManager.TrySetWeather(name);
            }

            Time.timeScale = Mathf.Clamp(timescale, 0f, 8f);
        }

        private void ApplyOwnerAdminState(AdminCommand command, float value)
        {
            switch (command)
            {
                case AdminCommand.Heal:
                    GetComponent<AircraftVitality>()?.Restore();
                    break;
                case AdminCommand.Reload:
                    if (_weapons == null || _weapons.Guns == null)
                        break;
                    for (int i = 0; i < _weapons.Guns.Length; i++)
                    {
                        if (_weapons.Guns[i])
                            _weapons.Guns[i].RefillAmmo();
                    }

                    break;
                case AdminCommand.Speed:
                    if (_controller && _controller.Engine)
                        _controller.Engine.SetMaxThrust((int)value);
                    break;
            }
        }

        private void HandleScaleChanged(float previous, float current)
        {
            float scale = Mathf.Clamp(current, 0f, 50f);
            transform.localScale = new Vector3(scale, scale, scale);
        }
    }
}
