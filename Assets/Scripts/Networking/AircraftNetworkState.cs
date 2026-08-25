using Unity.Netcode;
using UnityEngine;

namespace Airplane.Multiplayer
{
    /// <summary>
    /// Stick, lever and control-surface positions quantised to one byte per channel.
    /// Remote peers feed these straight into the flight controller for visuals and audio;
    /// they never re-run the aero solver, so precision only has to survive the eye.
    /// </summary>
    public struct AircraftControlPacket : INetworkSerializable
    {
        public sbyte Aileron;
        public sbyte Elevator;
        public sbyte Rudder;
        public byte Throttle;
        public byte Flaps;
        public byte Airbrake;
        public byte WheelBrake;

        public float Aileron01 => Decode11(Aileron);
        public float Elevator01 => Decode11(Elevator);
        public float Rudder01 => Decode11(Rudder);
        public float Throttle01 => Decode01(Throttle);
        public float Flaps01 => Decode01(Flaps);
        public float Airbrake01 => Decode01(Airbrake);
        public float WheelBrake01 => Decode01(WheelBrake);

        public static AircraftControlPacket Create(
            float aileron,
            float elevator,
            float rudder,
            float throttle,
            float flaps,
            float airbrake,
            float wheelBrake)
        {
            return new AircraftControlPacket
            {
                Aileron = Encode11(aileron),
                Elevator = Encode11(elevator),
                Rudder = Encode11(rudder),
                Throttle = Encode01(throttle),
                Flaps = Encode01(flaps),
                Airbrake = Encode01(airbrake),
                WheelBrake = Encode01(wheelBrake)
            };
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Aileron);
            serializer.SerializeValue(ref Elevator);
            serializer.SerializeValue(ref Rudder);
            serializer.SerializeValue(ref Throttle);
            serializer.SerializeValue(ref Flaps);
            serializer.SerializeValue(ref Airbrake);
            serializer.SerializeValue(ref WheelBrake);
        }

        private static sbyte Encode11(float value)
        {
            return (sbyte)Mathf.RoundToInt(Mathf.Clamp(value, -1f, 1f) * 127f);
        }

        private static float Decode11(sbyte value)
        {
            return value * (1f / 127f);
        }

        private static byte Encode01(float value)
        {
            return (byte)Mathf.RoundToInt(Mathf.Clamp01(value) * 255f);
        }

