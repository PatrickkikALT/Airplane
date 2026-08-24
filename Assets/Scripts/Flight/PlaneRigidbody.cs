using UnityEngine;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Custom rigidbody
    ///
    /// State (SI units):
    ///   r  position of the centre of mass (world, m)
    ///   v  linear velocity of the CoM (world, m/s)
    ///   a  linear acceleration of the CoM (world, m/s²) — diagnostic, from last step
    ///   q  orientation (world, body-to-world)
    ///   ω  angular velocity (body frame, rad/s)
    ///   α  angular acceleration (body frame, rad/s²) — diagnostic, from last step
    ///
    /// Rotational dynamics (Euler's equation, body frame):
    ///   α = I⁻¹ ( Στ − ω × (I ω) )
    ///
    /// Integration runs in FixedUpdate at Time.fixedDeltaTime with optional sub-stepping.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Airplane/Rigidbody")]
    public sealed class PlaneRigidbody : MonoBehaviour
    {
        [Header("Mass & Inertia")]
        [Tooltip("Mass of the vehicle, kg. Baseline trainer = 1500 kg.")]
        [SerializeField] private float mass = 1500f;

        [Tooltip("Ixx — roll inertia about body +X (forward), kg·m². Smallest of the three for a conventional airframe.")]
        [SerializeField] private float inertiaIxx = 2200f;

        [Tooltip("Iyy — yaw inertia about body +Y (up), kg·m². Largest of the three.")]
        [SerializeField] private float inertiaIyy = 5200f;

        [Tooltip("Izz — pitch inertia about body +Z (right), kg·m².")]
        [SerializeField] private float inertiaIzz = 3600f;

        [Tooltip("Product of inertia Ixy, kg·m². Leave 0 for a left/right-symmetric airframe.")]
        [SerializeField] private float inertiaIxy;

        [Tooltip("Product of inertia Ixz, kg·m². Small negative values appear on taildraggers; 0 is fine for a trainer.")]
        [SerializeField] private float inertiaIxz;

        [Tooltip("Product of inertia Iyz, kg·m². Leave 0 for a left/right-symmetric airframe.")]
        [SerializeField] private float inertiaIyz;

        [Tooltip("Centre of mass in local (body) coordinates, metres. Forces are applied about this point. Keep at origin for the default prefab.")]
        [SerializeField] private Vector3 centerOfMassBody;

        [Header("Integration")]
        [Tooltip("Semi-implicit Euler is recommended. RK4 is more accurate but evaluates aero 4× per sub-step.")]
        [SerializeField] private IntegrationScheme integrationScheme = IntegrationScheme.SemiImplicitEuler;

        [Tooltip("Sub-steps per FixedUpdate. 4 at 50 Hz physics → 5 ms steps, enough for stall break and ground springs.")]
        [SerializeField] [Range(1, 16)] private int substeps = 4;

        [Tooltip("Lerp the visible transform between physics ticks. Without this the aircraft (and any chase camera) stutter at FixedUpdate rate.")]
        [SerializeField] private bool interpolateTransform = true;

        [Tooltip("Hard clamp on |a| (m/s²) to survive a bad initial condition. 0 disables.")]
        [SerializeField] private float maxLinearAcceleration = 2000f;

        [Tooltip("Hard clamp on |α| (rad/s²). 0 disables.")]
        [SerializeField] private float maxAngularAcceleration = 80f;

        [Header("Initial Conditions (body frame)")]
        [Tooltip("Initial linear velocity in body axes, m/s. (55, 0, 0) is a typical unstick / cruise TAS so the aircraft is immediately flyable.")]
        [SerializeField] private Vector3 initialVelocityBody = new Vector3(55f, 0f, 0f);

        [Tooltip("Initial angular velocity in body axes, rad/s.")]
        [SerializeField] private Vector3 initialAngularVelocityBody;

        [Header("Ground Contact")]
        [Tooltip("Simple spring-damper landing gear so WheelBrakes have a surface to bite. Does not use a Rigidbody.")]
        [SerializeField] private bool enableLandingGear = true;

        [Tooltip("World-Y of the infinite runway plane. Used when no collider is hit (and always in InfinitePlane mode).")]
        [SerializeField] private float groundPlaneY;

        [Tooltip("If true, raycasts down from each gear point (PhysX query only — the aircraft is still integrated here).")]
        [SerializeField] private bool raycastGround;

        [SerializeField] private LayerMask groundMask = ~0;

        [Tooltip("Gear attach points in body frame, metres. Y is typically negative (below CoM).")]
        [SerializeField] private Vector3[] gearAttachBody =
        {
            new Vector3(2.05f, -0.35f, 0f),
            new Vector3(-0.45f, -0.35f, -1.25f),
            new Vector3(-0.45f, -0.35f, 1.25f)
        };

        [Tooltip("Uncompressed strut length, metres.")]
        [SerializeField] private float gearRestLength = 0.95f;

        [Tooltip("Vertical stiffness per strut, N/m. ~m*g / 0.15 m compression at rest.")]
        [SerializeField] private float gearSpring = 55000f;

        [Tooltip("Vertical damping per strut, N·s/m.")]
        [SerializeField] private float gearDamper = 6500f;

        [Tooltip("Rolling-resistance friction coefficient (brakes released).")]
        [SerializeField] private float rollingFriction = 0.025f;

        [Tooltip("Locked-wheel friction coefficient at full brake.")]
        [SerializeField] private float brakingFriction = 0.75f;

        [Tooltip("Nose-gear index into Gear Attach Body used for tiller steering.")]
        [SerializeField] private int noseGearIndex;

        [Tooltip("Max nose-wheel steer angle, degrees, at walking speed.")]
        [SerializeField] private float noseWheelSteerDeg = 35f;

        [Tooltip("Ground speed (m/s) at which nose-wheel steer fades to 0 (aerodynamic rudder takes over).")]
        [SerializeField] private float noseSteerFadeSpeed = 25f;

        [Header("Debug Gizmos")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private float gizmoForceScale = 0.0004f;
        [SerializeField] private float gizmoVelocityScale = 0.15f;

        private Mat3 _inertia;
        private Mat3 _inertiaInverse;
        private bool _tensorDirty = true;

        private Vector3 _position;
        private Vector3 _velocity;
        private Vector3 _acceleration;
        private Quaternion _orientation = Quaternion.identity;
        private Vector3 _omegaBody;
        private Vector3 _alphaBody;
        private Vector3 _prevPosition;
        private Quaternion _prevOrientation = Quaternion.identity;

        private Vector3 _forceWorld;
        private Vector3 _torqueBody;

        private Vector3 _lastAeroForceWorld;
        private Vector3 _lastTotalForceWorld;
        private bool _initialized;
        private bool _anyGearDown;

        private AeroSurface[] _surfaces;
        private AircraftEngine _engine;
        private AircraftFlightController _controller;

        public float Mass => mass;
        public Vector3 Position => _position;
        public Vector3 Velocity => _velocity;
        public Vector3 Acceleration => _acceleration;
        public Quaternion Orientation => _orientation;
        public Vector3 AngularVelocityBody => _omegaBody;
        public Vector3 AngularAccelerationBody => _alphaBody;
        public Vector3 CenterOfMassWorld => _position;
        public Vector3 LastAeroForceWorld => _lastAeroForceWorld;
        public Vector3 LastTotalForceWorld => _lastTotalForceWorld;
        public bool AnyGearDown => _anyGearDown;

        public Vector3 AngularVelocityWorld
        {
            get { return _orientation * _omegaBody; }
        }

        public float TrueAirspeed
        {
            get { return FlightSimMath.SafeMagnitude(_velocity - AtmosphericModel.SampleWind()); }
        }
        
        public void SetMassAndInertia(float newMass, float ixx, float iyy, float izz, float ixy = 0f, float ixz = 0f, float iyz = 0f)
        {
            mass = Mathf.Max(1f, newMass);
            inertiaIxx = Mathf.Max(0.01f, ixx);
            inertiaIyy = Mathf.Max(0.01f, iyy);
            inertiaIzz = Mathf.Max(0.01f, izz);
            inertiaIxy = ixy;
            inertiaIxz = ixz;
            inertiaIyz = iyz;
            _tensorDirty = true;
            RebuildInertia();
        }

        public void SetCenterOfMassBody(Vector3 com)
        {
            centerOfMassBody = com;
        }

        private void Awake()
        {
            CacheSiblings();
            RebuildInertia();
        }

        private void OnEnable()
        {
            CacheSiblings();
        }

        private void Start()
        {
            CaptureFromTransform();
            _prevPosition = _position;
            _prevOrientation = _orientation;
            _velocity = _orientation * initialVelocityBody;
            _omegaBody = initialAngularVelocityBody;
            _initialized = true;
        }

        private void OnValidate()
        {
            mass = Mathf.Max(1f, mass);
            inertiaIxx = Mathf.Max(0.01f, inertiaIxx);
            inertiaIyy = Mathf.Max(0.01f, inertiaIyy);
            inertiaIzz = Mathf.Max(0.01f, inertiaIzz);
            substeps = Mathf.Clamp(substeps, 1, 16);
            _tensorDirty = true;
        }
        

        private void CacheSiblings()
        {
            _surfaces = GetComponentsInChildren<AeroSurface>(true);
            _engine = GetComponentInChildren<AircraftEngine>(true);
            _controller = GetComponent<AircraftFlightController>();
        }

        private void CaptureFromTransform()
        {
            _orientation = FlightSimMath.Normalize(transform.rotation);
            _position = transform.TransformPoint(centerOfMassBody);
        }

        private void RebuildInertia()
        {
            _inertia = Mat3.Inertia(inertiaIxx, inertiaIyy, inertiaIzz, inertiaIxy, inertiaIxz, inertiaIyz);
            if (!_inertia.TryInvert(out _inertiaInverse))
            {
                Debug.LogError("CustomRigidBody6DOF: inertia tensor is singular. Check Ixx/Iyy/Izz.", this);
                _inertiaInverse = Mat3.Diagonal(1f / inertiaIxx, 1f / inertiaIyy, 1f / inertiaIzz);
            }

            _tensorDirty = false;
        }

        private void FixedUpdate()
        {
            if (!_initialized)
            {
                CaptureFromTransform();
                _prevPosition = _position;
                _prevOrientation = _orientation;
                _initialized = true;
            }

            if (_tensorDirty)
                RebuildInertia();

            if (_surfaces == null)
                CacheSiblings();

            float dt = Time.fixedDeltaTime;
            if (dt <= 0f)
                return;

            if (_controller != null)
                _controller.PrePhysicsTick(dt);

            _prevPosition = _position;
            _prevOrientation = _orientation;

            int steps = substeps;
            float h = dt / steps;
            for (int i = 0; i < steps; i++)
            {
                if (integrationScheme == IntegrationScheme.RK4)
                    StepRK4(h);
                else
                    StepSemiImplicitEuler(h);
            }

            if (!interpolateTransform)
                ApplyToTransform(_position, _orientation);
        }

        private void Update()
        {
            if (!interpolateTransform || !_initialized)
                return;

            float alpha = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                : 1f;
            Vector3 pos = Vector3.Lerp(_prevPosition, _position, alpha);
            Quaternion rot = Quaternion.Slerp(_prevOrientation, _orientation, alpha);
            ApplyToTransform(pos, rot);
        }

        /// <summary>World-frame force, Newtons. Accumulated until the next integration sub-step.</summary>
        public void AddForce(Vector3 forceWorld)
        {
            _forceWorld += forceWorld;
        }

        /// <summary>Torque in the body frame, N * m. Use <see cref="AddTorqueWorld"/> for a world-frame moment.</summary>
        public void AddTorque(Vector3 torqueBody)
        {
            _torqueBody += torqueBody;
        }

        public void AddTorqueWorld(Vector3 torqueWorld)
        {
            _torqueBody += Quaternion.Inverse(_orientation) * torqueWorld;
        }

        /// <summary>
        /// Applies a world-frame force at a world-space point and the resulting moment about the CoM.
        /// τ_world = r * F, then rotated into the body frame for Euler's equation.
        /// </summary>
        public void AddForceAtPosition(Vector3 forceWorld, Vector3 worldPoint)
        {
            _forceWorld += forceWorld;
            Vector3 r = worldPoint - _position;
            _torqueBody += Quaternion.Inverse(_orientation) * Vector3.Cross(r, forceWorld);
        }

        public Vector3 GetPointVelocity(Vector3 worldPoint)
        {
            return _velocity + Vector3.Cross(AngularVelocityWorld, worldPoint - _position);
        }

        public Vector3 TransformDirection(Vector3 bodyDirection)
        {
            return _orientation * bodyDirection;
        }

        public Vector3 InverseTransformDirection(Vector3 worldDirection)
        {
            return Quaternion.Inverse(_orientation) * worldDirection;
        }

        public Vector3 TransformPoint(Vector3 bodyPoint)
        {
            return _position + _orientation * (bodyPoint - centerOfMassBody);
        }

        private void ClearAccumulators()
        {
            _forceWorld = Vector3.zero;
            _torqueBody = Vector3.zero;
        }

        private void EvaluateForces(float dt)
        {
            ClearAccumulators();

            AddForce(mass * AtmosphericModel.SampleGravity());

            AtmosphereSample atmo = AtmosphericModel.SampleAt(_position);
            Vector3 wind = AtmosphericModel.SampleWind();

            PropWashState wash = default;
            if (_engine)
            {
                _engine.ContributeForces(this, in atmo, dt);
                wash = _engine.BuildPropWash();
            }

            Vector3 aeroSum = Vector3.zero;
            if (_surfaces != null)
            {
                foreach (var surface in _surfaces)
                {
                    if (!surface || !surface.isActiveAndEnabled)
                        continue;
                    surface.ContributeForces(this, in atmo, wind, in wash);
                    aeroSum += surface.LastForceWorld;
                }
            }

            _lastAeroForceWorld = aeroSum;

            if (enableLandingGear)
                ContributeLandingGear();

            _lastTotalForceWorld = _forceWorld;
        }

        private void ComputeAccelerations()
        {
            float invMass = 1f / mass;
            Vector3 a = _forceWorld * invMass;
            if (maxLinearAcceleration > 0f)
            {
                float maxA = maxLinearAcceleration;
                float magSqr = a.sqrMagnitude;
                if (magSqr > maxA * maxA)
                    a *= maxA / Mathf.Sqrt(magSqr);
            }

            Vector3 iOmega = _inertia.Multiply(_omegaBody);
            Vector3 gyro = Vector3.Cross(_omegaBody, iOmega);
            Vector3 alpha = _inertiaInverse.Multiply(_torqueBody - gyro);
            if (maxAngularAcceleration > 0f)
            {
                float maxAl = maxAngularAcceleration;
                float magSqr = alpha.sqrMagnitude;
                if (magSqr > maxAl * maxAl)
                    alpha *= maxAl / Mathf.Sqrt(magSqr);
            }

            _acceleration = a;
            _alphaBody = alpha;
        }

        private void StepSemiImplicitEuler(float dt)
        {
            EvaluateForces(dt);
            ComputeAccelerations();

            _velocity += _acceleration * dt;
            _position += _velocity * dt;

            _omegaBody += _alphaBody * dt;
            _orientation = FlightSimMath.IntegrateQuaternionEuler(_orientation, _omegaBody, dt);

            SanitizeState();
        }

        private void StepRK4(float dt)
        {
            RigidBodyState s0 = StoreState();

            RigidBodyDerivatives k1 = EvaluateDerivatives(dt);
            ApplyState(Advance(s0, k1, dt * 0.5f));

            RigidBodyDerivatives k2 = EvaluateDerivatives(dt);
            ApplyState(Advance(s0, k2, dt * 0.5f));

            RigidBodyDerivatives k3 = EvaluateDerivatives(dt);
            ApplyState(Advance(s0, k3, dt));

            RigidBodyDerivatives k4 = EvaluateDerivatives(dt);

            _position = s0.Position + (dt / 6f) * (k1.Velocity + 2f * k2.Velocity + 2f * k3.Velocity + k4.Velocity);
            _velocity = s0.Velocity + (dt / 6f) * (k1.Acceleration + 2f * k2.Acceleration + 2f * k3.Acceleration + k4.Acceleration);
            _omegaBody = s0.AngularVelocityBody + (dt / 6f) * (k1.AngularAccelerationBody + 2f * k2.AngularAccelerationBody + 2f * k3.AngularAccelerationBody + k4.AngularAccelerationBody);

            Quaternion q =
                FlightSimMath.AddScaled(s0.Orientation, k1.OrientationDot, dt / 6f);
            q = FlightSimMath.AddScaled(q, k2.OrientationDot, dt / 3f);
            q = FlightSimMath.AddScaled(q, k3.OrientationDot, dt / 3f);
            q = FlightSimMath.AddScaled(q, k4.OrientationDot, dt / 6f);
            _orientation = FlightSimMath.Normalize(q);

            _acceleration = k1.Acceleration;
            _alphaBody = k1.AngularAccelerationBody;
            SanitizeState();
        }

        private RigidBodyDerivatives EvaluateDerivatives(float dt)
        {
            EvaluateForces(dt);
            ComputeAccelerations();
            RigidBodyDerivatives d;
            d.Velocity = _velocity;
            d.Acceleration = _acceleration;
            d.OrientationDot = FlightSimMath.QuaternionDerivative(_orientation, _omegaBody);
            d.AngularAccelerationBody = _alphaBody;
            return d;
        }

        private RigidBodyState StoreState()
        {
            RigidBodyState s;
            s.Position = _position;
            s.Velocity = _velocity;
            s.Orientation = _orientation;
            s.AngularVelocityBody = _omegaBody;
            return s;
        }

        private void ApplyState(RigidBodyState s)
        {
            _position = s.Position;
            _velocity = s.Velocity;
            _orientation = s.Orientation;
            _omegaBody = s.AngularVelocityBody;
        }

        private static RigidBodyState Advance(in RigidBodyState s, in RigidBodyDerivatives d, float dt)
        {
            RigidBodyState n;
            n.Position = s.Position + d.Velocity * dt;
            n.Velocity = s.Velocity + d.Acceleration * dt;
            n.Orientation = FlightSimMath.Normalize(FlightSimMath.AddScaled(s.Orientation, d.OrientationDot, dt));
            n.AngularVelocityBody = s.AngularVelocityBody + d.AngularAccelerationBody * dt;
            return n;
        }

        private void SanitizeState()
        {
            if (!FlightSimMath.IsFinite(_position) || !FlightSimMath.IsFinite(_velocity) || !FlightSimMath.IsFinite(_omegaBody))
            {
                _velocity = Vector3.zero;
                _omegaBody = Vector3.zero;
                _acceleration = Vector3.zero;
                _alphaBody = Vector3.zero;
                CaptureFromTransform();
            }
        }

        private void ApplyToTransform(Vector3 comWorld, Quaternion orientation)
        {
            Vector3 worldComOffset = orientation * centerOfMassBody;
            transform.SetPositionAndRotation(comWorld - worldComOffset, orientation);
        }

        private void ContributeLandingGear()
        {
            if (gearAttachBody == null || gearAttachBody.Length == 0)
                return;

            float brake = _controller ? _controller.WheelBrake01 : 0f;
            float yawCmd = _controller ? _controller.RawYaw : 0f;
            float mu = Mathf.Lerp(rollingFriction, brakingFriction, FlightSimMath.Saturate(brake));
            float speed = FlightSimMath.SafeMagnitude(_velocity);
            float steerFade = 1f - FlightSimMath.Saturate(speed / Mathf.Max(1f, noseSteerFadeSpeed));
            float steerRad = yawCmd * noseWheelSteerDeg * FlightSimMath.Deg2Rad * steerFade;

            _anyGearDown = false;
            Vector3 up = Vector3.up;

            for (int i = 0; i < gearAttachBody.Length; i++)
            {
                Vector3 attachWorld = TransformPoint(gearAttachBody[i]);
                Vector3 rayDir = Vector3.down;
                float groundY = groundPlaneY;
                Vector3 groundNormal = up;

                if (raycastGround)
                {
                    if (Physics.Raycast(attachWorld, rayDir, out var hit, gearRestLength + 4f, groundMask, QueryTriggerInteraction.Ignore))
                    {
                        groundY = hit.point.y;
                        groundNormal = hit.normal;
                    }
                }

                float compression = gearRestLength - (attachWorld.y - groundY);
                if (compression <= 0f)
                    continue;

                _anyGearDown = true;
                Vector3 pointVel = GetPointVelocity(attachWorld);
                float vN = Vector3.Dot(pointVel, groundNormal);
                float nForce = gearSpring * compression - gearDamper * vN;
                if (nForce < 0f)
                    nForce = 0f;

                Vector3 contact = attachWorld - groundNormal * (gearRestLength - compression);
                AddForceAtPosition(groundNormal * nForce, contact);

                Vector3 vTan = pointVel - groundNormal * vN;
                Vector3 forward = _orientation * Vector3.right;
                if (i == noseGearIndex && steerRad * steerRad > 1e-8f)
                {
                    Quaternion steer = Quaternion.AngleAxis(steerRad * FlightSimMath.Rad2Deg, groundNormal);
                    forward = steer * (_orientation * Vector3.right);
                }

                Vector3 longDir = FlightSimMath.SafeNormalize(forward - groundNormal * Vector3.Dot(forward, groundNormal));
                if (longDir.sqrMagnitude < 1e-8f)
                    longDir = FlightSimMath.SafeNormalize(vTan);

                Vector3 latDir = Vector3.Cross(groundNormal, longDir);
                float vLong = Vector3.Dot(vTan, longDir);
                float vLat = Vector3.Dot(vTan, latDir);

                // Smooth Coulomb friction: F = −μ N v̂ * ( |v| / (|v|+ε) )
                const float eps = 0.4f;
                float latMu = Mathf.Max(mu, 0.55f);
                Vector3 fLong = longDir * (-mu * nForce * vLong / (Mathf.Abs(vLong) + eps));
                Vector3 fLat = latDir * (-latMu * nForce * vLat / (Mathf.Abs(vLat) + eps));
                AddForceAtPosition(fLong + fLat, contact);
            }
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos)
                return;

            Vector3 com = Application.isPlaying ? _position : transform.TransformPoint(centerOfMassBody);
            Gizmos.color = new Color(1f, 0.2f, 0.85f, 0.95f);
            Gizmos.DrawSphere(com, 0.18f);
            Gizmos.DrawWireSphere(com, 0.28f);

            if (Application.isPlaying)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawLine(com, com + _velocity * gizmoVelocityScale);

                Gizmos.color = Color.white;
                Gizmos.DrawLine(com, com + _lastTotalForceWorld * gizmoForceScale);

                Gizmos.color = new Color(1f, 0.85f, 0.1f, 1f);
                Gizmos.DrawLine(com, com + _lastAeroForceWorld * gizmoForceScale);
            }

            if (!enableLandingGear || gearAttachBody == null)
                return;

            Gizmos.color = new Color(0.4f, 0.9f, 0.3f, 0.8f);
            Quaternion q = Application.isPlaying ? _orientation : transform.rotation;
            Vector3 p = Application.isPlaying ? _position : transform.TransformPoint(centerOfMassBody);
            foreach (var vec3 in gearAttachBody)
            {
                Vector3 attach = p + q * (vec3 - centerOfMassBody);
                Gizmos.DrawLine(attach, attach + Vector3.down * gearRestLength);
                Gizmos.DrawWireSphere(attach + Vector3.down * gearRestLength, 0.08f);
            }
        }
    }
}
