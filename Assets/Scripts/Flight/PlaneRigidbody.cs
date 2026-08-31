using Airplane.Weapons;
using UnityEngine;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Custom rigid body. Aircraft dynamics are integrated here; PhysX is not the solver.
    /// Collider contact uses PhysX only as a query / other-body back-end: overlaps are resolved
    /// with sequential impulses so the aircraft can hit static colliders and exchange momentum
    /// with Unity Rigidbodies (and other PlaneRigidbody instances).
    /// You can also detect collisions with OnPlaneCollisionEnter, for example:
    /// <code>
    ///     private void OnPlaneCollisionEnter(PlaneCollision hit)
    ///     {
    ///         Debug.Log("Hit");
    ///     }
    /// </code>
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Airplane/Rigidbody")]
    public sealed partial class PlaneRigidbody : MonoBehaviour
    {
        [Header("Mass & Inertia")]
        [Tooltip("Mass of the vehicle, kg. Baseline trainer = 1500 kg.")]
        [SerializeField] private float mass = 1500f;

        [Tooltip("Ixx - roll inertia about body +X (forward), kg·m².")]
        [SerializeField] private float inertiaIxx = 2200f;

        [Tooltip("Iyy - yaw inertia about body +Y (up), kg·m².")]
        [SerializeField] private float inertiaIyy = 5200f;

        [Tooltip("Izz - pitch inertia about body +Z (right), kg·m².")]
        [SerializeField] private float inertiaIzz = 3600f;

        [SerializeField] private float inertiaIxy;
        [SerializeField] private float inertiaIxz;
        [SerializeField] private float inertiaIyz;

        [Tooltip("Centre of mass in local (body) coordinates, metres.")]
        [SerializeField] private Vector3 centerOfMassBody;

        [Header("Integration")]
        [SerializeField] private IntegrationScheme integrationScheme = IntegrationScheme.SemiImplicitEuler;

        [SerializeField] [Range(1, 16)] private int substeps = 4;

        [Tooltip("Lerp the visible transform between physics ticks.")]
        [SerializeField] private bool interpolateTransform = true;

        [Tooltip("Hard clamp on |a| (m/s²). 0 disables.")]
        [SerializeField] private float maxLinearAcceleration = 2000f;

        [Tooltip("Hard clamp on |α| per body axis (rad/s²). 0 disables. Applied per axis so pitch does not steal roll.")]
        [SerializeField] private float maxAngularAcceleration = 6f;

        [Header("Initial Conditions (body frame)")]
        [SerializeField] private Vector3 initialVelocityBody = new Vector3(50f, 0f, 0f);
        [SerializeField] private Vector3 initialAngularVelocityBody;

        [Header("Ground Contact")]
        [SerializeField] private bool enableLandingGear = true;
        [SerializeField] private float groundPlaneY;
        [SerializeField] private bool raycastGround;
        [SerializeField] private LayerMask groundMask = ~0;

        [SerializeField] private Vector3[] gearAttachBody =
        {
            new Vector3(2.05f, -0.35f, 0f),
            new Vector3(-0.45f, -0.35f, -1.25f),
            new Vector3(-0.45f, -0.35f, 1.25f)
        };

        [SerializeField] private float gearRestLength = 0.95f;
        [SerializeField] private float gearSpring = 55000f;
        [SerializeField] private float gearDamper = 6500f;
        [SerializeField] private float rollingFriction = 0.025f;
        [SerializeField] private float brakingFriction = 0.75f;
        [SerializeField] private int noseGearIndex;
        [SerializeField] private float noseWheelSteerDeg = 35f;
        [SerializeField] private float noseSteerFadeSpeed = 25f;

        [Header("Collider Contact")]
        [Tooltip("Resolve overlaps against Unity colliders after each integration sub-step.")]
        [SerializeField] private bool enableColliderContact = true;

        [SerializeField] private LayerMask collisionMask = ~0;

        [SerializeField] [Range(1, 8)] private int collisionIterations = 4;

        [Tooltip("Used when a collider has no Physics Material. 0 = inelastic.")]
        [SerializeField] [Range(0f, 1f)] private float collisionRestitution = 0.15f;

        [Tooltip("Coulomb μ used when a collider has no Physics Material.")]
        [SerializeField] private float collisionFriction = 0.4f;

        [Tooltip("Leave this much penetration (m) before positional correction.")]
        [SerializeField] private float collisionSlop = 0.015f;

        [Tooltip("Fraction of overlap removed each iteration.")]
        [SerializeField] [Range(0.1f, 1f)] private float collisionBaumgarte = 0.7f;

        [Tooltip("If the aircraft has no colliders, add a box hull so contact still works.")]
        [SerializeField] private bool createFallbackHull = true;

        [SerializeField] private Vector3 fallbackHullSize = new Vector3(7.5f, 1.2f, 10.5f);
        [SerializeField] private Vector3 fallbackHullCenter;

        [Header("Handling")]
        [Tooltip("Body-axis rate damping (N·m / (rad/s)) at reference q. Higher = slower, heavier rotation.")]
        [SerializeField] private Vector3 angularDamping = new Vector3(8000f, 7000f, 7000f);

        [Tooltip("Dynamic pressure (Pa) at which Angular Damping is quoted. Scales with q.")]
        [SerializeField] private float dampingReferenceQ = 1800f;

        [Tooltip("Hard cap on body rates, degrees/s (roll, yaw, pitch).")]
        [SerializeField] private Vector3 maxAngularSpeedDeg = new Vector3(80f, 28f, 45f);

        [Tooltip("Extra damping multiplier when the stick is released, so rotation stops with the input instead of coasting.")]
        [SerializeField] private float idleRateDamping = 2.2f;

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
        private float _loadFactorNz = 1f;
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
        private bool _simulationEnabled = true;
        private bool _externalStateApplied;

        private AeroSurface[] _surfaces;
        private AircraftEngine _engine;
        private AircraftFlightController _controller;
        private AircraftWeaponsController _weapons;

        public float Mass => mass;
        public Vector3 Position => _position;
        public Vector3 Velocity => _velocity;
        public Vector3 Acceleration => _acceleration;
        public Quaternion Orientation => _orientation;
        public float LoadFactorNz => _loadFactorNz;
        public Vector3 AngularVelocityBody => _omegaBody;
        public Vector3 AngularAccelerationBody => _alphaBody;
        public Vector3 CenterOfMassWorld => _position;
        public Vector3 LastAeroForceWorld => _lastAeroForceWorld;
        public Vector3 LastTotalForceWorld => _lastTotalForceWorld;
        public bool AnyGearDown => _anyGearDown;
        public Vector3 CenterOfMassBody => centerOfMassBody;

        /// <summary>
        /// Instant change in linear and angular velocity from an impulse at a world point.
        /// Recoil and projectile hits use this so they participate in the next integration sub-step.
        /// </summary>
        public void ApplyImpulseAtPosition(Vector3 impulseWorld, Vector3 worldPoint)
        {
            ApplyImpulseAtWorldPoint(impulseWorld, worldPoint);
        }

        /// <summary>
        /// False while another peer owns this aircraft and the pose is being replayed from the network.
        /// Integration, contact solving and controller ticks are all suppressed in that state; the
        /// solver fields are still kept current so airspeed readouts and other bodies' contact maths
        /// see sane values.
        /// </summary>
        public bool SimulationEnabled => _simulationEnabled;

        public Vector3 AngularVelocityWorld => _orientation * _omegaBody;

        public float TrueAirspeed => FlightSimMath.SafeMagnitude(_velocity - AtmosphericModel.SampleWind());

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

        /// <summary>
        /// Enables or suppresses the solver. Disable this on a body whose pose comes from elsewhere
        /// (a network proxy, a cutscene) so it stops integrating and stops fighting the driver.
        /// </summary>
        public void SetSimulationEnabled(bool enable)
        {
            if (_simulationEnabled == enable)
                return;

            _simulationEnabled = enable;

            if (!enable)
            {
                _hitsThisTick.Clear();
                _hitsLastTick.Clear();
                _gizmoContactCount = 0;
                return;
            }

            _prevPosition = _position;
            _prevOrientation = _orientation;
        }

        /// <summary>
        /// Hard-sets the full state and snaps the transform, discarding any interpolation history.
        /// Use for spawning, respawning and teleports.
        /// </summary>
        public void Teleport(Vector3 comWorld, Quaternion orientation, Vector3 velocityWorld, Vector3 angularVelocityBody)
        {
            _orientation = FlightSimMath.Normalize(orientation);
            _position = comWorld;
            _velocity = velocityWorld;
            _omegaBody = angularVelocityBody;
            _acceleration = Vector3.zero;
            _alphaBody = Vector3.zero;
            _prevPosition = _position;
            _prevOrientation = _orientation;
            _hitsThisTick.Clear();
            _hitsLastTick.Clear();
            _initialized = true;
            _externalStateApplied = true;
            ApplyToTransform(_position, _orientation);
        }

        /// <summary>
        /// Drives a non-simulated body from an externally computed state, keeping the previous pose so
        /// gizmos and airspeed readouts stay continuous. Intended for network proxies once per frame.
        /// </summary>
        public void ApplyNetworkState(Vector3 comWorld, Quaternion orientation, Vector3 velocityWorld, Vector3 angularVelocityBody)
        {
            _prevPosition = _position;
            _prevOrientation = _orientation;
            _position = comWorld;
            _orientation = FlightSimMath.Normalize(orientation);
            _velocity = velocityWorld;
            _omegaBody = angularVelocityBody;
            _initialized = true;
            _externalStateApplied = true;
            ApplyToTransform(_position, _orientation);
        }

        private void Awake()
        {
            CacheSiblings();
            RebuildInertia();
            ConfigureKinematicProxy();
            CacheOwnColliders();
        }

        private void OnEnable()
        {
            CacheSiblings();
            ConfigureKinematicProxy();
            CacheOwnColliders();
        }

        private void Start()
        {
            if (_externalStateApplied)
                return;

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
            collisionIterations = Mathf.Clamp(collisionIterations, 1, 8);
            _tensorDirty = true;
        }

        private void CacheSiblings()
        {
            _surfaces = GetComponentsInChildren<AeroSurface>(true);
            _engine = GetComponentInChildren<AircraftEngine>(true);
            _controller = GetComponent<AircraftFlightController>();
            _weapons = GetComponent<AircraftWeaponsController>();
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
                Debug.LogError("PlaneRigidbody: inertia tensor is singular. Check Ixx/Iyy/Izz.", this);
                _inertiaInverse = Mat3.Diagonal(1f / inertiaIxx, 1f / inertiaIyy, 1f / inertiaIzz);
            }

            _tensorDirty = false;
        }

        private void FixedUpdate()
        {
            if (!_simulationEnabled)
                return;

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

            if (_controller)
                _controller.PrePhysicsTick(dt);

            if (_weapons)
                _weapons.PrePhysicsTick(dt);

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

            DispatchCollisionEvents();
            UpdateLoadFactor();

            if (!interpolateTransform)
                ApplyToTransform(_position, _orientation);
        }

        private void Update()
        {
            if (!_simulationEnabled || !interpolateTransform || !_initialized)
                return;

            float alpha = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                : 1f;
            Vector3 pos = Vector3.Lerp(_prevPosition, _position, alpha);
            Quaternion rot = Quaternion.Slerp(_prevOrientation, _orientation, alpha);
            ApplyToTransform(pos, rot);
        }

        public void AddForce(Vector3 forceWorld)
        {
            _forceWorld += forceWorld;
        }

        public void AddTorque(Vector3 torqueBody)
        {
            _torqueBody += torqueBody;
        }

        public void AddTorqueWorld(Vector3 torqueWorld)
        {
            _torqueBody += Quaternion.Inverse(_orientation) * torqueWorld;
        }

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

        private void ApplyImpulseAtWorldPoint(Vector3 impulseWorld, Vector3 worldPoint)
        {
            _velocity += impulseWorld * (1f / mass);
            Vector3 r = worldPoint - _position;
            Vector3 torqueBody = Quaternion.Inverse(_orientation) * Vector3.Cross(r, impulseWorld);
            _omegaBody += _inertiaInverse.Multiply(torqueBody);
        }

        public void TranslateWorld(Vector3 worldDelta)
        {
            _position += worldDelta;
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

            ContributeRateDamping(in atmo);

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
                alpha.x = Mathf.Clamp(alpha.x, -maxAl, maxAl);
                alpha.y = Mathf.Clamp(alpha.y, -maxAl, maxAl);
                alpha.z = Mathf.Clamp(alpha.z, -maxAl, maxAl);
            }

            _acceleration = a;
            _alphaBody = alpha;
        }

        private void UpdateLoadFactor()
        {
            // Seat-pad load factor Nz = (a − g) · bodyUp / g0, with g0 = 9.80665 m/s².
            // Use the force-model acceleration, not Δv/Δt: contact impulses would spike this to tens of G.
            Vector3 g = AtmosphericModel.SampleGravity();
            Vector3 proper = _acceleration - g;
            Vector3 bodyUp = _orientation * Vector3.up;
            float raw = Vector3.Dot(proper, bodyUp) / AtmosphericModel.StandardGravity;
            if (!FlightSimMath.IsFinite(raw))
                raw = _loadFactorNz;
            _loadFactorNz = raw;
        }

        private void StepSemiImplicitEuler(float dt)
        {
            EvaluateForces(dt);
            ComputeAccelerations();

            _velocity += _acceleration * dt;
            _position += _velocity * dt;

            _omegaBody += _alphaBody * dt;
            ClampAngularRates();
            _orientation = FlightSimMath.IntegrateQuaternionEuler(_orientation, _omegaBody, dt);

            SanitizeState();
            ResolveColliderContacts();
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
            ClampAngularRates();

            Quaternion q = FlightSimMath.AddScaled(s0.Orientation, k1.OrientationDot, dt / 6f);
            q = FlightSimMath.AddScaled(q, k2.OrientationDot, dt / 3f);
            q = FlightSimMath.AddScaled(q, k3.OrientationDot, dt / 3f);
            q = FlightSimMath.AddScaled(q, k4.OrientationDot, dt / 6f);
            _orientation = FlightSimMath.Normalize(q);

            _acceleration = (k1.Acceleration + 2f * k2.Acceleration + 2f * k3.Acceleration + k4.Acceleration) / 6f;
            _alphaBody = (k1.AngularAccelerationBody + 2f * k2.AngularAccelerationBody + 2f * k3.AngularAccelerationBody + k4.AngularAccelerationBody) / 6f;
            SanitizeState();
            ResolveColliderContacts();
        }

        private void ClampAngularRates()
        {
            Vector3 maxRad = maxAngularSpeedDeg * FlightSimMath.Deg2Rad;
            if (maxRad.x > 0.01f)
                _omegaBody.x = Mathf.Clamp(_omegaBody.x, -maxRad.x, maxRad.x);
            if (maxRad.y > 0.01f)
                _omegaBody.y = Mathf.Clamp(_omegaBody.y, -maxRad.y, maxRad.y);
            if (maxRad.z > 0.01f)
                _omegaBody.z = Mathf.Clamp(_omegaBody.z, -maxRad.z, maxRad.z);
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
            if (_proxyBody)
            {
                _proxyBody.position = transform.position;
                _proxyBody.rotation = transform.rotation;
            }
        }

        private void ContributeRateDamping(in AtmosphereSample atmo)
        {
            float q = atmo.DynamicPressure(TrueAirspeed);
            float scale = q / Mathf.Max(50f, dampingReferenceQ);
            if (scale > 2.5f)
                scale = 2.5f;

            if (_controller && idleRateDamping > 0f)
            {
                float stick = Mathf.Max(
                    Mathf.Abs(_controller.RawPitch),
                    Mathf.Abs(_controller.RawRoll),
                    Mathf.Abs(_controller.RawYaw));
                float idle = 1f - FlightSimMath.Saturate(stick / 0.2f);
                scale *= 1f + idle * idleRateDamping;
            }

            AddTorque(new Vector3(
                -angularDamping.x * _omegaBody.x * scale,
                -angularDamping.y * _omegaBody.y * scale,
                -angularDamping.z * _omegaBody.z * scale));
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
                    if (RaycastIgnoringSelf(attachWorld, rayDir, gearRestLength + 4f, out var hit))
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

                Gizmos.color = new Color(1f, 0.35f, 0.1f, 1f);
                for (int i = 0; i < _gizmoContactCount && i < _contacts.Length; i++)
                {
                    Vector3 p = _contacts[i].Point;
                    Gizmos.DrawSphere(p, 0.08f);
                    Gizmos.DrawLine(p, p + _contacts[i].Normal * 0.6f);
                }
            }

            if (!enableLandingGear || gearAttachBody == null)
                return;

            Gizmos.color = new Color(0.4f, 0.9f, 0.3f, 0.8f);
            Quaternion q = Application.isPlaying ? _orientation : transform.rotation;
            Vector3 p2 = Application.isPlaying ? _position : transform.TransformPoint(centerOfMassBody);
            foreach (var vec3 in gearAttachBody)
            {
                Vector3 attach = p2 + q * (vec3 - centerOfMassBody);
                Gizmos.DrawLine(attach, attach + Vector3.down * gearRestLength);
                Gizmos.DrawWireSphere(attach + Vector3.down * gearRestLength, 0.08f);
            }
        }
    }
}
