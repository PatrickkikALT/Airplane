using System;
using System.Collections.Generic;
using UnityEngine;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// One contact in a <see cref="PlaneCollision"/>. Mirrors Unity's <see cref="ContactPoint"/>.
    /// </summary>
    public readonly struct PlaneContactPoint
    {
        public readonly Vector3 point;
        public readonly Vector3 normal;
        public readonly Collider thisCollider;
        public readonly Collider otherCollider;
        /// <summary>Negative while overlapping (same sign convention as Unity's <see cref="ContactPoint.separation"/>).</summary>
        public readonly float separation;

        public PlaneContactPoint(Vector3 point, Vector3 normal, Collider thisCollider, Collider otherCollider, float separation)
        {
            this.point = point;
            this.normal = normal;
            this.thisCollider = thisCollider;
            this.otherCollider = otherCollider;
            this.separation = separation;
        }
    }

    /// <summary>
    /// Hit report from <see cref="PlaneRigidbody"/> collider contact.
    /// Same role as Unity's <see cref="Collision"/> for <c>OnCollisionEnter</c>.
    /// </summary>
    public struct PlaneCollision
    {
        public Collider collider;
        public Collider thisCollider;
        public Rigidbody rigidbody;
        public PlaneRigidbody planeBody;
        public Transform transform;
        public GameObject gameObject;
        public Vector3 relativeVelocity;
        public Vector3 impulse;
        public Vector3 point;
        public Vector3 normal;
        public float separation;
        public int contactCount;

        public PlaneContactPoint GetContact(int index)
        {
            if (index < 0 || index >= contactCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return new PlaneContactPoint(point, normal, thisCollider, collider, separation);
        }
    }

    /// <summary>
    /// Custom rigid body. Aircraft dynamics are integrated here; PhysX is not the solver.
    /// Collider contact uses PhysX only as a query / other-body back-end: overlaps are resolved
    /// with sequential impulses so the aircraft can hit static colliders and exchange momentum
    /// with Unity Rigidbodies (and other PlaneRigidbody instances).
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(100)]
    [AddComponentMenu("Airplane/Rigidbody")]
    public sealed class PlaneRigidbody : MonoBehaviour
    {
        [Header("Mass & Inertia")]
        [Tooltip("Mass of the vehicle, kg. Baseline trainer = 1500 kg.")]
        [SerializeField] private float mass = 1500f;

        [Tooltip("Ixx — roll inertia about body +X (forward), kg·m².")]
        [SerializeField] private float inertiaIxx = 2200f;

        [Tooltip("Iyy — yaw inertia about body +Y (up), kg·m².")]
        [SerializeField] private float inertiaIyy = 5200f;

        [Tooltip("Izz — pitch inertia about body +Z (right), kg·m².")]
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

        [Tooltip("Hard clamp on |α| (rad/s²). Keep this modest so the nose does not whip.")]
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

        private Rigidbody _proxyBody;
        private Collider[] _ownColliders = System.Array.Empty<Collider>();
        private BoxCollider _fallbackHull;
        private readonly Collider[] _overlapBuffer = new Collider[48];
        private readonly RaycastHit[] _rayBuffer = new RaycastHit[16];
        private readonly Contact[] _contacts = new Contact[32];
        private int _gizmoContactCount;

        private Dictionary<int, PlaneCollision> _hitsThisTick = new Dictionary<int, PlaneCollision>(16);
        private Dictionary<int, PlaneCollision> _hitsLastTick = new Dictionary<int, PlaneCollision>(16);

        /// <summary>Fired once when contact with a collider begins. Same timing idea as <c>OnCollisionEnter</c>.</summary>
        public event Action<PlaneCollision> CollisionEnter;

        /// <summary>Fired every physics tick while still overlapping that collider.</summary>
        public event Action<PlaneCollision> CollisionStay;

        /// <summary>Fired once when contact with a collider ends.</summary>
        public event Action<PlaneCollision> CollisionExit;

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

            DispatchCollisionEvents();

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

        public void ApplyImpulseAtWorldPoint(Vector3 impulseWorld, Vector3 worldPoint)
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

            _acceleration = k1.Acceleration;
            _alphaBody = k1.AngularAccelerationBody;
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
            if (_proxyBody != null)
            {
                _proxyBody.position = transform.position;
                _proxyBody.rotation = transform.rotation;
            }
        }

        private void ConfigureKinematicProxy()
        {
            _proxyBody = GetComponent<Rigidbody>();
            if (_proxyBody == null)
                _proxyBody = gameObject.AddComponent<Rigidbody>();

            _proxyBody.mass = mass;
            _proxyBody.useGravity = false;
            _proxyBody.isKinematic = true;
            _proxyBody.interpolation = RigidbodyInterpolation.None;
            _proxyBody.collisionDetectionMode = CollisionDetectionMode.Discrete;
            _proxyBody.detectCollisions = false;
            _proxyBody.constraints = RigidbodyConstraints.None;
        }

        private void CacheOwnColliders()
        {
            Collider[] found = GetComponentsInChildren<Collider>(true);
            int count = 0;
            for (int i = 0; i < found.Length; i++)
            {
                Collider c = found[i];
                if (c != null && c.enabled && !c.isTrigger)
                    count++;
            }

            if (count == 0 && createFallbackHull && Application.isPlaying)
            {
                if (_fallbackHull == null)
                {
                    _fallbackHull = gameObject.AddComponent<BoxCollider>();
                    _fallbackHull.size = fallbackHullSize;
                    _fallbackHull.center = fallbackHullCenter;
                    _fallbackHull.isTrigger = false;
                }

                found = GetComponentsInChildren<Collider>(true);
                count = 0;
                for (int i = 0; i < found.Length; i++)
                {
                    Collider c = found[i];
                    if (c != null && c.enabled && !c.isTrigger)
                        count++;
                }
            }

            _ownColliders = new Collider[count];
            int w = 0;
            for (int i = 0; i < found.Length; i++)
            {
                Collider c = found[i];
                if (c != null && c.enabled && !c.isTrigger)
                    _ownColliders[w++] = c;
            }
        }

        private bool IsOwnCollider(Collider c)
        {
            if (c == null)
                return false;
            for (int i = 0; i < _ownColliders.Length; i++)
            {
                if (_ownColliders[i] == c)
                    return true;
            }

            return c.transform == transform || c.transform.IsChildOf(transform);
        }

        private void ResolveColliderContacts()
        {
            if (!enableColliderContact)
                return;

            if (_ownColliders == null || _ownColliders.Length == 0)
                CacheOwnColliders();
            if (_ownColliders.Length == 0)
                return;

            ApplyToTransform(_position, _orientation);
            Physics.SyncTransforms();

            int iterations = Mathf.Max(1, collisionIterations);
            for (int iter = 0; iter < iterations; iter++)
            {
                int n = CollectContacts();
                _gizmoContactCount = n;
                if (n == 0)
                    break;
                SolveContacts(n);
                RecordCollisionHits(n);
                ApplyToTransform(_position, _orientation);
                Physics.SyncTransforms();
            }
        }

        private int CollectContacts()
        {
            int count = 0;
            int mask = collisionMask.value;
            float slop = Mathf.Max(0f, collisionSlop);

            for (int i = 0; i < _ownColliders.Length; i++)
            {
                Collider own = _ownColliders[i];
                if (own == null || !own.enabled || own.isTrigger)
                    continue;

                Bounds bounds = own.bounds;
                int hits = Physics.OverlapBoxNonAlloc(
                    bounds.center,
                    bounds.extents,
                    _overlapBuffer,
                    Quaternion.identity,
                    mask,
                    QueryTriggerInteraction.Ignore);

                for (int h = 0; h < hits; h++)
                {
                    Collider other = _overlapBuffer[h];
                    if (other == null || other == own || other.isTrigger || IsOwnCollider(other))
                        continue;

                    if (!Physics.ComputePenetration(
                            own, own.transform.position, own.transform.rotation,
                            other, other.transform.position, other.transform.rotation,
                            out Vector3 direction, out float distance))
                        continue;

                    if (distance <= slop * 0.25f || direction.sqrMagnitude < 1e-12f)
                        continue;

                    Vector3 n = direction.normalized;
                    Vector3 contactPoint = own.ClosestPoint(other.bounds.center);
                    if ((contactPoint - own.bounds.center).sqrMagnitude < 1e-10f)
                        contactPoint = own.bounds.center - n * (distance * 0.5f);

                    PlaneRigidbody otherPlane = other.GetComponentInParent<PlaneRigidbody>();
                    if (otherPlane == this)
                        continue;
                    if (otherPlane != null && otherPlane.GetEntityId() < GetEntityId())
                        continue;

                    Rigidbody otherRb = other.attachedRigidbody;
                    if (otherPlane != null)
                        otherRb = null;
                    else if (otherRb != null && otherRb.isKinematic && otherRb == _proxyBody)
                        continue;

                    GetContactMaterials(own, other, out float restitution, out float friction);

                    if (count >= _contacts.Length)
                        return count;

                    _contacts[count++] = new Contact
                    {
                        Point = contactPoint,
                        Normal = n,
                        Penetration = distance,
                        Restitution = restitution,
                        Friction = friction,
                        ThisCollider = own,
                        OtherCollider = other,
                        OtherBody = otherRb,
                        OtherPlane = otherPlane
                    };
                }
            }

            return count;
        }

        private void SolveContacts(int count)
        {
            float invMassA = 1f / mass;
            float slop = Mathf.Max(0f, collisionSlop);
            float baumgarte = Mathf.Clamp01(collisionBaumgarte);

            for (int i = 0; i < count; i++)
            {
                Contact c = _contacts[i];
                Vector3 n = c.Normal;
                Vector3 rA = c.Point - _position;

                float invMassB = 0f;
                Vector3 rB = Vector3.zero;
                Vector3 vB = Vector3.zero;
                bool otherIsDynamicRb = c.OtherBody != null && !c.OtherBody.isKinematic;
                if (c.OtherPlane != null)
                {
                    invMassB = 1f / c.OtherPlane.mass;
                    rB = c.Point - c.OtherPlane._position;
                    vB = c.OtherPlane.GetPointVelocity(c.Point);
                }
                else if (c.OtherBody != null)
                {
                    rB = c.Point - c.OtherBody.worldCenterOfMass;
                    vB = c.OtherBody.GetPointVelocity(c.Point);
                    if (otherIsDynamicRb && c.OtherBody.mass > 1e-6f)
                        invMassB = 1f / c.OtherBody.mass;
                }

                Vector3 vA = GetPointVelocity(c.Point);
                Vector3 vRel = vA - vB;
                c.RelativeVelocity = vRel;
                float vN = Vector3.Dot(vRel, n);

                float invMn = InverseMassAlong(rA, n, invMassA);
                if (c.OtherPlane != null)
                    invMn += c.OtherPlane.InverseMassAlong(rB, n, invMassB);
                else if (otherIsDynamicRb)
                    invMn += RigidbodyInverseMassAlong(c.OtherBody, rB, n);
                if (invMn < 1e-8f)
                {
                    _contacts[i] = c;
                    continue;
                }

                float jn = 0f;
                if (vN < 0f)
                    jn = -(1f + c.Restitution) * vN / invMn;

                Vector3 impulse = n * jn;

                Vector3 vTan = vRel - n * vN;
                float vTanMag = vTan.magnitude;
                if (vTanMag > 1e-4f && c.Friction > 1e-6f)
                {
                    Vector3 t = vTan / vTanMag;
                    float invMt = InverseMassAlong(rA, t, invMassA);
                    if (c.OtherPlane != null)
                        invMt += c.OtherPlane.InverseMassAlong(rB, t, invMassB);
                    else if (otherIsDynamicRb)
                        invMt += RigidbodyInverseMassAlong(c.OtherBody, rB, t);
                    if (invMt > 1e-8f)
                    {
                        float jt = -vTanMag / invMt;
                        float maxJt = c.Friction * Mathf.Abs(jn);
                        if (jt > maxJt) jt = maxJt;
                        else if (jt < -maxJt) jt = -maxJt;
                        impulse += t * jt;
                    }
                }

                c.Impulse = impulse;
                _contacts[i] = c;

                ApplyImpulseAtWorldPoint(impulse, c.Point);
                if (c.OtherPlane != null)
                    c.OtherPlane.ApplyImpulseAtWorldPoint(-impulse, c.Point);
                else if (otherIsDynamicRb)
                {
                    c.OtherBody.WakeUp();
                    c.OtherBody.AddForceAtPosition(-impulse, c.Point, ForceMode.Impulse);
                }

                float correction = (c.Penetration - slop) * baumgarte;
                if (correction > 0f)
                {
                    float wA = invMassA;
                    float wB = otherIsDynamicRb || c.OtherPlane != null ? invMassB : 0f;
                    float wSum = wA + wB;
                    if (wSum < 1e-8f)
                        wSum = wA;
                    Vector3 corr = n * correction;
                    _position += corr * (wA / wSum);
                    if (c.OtherPlane != null)
                        c.OtherPlane._position -= corr * (wB / wSum);
                    else if (otherIsDynamicRb)
                        c.OtherBody.position -= corr * (wB / wSum);
                }
            }
        }

        private float InverseMassAlong(Vector3 rWorld, Vector3 n, float invMass)
        {
            Vector3 rXn = Vector3.Cross(rWorld, n);
            Vector3 torqueBody = Quaternion.Inverse(_orientation) * rXn;
            Vector3 alphaBody = _inertiaInverse.Multiply(torqueBody);
            Vector3 alphaWorld = _orientation * alphaBody;
            float angular = Vector3.Dot(Vector3.Cross(alphaWorld, rWorld), n);
            return Mathf.Max(1e-8f, invMass + angular);
        }

        private static float RigidbodyInverseMassAlong(Rigidbody rb, Vector3 rWorld, Vector3 n)
        {
            if (rb == null || rb.isKinematic)
                return 0f;

            float invMass = rb.mass > 1e-6f ? 1f / rb.mass : 0f;
            Quaternion rot = rb.rotation * rb.inertiaTensorRotation;
            Vector3 tauLocal = Quaternion.Inverse(rot) * Vector3.Cross(rWorld, n);
            Vector3 I = rb.inertiaTensor;
            Vector3 alphaLocal = new Vector3(
                Mathf.Abs(I.x) > 1e-8f ? tauLocal.x / I.x : 0f,
                Mathf.Abs(I.y) > 1e-8f ? tauLocal.y / I.y : 0f,
                Mathf.Abs(I.z) > 1e-8f ? tauLocal.z / I.z : 0f);
            Vector3 alphaWorld = rot * alphaLocal;
            float angular = Vector3.Dot(Vector3.Cross(alphaWorld, rWorld), n);
            return Mathf.Max(0f, invMass + angular);
        }

        private void GetContactMaterials(Collider a, Collider b, out float restitution, out float friction)
        {
            PhysicsMaterial ma = a != null ? a.sharedMaterial : null;
            PhysicsMaterial mb = b != null ? b.sharedMaterial : null;

            float eA = ma != null ? ma.bounciness : collisionRestitution;
            float eB = mb != null ? mb.bounciness : collisionRestitution;
            float fA = ma != null ? ma.dynamicFriction : collisionFriction;
            float fB = mb != null ? mb.dynamicFriction : collisionFriction;

            PhysicsMaterialCombine bounceMode = DominantCombine(
                ma != null ? ma.bounceCombine : PhysicsMaterialCombine.Average,
                mb != null ? mb.bounceCombine : PhysicsMaterialCombine.Average);
            PhysicsMaterialCombine frictionMode = DominantCombine(
                ma != null ? ma.frictionCombine : PhysicsMaterialCombine.Average,
                mb != null ? mb.frictionCombine : PhysicsMaterialCombine.Average);

            restitution = Combine(eA, eB, bounceMode);
            friction = Combine(fA, fB, frictionMode);
        }

        private static PhysicsMaterialCombine DominantCombine(PhysicsMaterialCombine a, PhysicsMaterialCombine b)
        {
            return (PhysicsMaterialCombine)Mathf.Max((int)a, (int)b);
        }

        private static float Combine(float a, float b, PhysicsMaterialCombine mode)
        {
            switch (mode)
            {
                case PhysicsMaterialCombine.Multiply:
                    return a * b;
                case PhysicsMaterialCombine.Minimum:
                    return Mathf.Min(a, b);
                case PhysicsMaterialCombine.Maximum:
                    return Mathf.Max(a, b);
                default:
                    return (a + b) * 0.5f;
            }
        }

        private struct Contact
        {
            public Vector3 Point;
            public Vector3 Normal;
            public float Penetration;
            public float Restitution;
            public float Friction;
            public Vector3 RelativeVelocity;
            public Vector3 Impulse;
            public Collider ThisCollider;
            public Collider OtherCollider;
            public Rigidbody OtherBody;
            public PlaneRigidbody OtherPlane;
        }

        private void RecordCollisionHits(int count)
        {
            for (int i = 0; i < count; i++)
            {
                Contact c = _contacts[i];
                if (c.OtherCollider == null)
                    continue;

                int id = c.OtherCollider.GetEntityId().ToInt();
                if (_hitsThisTick.TryGetValue(id, out PlaneCollision hit))
                {
                    hit.impulse += c.Impulse;
                    hit.contactCount++;
                    if (c.Penetration > -hit.separation)
                    {
                        hit.point = c.Point;
                        hit.normal = c.Normal;
                        hit.separation = -c.Penetration;
                        hit.thisCollider = c.ThisCollider;
                    }
                    _hitsThisTick[id] = hit;
                }
                else
                {
                    Transform otherTransform = c.OtherCollider.transform;
                    _hitsThisTick[id] = new PlaneCollision
                    {
                        collider = c.OtherCollider,
                        thisCollider = c.ThisCollider,
                        rigidbody = c.OtherBody,
                        planeBody = c.OtherPlane,
                        transform = otherTransform,
                        gameObject = otherTransform.gameObject,
                        relativeVelocity = c.RelativeVelocity,
                        impulse = c.Impulse,
                        point = c.Point,
                        normal = c.Normal,
                        separation = -c.Penetration,
                        contactCount = 1
                    };
                }
            }
        }

        private void DispatchCollisionEvents()
        {
            foreach (var kv in _hitsThisTick)
            {
                if (_hitsLastTick.ContainsKey(kv.Key))
                    InvokeCollisionEvent(CollisionStay, "OnPlaneCollisionStay", kv.Value);
                else
                    InvokeCollisionEvent(CollisionEnter, "OnPlaneCollisionEnter", kv.Value);
            }

            foreach (var kv in _hitsLastTick)
            {
                if (!_hitsThisTick.ContainsKey(kv.Key))
                    InvokeCollisionEvent(CollisionExit, "OnPlaneCollisionExit", kv.Value);
            }

            (_hitsLastTick, _hitsThisTick) = (_hitsThisTick, _hitsLastTick);
            _hitsThisTick.Clear();
        }

        private void InvokeCollisionEvent(Action<PlaneCollision> evt, string message, PlaneCollision hit)
        {
            evt?.Invoke(hit);
            SendMessage(message, hit, SendMessageOptions.DontRequireReceiver);
        }

        private void ContributeRateDamping(in AtmosphereSample atmo)
        {
            float q = atmo.DynamicPressure(TrueAirspeed);
            float scale = q / Mathf.Max(50f, dampingReferenceQ);
            if (scale > 2.5f)
                scale = 2.5f;

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

        private bool RaycastIgnoringSelf(Vector3 origin, Vector3 direction, float maxDistance, out RaycastHit hit)
        {
            int n = Physics.RaycastNonAlloc(origin, direction, _rayBuffer, maxDistance, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < n; i++)
            {
                Collider col = _rayBuffer[i].collider;
                if (col == null || IsOwnCollider(col))
                    continue;
                if (_rayBuffer[i].distance < best)
                {
                    best = _rayBuffer[i].distance;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                hit = default;
                return false;
            }

            hit = _rayBuffer[bestIndex];
            return true;
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
