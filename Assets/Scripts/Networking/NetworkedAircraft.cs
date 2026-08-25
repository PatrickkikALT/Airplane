using System;
using Airplane.FlightSimulation;
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

        private readonly AircraftSnapshotBuffer _buffer = new AircraftSnapshotBuffer();
        private readonly NetworkVariable<int> _crashCount = new NetworkVariable<int>(
            0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        private PlaneRigidbody _body;
        private AircraftFlightController _controller;
        private PlayerInput _playerInput;
        private float _sendAccumulator;
        private Vector3 _lastSentPosition;
        private Quaternion _lastSentOrientation = Quaternion.identity;
        private bool _hasSent;
        private bool _crashReported;

        /// <summary>Raised when the aircraft this client owns finishes spawning.</summary>
        public static event Action<NetworkedAircraft> LocalAircraftSpawned;

        /// <summary>Raised when the aircraft this client owns is despawned, for any reason.</summary>
        public static event Action<NetworkedAircraft> LocalAircraftDespawned;

        /// <summary>The aircraft owned by this peer, or null between a crash and the respawn.</summary>
        public static NetworkedAircraft Local { get; private set; }

        public PlaneRigidbody Body => _body;

        public int CrashCount => _crashCount.Value;

        public string DisplayName => string.IsNullOrWhiteSpace(displayNameFallback)
            ? $"Pilot {OwnerClientId}"
            : displayNameFallback;

        private void Awake()
        {
            _body = GetComponent<PlaneRigidbody>();
            _controller = GetComponent<AircraftFlightController>();
            _playerInput = GetComponent<PlayerInput>();
        }

        public override void OnNetworkSpawn()
        {
            ApplyAuthorityRoles();

            if (IsOwner)
            {
                Local = this;
                LocalAircraftSpawned?.Invoke(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            if (Local == this)
            {
                Local = null;
                LocalAircraftDespawned?.Invoke(this);
            }

            _buffer.Clear();
            _hasSent = false;
            _crashReported = false;
        }

        public override void OnGainedOwnership()
        {
            ApplyAuthorityRoles();
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

        /// <summary>
        /// Turns the local copy into either a simulated aircraft or a replay proxy. Everything that
        /// consumes input or integrates forces is switched off on a proxy; visuals and colliders stay.
        /// </summary>
        private void ApplyAuthorityRoles()
        {
            bool simulate = IsOwner;

            if (_body)
                _body.SetSimulationEnabled(simulate);

            if (_controller)
            {
                _controller.SetInputEnabled(simulate);
                _controller.SetHudVisible(simulate);
            }

            if (_playerInput)
                _playerInput.enabled = simulate;

            _buffer.Clear();
            _hasSent = false;
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
                _controller.WheelBrake01);
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
            if (impactSpeedKmh < crashSpeedKmh)
                return;

            _crashReported = true;
            SubmitCrashRpc(point, impactSpeedKmh);
        }

        /// <summary>Threshold the owner compares impact speed against, km/h.</summary>
        public float CrashSpeedKmh => crashSpeedKmh;

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Owner)]
        private void SubmitCrashRpc(Vector3 point, float impactSpeedKmh)
        {
            if (impactSpeedKmh < crashSpeedKmh)
                return;

            _crashCount.Value++;
            AircraftNetworkSpawner.NotifyAircraftDestroyed(this, point);
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
    }
}
