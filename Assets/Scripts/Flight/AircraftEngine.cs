using UnityEngine;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Slipstream field produced by the propeller. Downstream surfaces add this velocity
    /// to their local airflow when they lie inside the wash cylinder.
    /// </summary>
    public readonly struct PropWashState
    {
        public readonly bool Active;
        public readonly Vector3 Origin;
        public readonly Vector3 Axis;
        public readonly float Radius;
        public readonly float Length;
        public readonly float ExcessSpeed;

        public PropWashState(bool active, Vector3 origin, Vector3 axis, float radius, float length, float excessSpeed)
        {
            Active = active;
            Origin = origin;
            Axis = axis;
            Radius = radius;
            Length = length;
            ExcessSpeed = excessSpeed;
        }

        /// <summary>
        /// Excess axial velocity at <paramref name="worldPoint"/> (m/s). Zero outside the wash cone.
        /// </summary>
        public Vector3 VelocityAt(Vector3 worldPoint)
        {
            if (!Active || ExcessSpeed <= 0.01f)
                return Vector3.zero;

            Vector3 d = worldPoint - Origin;
            float axial = Vector3.Dot(d, Axis);
            if (axial < 0f || axial > Length)
                return Vector3.zero;

            Vector3 radial = d - Axis * axial;
            float growth = 1f + 0.12f * axial;
            float r = Radius * growth;
            float rSqr = r * r;
            float radSqr = radial.sqrMagnitude;
            if (radSqr > rSqr)
                return Vector3.zero;

            float radialFalloff = 1f - radSqr / rSqr;
            float axialFalloff = 1f - axial / Length;
            return Axis * (ExcessSpeed * radialFalloff * axialFalloff);
        }
    }

    /// <summary>
    /// Propulsion model. Thrust depends on throttle, airspeed (propeller "ram" / advance-ratio lapse)
    /// and atmospheric density. Also emits engine torque reaction and a momentum-theory slipstream
    /// consumed by downstream <see cref="AeroSurface"/> components.
    /// </summary>
    [AddComponentMenu("Airplane/Aircraft Engine")]
    public sealed class AircraftEngine : MonoBehaviour
    {
        [Header("Thrust")]
        [Tooltip("Maximum static thrust at sea level, Newtons. ~0.4× weight (≈ 6 kN) for a 1500 kg trainer.")]
        [SerializeField] private float maxStaticThrust = 6200f;

        [Tooltip("Advance ratio at which net thrust falls to zero (m/s TAS). Propellers unload with airspeed.")]
        [SerializeField] private float zeroThrustAirspeed = 125f;

        [Tooltip("Density exponent n in T ∝ (ρ/ρ₀)^n. Props ≈ 1.0, high-bypass turbofans ≈ 0.7–1.0.")]
        [SerializeField] [Range(0.4f, 1.2f)] private float densityExponent = 1f;

        [Tooltip("Idle thrust fraction with throttle at 0. Keeps the prop turning and a trickle of slipstream.")]
        [SerializeField] [Range(0f, 0.15f)] private float idleThrottle = 0.04f;

        [Header("Geometry")]
        [Tooltip("Thrust application point. Defaults to this transform (PropellerMount).")]
        [SerializeField] private Transform thrustTransform;

        [Tooltip("Thrust axis in the thrust-transform local frame. (1,0,0) = forward.")]
        [SerializeField] private Vector3 localThrustAxis = Vector3.right;

        [Tooltip("Propeller disc radius, metres. Used for momentum-theory induced velocity.")]
        [SerializeField] private float propellerRadius = 1.05f;

        [Tooltip("Far-wake length of the slipstream cylinder, metres.")]
        [SerializeField] private float slipstreamLength = 11f;

        [Header("Engine Torque Reaction")]
        [Tooltip("Roll torque opposite propeller rotation, N·m per Newton of thrust. Sign: +1 = American engine (clockwise from cockpit) producing left-roll reaction.")]
        [SerializeField] private float torqueReactionPerNewton = 0.012f;

        [Tooltip("Propeller rotation sign in the body frame. +1 = spinning about +X (right-hand). Reaction torque is −sign × thrust.")]
        [SerializeField] private float propellerSpinSign = 1f;

        [Header("Gyroscopic Precession (optional)")]
        [Tooltip("If enabled, applies τ = I_prop ω_prop × Ω_aircraft. Noticeable on powerful props during pitch/yaw.")]
        [SerializeField] private bool enableGyroPrecession;

        [Tooltip("Polar moment of the spinning group, kg·m².")]
        [SerializeField] private float propellerInertia = 8f;

        [Tooltip("Prop RPM at full throttle (visual + gyro).")]
        [SerializeField] private float fullThrottleRpm = 2700f;

        [Header("Visual")]
        [Tooltip("Optional disc/blade transform spun in LateUpdate. Purely cosmetic.")]
        [SerializeField] private Transform propellerVisual;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private float gizmoThrustScale = 0.0005f;

        private float _throttle01 = 0.7f;
        private float _lastThrust;
        private Vector3 _lastThrustWorld;
        private Vector3 _lastThrustPoint;
        private float _lastExcessWake;
        private AircraftFlightController _controller;
        private PlaneRigidbody _body;
        private Vector3 _thrustLocalPos;
        private Quaternion _thrustLocalRot;
        private bool _thrustPoseCached;

        public float Throttle01 => _throttle01;
        public float LastThrust => _lastThrust;
        public Transform ThrustTransform => thrustTransform != null ? thrustTransform : transform;

        public void SetThrustTransform(Transform t)
        {
            thrustTransform = t;
        }

        public void SetPropellerVisual(Transform visual)
        {
            propellerVisual = visual;
        }

        public void Configure(float staticThrust, float radius, float zeroThrustTas)
        {
            maxStaticThrust = staticThrust;
            propellerRadius = radius;
            zeroThrustAirspeed = zeroThrustTas;
        }

        private void Awake()
        {
            if (!thrustTransform)
                thrustTransform = transform;
            _controller = GetComponentInParent<AircraftFlightController>();
            _body = GetComponentInParent<PlaneRigidbody>();
            CacheThrustPose();
        }

        private void OnEnable()
        {
            if (!_body)
                _body = GetComponentInParent<PlaneRigidbody>();
            CacheThrustPose();
        }

        private void CacheThrustPose()
        {
            if (!_body)
                return;
            Transform mount = ThrustTransform;
            Transform root = _body.transform;
            _thrustLocalPos = root.InverseTransformPoint(mount.position);
            _thrustLocalRot = Quaternion.Inverse(root.rotation) * mount.rotation;
            _thrustPoseCached = true;
        }

        private void GetThrustWorld(PlaneRigidbody body, out Vector3 point, out Vector3 axisWorld)
        {
            if (!_thrustPoseCached)
                CacheThrustPose();
            point = body.TransformPoint(_thrustLocalPos);
            axisWorld = (body.Orientation * _thrustLocalRot) * localThrustAxis.normalized;
        }

        private void LateUpdate()
        {
            // Tracked here as well as in ContributeForces so the disc still spins on an aircraft whose
            // solver is off because another peer owns it.
            if (_controller)
                _throttle01 = _controller.Throttle01;

            if (!propellerVisual)
                return;
            float rpm = Mathf.Lerp(400f, fullThrottleRpm, _throttle01);
            propellerVisual.Rotate(new Vector3(0, 1, 0), rpm * 6f * Time.deltaTime, Space.Self);
        }

        /// <summary>
        /// Called by <see cref="PlaneRigidbody"/> once per sub-step. Reads throttle from the controller.
        /// </summary>
        public void ContributeForces(PlaneRigidbody body, in AtmosphereSample atmo, float dt)
        {
            if (_controller)
                _throttle01 = _controller.Throttle01;

            GetThrustWorld(body, out Vector3 point, out Vector3 axisWorld);
            
            float tas = body.TrueAirspeed;
            float densityRatio = atmo.Density / AtmosphericModel.StandardSeaLevelDensity;
            if (densityRatio < 0.05f)
                densityRatio = 0.05f;
            
            float tEff = idleThrottle + (1f - idleThrottle) * FlightSimMath.Saturate(_throttle01);
            float speedLapse = 1f - FlightSimMath.Saturate(tas / Mathf.Max(10f, zeroThrustAirspeed));
            // Slight residual high-speed thrust so the model does not go strictly propeller-idle at Vmax.
            speedLapse = 0.08f + 0.92f * speedLapse;
            
            float thrust = maxStaticThrust * tEff * Mathf.Pow(densityRatio, densityExponent) * speedLapse;
            _lastThrust = thrust;
            _lastThrustWorld = axisWorld * thrust;
            _lastThrustPoint = point;
            
            body.AddForceAtPosition(_lastThrustWorld, point);
            
            if (torqueReactionPerNewton != 0f && thrust > 1f)
            {
                Vector3 reactionWorld = axisWorld * (-propellerSpinSign * torqueReactionPerNewton * thrust);
                body.AddTorqueWorld(reactionWorld);
            }

            if (enableGyroPrecession && propellerInertia > 0f)
            {
                float omegaProp = (fullThrottleRpm * tEff) * (2f * Mathf.PI / 60f);
                Vector3 lPropWorld = axisWorld * (propellerSpinSign * propellerInertia * omegaProp);
                Vector3 tauGyro = Vector3.Cross(body.AngularVelocityWorld, lPropWorld);
                body.AddTorqueWorld(tauGyro);
            }

            // Momentum theory: T = 2 ρ A v_i (V + v_i)  →  v_i = 0.5 (−V + √(V² + T/(ρ A)))
            // Far-wake excess is 2 v_i.
            float area = Mathf.PI * propellerRadius * propellerRadius;
            float vAxial = Vector3.Dot(body.GetPointVelocity(point) - AtmosphericModel.SampleWind(), axisWorld);
            if (vAxial < 0f)
                vAxial = 0f;
            float discTerm = thrust / Mathf.Max(1f, 2f * atmo.Density * area);
            float vi = 0.5f * (-vAxial + Mathf.Sqrt(vAxial * vAxial + 2f * discTerm * 2f));
            if (vi < 0f)
                vi = 0f;
            _lastExcessWake = 2f * vi;
        }

        public PropWashState BuildPropWash()
        {
            if (_body != null)
            {
                GetThrustWorld(_body, out Vector3 point, out Vector3 axisWorld);
                return new PropWashState(
                    _lastExcessWake > 0.05f,
                    point,
                    axisWorld,
                    propellerRadius * 0.85f,
                    slipstreamLength,
                    _lastExcessWake);
            }

            Transform mount = ThrustTransform;
            Vector3 axis = mount.TransformDirection(localThrustAxis.normalized);
            return new PropWashState(
                _lastExcessWake > 0.05f,
                mount.position,
                axis,
                propellerRadius * 0.85f,
                slipstreamLength,
                _lastExcessWake);
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos)
                return;

            Transform mount = ThrustTransform;
            Vector3 p = mount.position;
            Vector3 axis = mount.TransformDirection(localThrustAxis.normalized);
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Gizmos.DrawLine(p, p + axis * 1.4f);
            Gizmos.DrawWireSphere(p, 0.12f);

            if (Application.isPlaying && _lastThrust > 1f)
            {
                Gizmos.color = new Color(1f, 0.35f, 0.05f, 1f);
                Gizmos.DrawLine(_lastThrustPoint, _lastThrustPoint + _lastThrustWorld * gizmoThrustScale);
            }

            Gizmos.color = new Color(0.6f, 0.8f, 1f, 0.25f);
            Gizmos.DrawWireSphere(p + axis * 0.05f, propellerRadius);
        }
    }
}