        private static float Decode01(byte value)
        {
            return value * (1f / 255f);
        }
    }

    /// <summary>
    /// One published rigid-body state from the aircraft's owner. <see cref="Position"/> is the
    /// centre of mass in world space (what <see cref="FlightSimulation.PlaneRigidbody"/> integrates),
    /// not <c>transform.position</c>.
    /// </summary>
    public struct AircraftStateSnapshot : INetworkSerializable
    {
        /// <summary>
        /// Server time the state is treated as belonging to, seconds. The owner fills in its own
        /// estimate, then the server overwrites it on arrival so all peers share one clock.
        /// </summary>
        public double ServerTime;

        public Vector3 Position;
        public Quaternion Orientation;
        public Vector3 Velocity;
        public Vector3 AngularVelocityBody;
        public AircraftControlPacket Controls;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ServerTime);
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref Orientation);
            serializer.SerializeValue(ref Velocity);
            serializer.SerializeValue(ref AngularVelocityBody);
            serializer.SerializeValue(ref Controls);
        }
    }

    /// <summary>
    /// Time-ordered ring of snapshots for one remote aircraft, sampled a fixed delay behind
    /// server time so jitter and reordering on the unreliable channel stay invisible.
    ///
    /// Position uses a cubic Hermite through the published velocities, which keeps a banking
    /// turn curved instead of cutting the corner the way a straight lerp does.
    /// </summary>
    public sealed class AircraftSnapshotBuffer
    {
        private readonly AircraftStateSnapshot[] _items;
        private int _count;

        public AircraftSnapshotBuffer(int capacity = 32)
        {
            _items = new AircraftStateSnapshot[Mathf.Max(4, capacity)];
        }

        public int Count => _count;

        public bool TryGetNewest(out AircraftStateSnapshot snapshot)
        {
            if (_count == 0)
            {
                snapshot = default;
                return false;
            }

            snapshot = _items[_count - 1];
            return true;
        }

        public void Clear()
        {
            _count = 0;
        }

        /// <summary>
        /// Inserts a snapshot keeping the buffer sorted by <see cref="AircraftStateSnapshot.ServerTime"/>.
        /// Duplicates and states older than the whole window are dropped.
        /// </summary>
        public void Insert(in AircraftStateSnapshot snapshot)
        {
            if (_count == _items.Length)
            {
                if (snapshot.ServerTime <= _items[0].ServerTime)
                    return;

                System.Array.Copy(_items, 1, _items, 0, _count - 1);
                _count--;
            }

            int insertAt = _count;
            while (insertAt > 0 && _items[insertAt - 1].ServerTime > snapshot.ServerTime)
                insertAt--;

            if (insertAt > 0 && _items[insertAt - 1].ServerTime >= snapshot.ServerTime - 1e-6)
            {
                _items[insertAt - 1] = snapshot;
                return;
            }

            for (int i = _count; i > insertAt; i--)
                _items[i] = _items[i - 1];

            _items[insertAt] = snapshot;
            _count++;
        }

        /// <summary>
        /// Samples the buffer at <paramref name="renderTime"/>. Past the newest snapshot the state is
        /// dead-reckoned from its velocity for at most <paramref name="maxExtrapolation"/> seconds so a
        /// dropped burst of packets coasts instead of freezing.
        /// </summary>
        public bool Sample(double renderTime, float maxExtrapolation, out AircraftStateSnapshot result)
        {
            if (_count == 0)
            {
                result = default;
                return false;
            }

            if (_count == 1 || renderTime <= _items[0].ServerTime)
            {
                result = _items[0];
                return true;
            }

            AircraftStateSnapshot newest = _items[_count - 1];
            if (renderTime >= newest.ServerTime)
            {
                float ahead = Mathf.Min((float)(renderTime - newest.ServerTime), Mathf.Max(0f, maxExtrapolation));
                result = newest;
                result.ServerTime = renderTime;
                result.Position = newest.Position + newest.Velocity * ahead;
                result.Orientation = IntegrateOrientation(newest.Orientation, newest.AngularVelocityBody, ahead);
                return true;
            }

            for (int i = _count - 1; i > 0; i--)
            {
                AircraftStateSnapshot a = _items[i - 1];
                AircraftStateSnapshot b = _items[i];
                if (renderTime < a.ServerTime || renderTime > b.ServerTime)
                    continue;

                float span = (float)(b.ServerTime - a.ServerTime);
                float t = span > 1e-5f ? Mathf.Clamp01((float)(renderTime - a.ServerTime) / span) : 1f;

                result = new AircraftStateSnapshot
                {
                    ServerTime = renderTime,
                    Position = Hermite(a.Position, a.Velocity, b.Position, b.Velocity, t, span),
                    Orientation = Quaternion.Slerp(a.Orientation, b.Orientation, t),
                    Velocity = Vector3.Lerp(a.Velocity, b.Velocity, t),
                    AngularVelocityBody = Vector3.Lerp(a.AngularVelocityBody, b.AngularVelocityBody, t),
                    Controls = t < 0.5f ? a.Controls : b.Controls
                };
                return true;
            }

            result = newest;
            return true;
        }

        // i genuinely do not know what this does
        private static Vector3 Hermite(Vector3 p0, Vector3 v0, Vector3 p1, Vector3 v1, float t, float dt)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            float h00 = 2f * t3 - 3f * t2 + 1f;
            float h10 = t3 - 2f * t2 + t;
            float h01 = -2f * t3 + 3f * t2;
            float h11 = t3 - t2;
            return h00 * p0 + v0 * (h10 * dt) + h01 * p1 + v1 * (h11 * dt);
        }

        private static Quaternion IntegrateOrientation(Quaternion orientation, Vector3 omegaBody, float dt)
        {
            if (dt <= 0f)
                return orientation;
            return FlightSimulation.FlightSimMath.IntegrateQuaternionEuler(orientation, omegaBody, dt);
        }
    }
}
