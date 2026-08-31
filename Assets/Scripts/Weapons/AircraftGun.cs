using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.UI;
using UnityEngine;

namespace Airplane.Weapons
{
    /// <summary>
    /// One mount on the airframe. Drop as many as you want as children of the aircraft; the
    /// <see cref="AircraftWeaponsController"/> finds them the same way <see cref="PlaneRigidbody"/>
    /// finds <see cref="AeroSurface"/> components.
    ///
    /// Shot direction is evaluated in the solver pose, not <c>transform.position</c>, because the
    /// visible transform is interpolated in Update and is stale during FixedUpdate sub-steps.
    /// </summary>
    [AddComponentMenu("Airplane/Weapons/Aircraft Gun")]
    public sealed class AircraftGun : MonoBehaviour
    {
        [Header("Mount")]
        [Tooltip("Muzzle. Defaults to this transform.")]
        [SerializeField] private Transform muzzle;

        [Tooltip("Shot axis in the muzzle-transform local frame.")]
        [SerializeField] private Vector3 localMuzzleAxis = Vector3.forward;

        [Tooltip("Which trigger on the weapons controller fires this mount.")]
        [SerializeField] private GunTriggerChannel triggerChannel = GunTriggerChannel.Primary;

        [Header("Ballistics")]
        [SerializeField] private GunFireMode fireMode = GunFireMode.Hitscan;

        [Tooltip("Rounds per second while the trigger is held.")]
        [SerializeField] private float roundsPerSecond = 12f;

        [Tooltip("Muzzle speed added on top of the airframe's point velocity, m/s.")]
        [SerializeField] private float muzzleSpeed = 800f;

        [Tooltip("Hitscan / tracer range, metres.")]
        [SerializeField] private float maxRange = 900f;

        [Tooltip("Cone half-angle, degrees. 0 = perfectly bore-sighted.")]
        [SerializeField] private float spreadDeg = 0.35f;

        [Header("Mass / Impulse")]
        [Tooltip("Recoil impulse applied to the firing aircraft at the muzzle, N·s. Opposite the shot.")]
        [SerializeField] private float recoilImpulse = 45f;

        [Tooltip("Impact impulse delivered to a hit PlaneRigidbody along the incoming velocity, N·s.")]
        [SerializeField] private float impactImpulse = 900f;

        [Tooltip("Hit-point damage forwarded to AircraftVitality. 0 skips vitality.")]
        [SerializeField] private float damage = 8f;

        [Tooltip("Projectile mass, kg. Only used in Projectile mode for ballistic drag.")]
        [SerializeField] private float projectileMass = 0.01f;

        [Tooltip("Projectile reference area, m². π (caliber/2)² for a 7.62 mm slug ≈ 4.6e-5.")]
        [SerializeField] private float projectileArea = 0.000046f;

        [Tooltip("Projectile drag coefficient. Spitzer ~0.3, cannon shell ~0.15–0.25.")]
        [SerializeField] private float projectileCd = 0.3f;

        [Tooltip("Ballistic tracer lifetime, seconds.")]
        [SerializeField] private float projectileLifetime = 4f;

        [Header("Ammo")]
        [Tooltip("Magazine size. 0 = infinite.")]
        [SerializeField] private int ammoCapacity = 250;

        [Tooltip("Seconds to refill an empty magazine. 0 = no reload.")]
        [SerializeField] private float reloadSeconds = 6f;

        [Header("Collision")]
        [SerializeField] private LayerMask hitMask = ~0;

        [Header("Tracer")]
        [Tooltip("Optional projectile prefab. If empty, a pooled tracer is spawned at runtime.")]
        [SerializeField] private AircraftProjectile projectilePrefab;

        [SerializeField] private Color tracerColor = new Color(1f, 0.75f, 0.2f, 1f);
        [SerializeField] private float tracerWidth = 0.045f;
        [SerializeField] private float tracerLifetime = 0.12f;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private float gizmoLength = 1.6f;

        private PlaneRigidbody _body;
        private Vector3 _muzzleLocalPos;
        private Quaternion _muzzleLocalRot;
        private bool _muzzlePoseCached;
        private float _cooldown;
        private int _ammo;
        private float _reloadClock;
        private bool _isFiring;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private AircraftProjectile[] _pool = System.Array.Empty<AircraftProjectile>();
        private int _poolCount;

        public GunTriggerChannel TriggerChannel => triggerChannel;
        public GunFireMode FireMode => fireMode;
        public int AmmoCapacity => ammoCapacity;
        public int AmmoRemaining => ammoCapacity <= 0 ? -1 : _ammo;
        public bool IsFiring => _isFiring;
        public Transform Muzzle => muzzle != null ? muzzle : transform;

        public void RefillAmmo()
        {
            _ammo = ammoCapacity;
            _reloadClock = 0f;
        }

        /// <summary>Muzzle speed added on top of the airframe's point velocity, m/s.</summary>
        public float MuzzleSpeed => muzzleSpeed;

        /// <summary>Hitscan / tracer range, metres.</summary>
        public float MaxRange => maxRange;

        public Vector3 MuzzlePosition => Muzzle.position;

        /// <summary>
        /// World-space shot axis. A fire-control solution has to steer this rather than the fuselage
        /// axis, because a mount is free to be converged or offset.
        /// </summary>
        public Vector3 ShotAxisWorld => Muzzle.TransformDirection(localMuzzleAxis.normalized);

        public void SetMuzzle(Transform t)
        {
            muzzle = t;
            _muzzlePoseCached = false;
        }

        public void Configure(float rps, float speed, float recoil, float impact)
        {
            roundsPerSecond = rps;
            muzzleSpeed = speed;
            recoilImpulse = recoil;
            impactImpulse = impact;
        }

        private void Awake()
        {
            if (!muzzle)
                muzzle = transform;
            _body = GetComponentInParent<PlaneRigidbody>();
            _ammo = ammoCapacity;
            CacheMuzzlePose();
        }

        private void OnEnable()
        {
            if (!_body)
                _body = GetComponentInParent<PlaneRigidbody>();
            CacheMuzzlePose();
        }

        private void CacheMuzzlePose()
        {
            if (!_body)
                return;
            Transform mount = Muzzle;
            Transform root = _body.transform;
            _muzzleLocalPos = root.InverseTransformPoint(mount.position);
            _muzzleLocalRot = Quaternion.Inverse(root.rotation) * mount.rotation;
            _muzzlePoseCached = true;
        }

        private void OnValidate()
        {
            roundsPerSecond = Mathf.Max(0.05f, roundsPerSecond);
            muzzleSpeed = Mathf.Max(1f, muzzleSpeed);
            maxRange = Mathf.Max(1f, maxRange);
            spreadDeg = Mathf.Max(0f, spreadDeg);
            projectileMass = Mathf.Max(0.0001f, projectileMass);
            projectileArea = Mathf.Max(1e-8f, projectileArea);
            ammoCapacity = Mathf.Max(0, ammoCapacity);
        }

        /// <summary>
        /// Called by <see cref="AircraftWeaponsController"/> once per weapons tick.
        /// <paramref name="visualOnly"/> is set on a remote proxy: tracers still spawn, but recoil
        /// and hit authority stay with the owning peer.
        /// </summary>
        public void Tick(AircraftWeaponsController controller, PlaneRigidbody body, float dt, bool visualOnly)
        {
            if (body == null)
                return;
            if (!_muzzlePoseCached)
                CacheMuzzlePose();

            float trigger = controller != null ? controller.ReadTrigger(triggerChannel) : 0f;
            bool wantFire = trigger > 0.5f;
            bool cheats = CheatFlags.AppliesTo(body);

            if (ammoCapacity > 0 && _ammo <= 0 && !(cheats && CheatFlags.InfiniteAmmo))
            {
                _isFiring = false;
                if (reloadSeconds > 0.01f && !visualOnly)
                {
                    _reloadClock += dt;
                    if (_reloadClock >= reloadSeconds)
                    {
                        _ammo = ammoCapacity;
                        _reloadClock = 0f;
                    }
                }

                return;
            }

            _isFiring = false;
            if (!wantFire)
            {
                if (_cooldown < 0f)
                    _cooldown = 0f;
                return;
            }

            _cooldown -= dt;

            float interval = 1f / Mathf.Max(0.05f, roundsPerSecond);
            int safety = 8;
            while (_cooldown <= 0f && safety-- > 0)
            {
                FireOne(body, visualOnly);
                _cooldown += interval;
                _isFiring = true;
                if (ammoCapacity > 0 && _ammo <= 0 && !(CheatFlags.InfiniteAmmo && CheatFlags.AppliesTo(body)))
                    break;
            }
        }

        private void FireOne(PlaneRigidbody body, bool visualOnly)
        {
            GetMuzzleWorld(body, out Vector3 origin, out Vector3 axis);
            Vector3 shotDir = ApplySpread(axis);
            Vector3 inherited = body.GetPointVelocity(origin);
            Vector3 velocity = inherited + shotDir * muzzleSpeed;

            if (!visualOnly)
            {
                if (recoilImpulse > 0f)
                    body.ApplyImpulseAtPosition(-shotDir * recoilImpulse, origin);
                if (ammoCapacity > 0 && !(CheatFlags.InfiniteAmmo && CheatFlags.AppliesTo(body)))
                    _ammo--;
            }

            if (fireMode == GunFireMode.Hitscan && !(CheatFlags.HomingBullets && CheatFlags.AppliesTo(body)))
                FireHitscan(body, origin, shotDir, velocity, visualOnly);
            else
                LaunchProjectile(body, origin, velocity, visualOnly);
        }

        public float ProjectileMass => projectileMass;
        public float ProjectileArea => projectileArea;
        public float ProjectileCd => projectileCd;
        public float ProjectileLifetime => projectileLifetime;

        private void FireHitscan(
            PlaneRigidbody body,
            Vector3 origin,
            Vector3 shotDir,
            Vector3 velocity,
            bool visualOnly)
        {
            // World path is the inherited airframe velocity plus muzzle speed. Raycasting along
            // shotDir alone leaves the tracer stuck in the ground frame, so a moving aircraft
            // appears to outrun its own rounds.
            float speed = FlightSimMath.SafeMagnitude(velocity);
            Vector3 worldDir = speed > 0.01f ? velocity / speed : shotDir;

            if (TryRaycast(origin, worldDir, maxRange, body, out RaycastHit hit))
            {
                if (!visualOnly)
                    ResolveHit(body, hit, velocity);
            }

            AircraftProjectile tracer = RentTracer();
            if (tracer)
                tracer.LaunchKinematic(origin, velocity, tracerLifetime);
        }

        private void LaunchProjectile(PlaneRigidbody body, Vector3 origin, Vector3 velocity, bool visualOnly)
        {
            AircraftProjectile tracer = RentTracer();
            if (!tracer)
                return;

            tracer.SetBallistics(projectileMass, projectileArea, projectileCd, projectileLifetime);
            tracer.LaunchBallistic(this, body, origin, velocity, visualOnly);
        }

        internal void NotifyImpact(PlaneRigidbody shooter, in RaycastHit hit, Vector3 velocity, bool visualOnly)
        {
            if (visualOnly)
                return;
            ResolveHit(shooter, hit, velocity);
        }

        private void ResolveHit(PlaneRigidbody shooter, RaycastHit hit, Vector3 velocity)
        {
            PlaneRigidbody victim = hit.collider ? hit.collider.GetComponentInParent<PlaneRigidbody>() : null;
            if (victim == shooter)
                return;

            Vector3 incoming = velocity;
            float speed = FlightSimMath.SafeMagnitude(incoming);
            Vector3 dir = speed > 0.01f ? incoming / speed : -hit.normal;
            Vector3 impulse = dir * impactImpulse;

            GunHit report = new GunHit
            {
                Point = hit.point,
                Normal = hit.normal,
                Impulse = impulse,
                IncomingVelocity = incoming,
                Damage = damage,
                Collider = hit.collider,
                Victim = victim,
                Shooter = shooter,
                Gun = this
            };

            if (!victim)
                return;

            if (victim.SimulationEnabled)
            {
                ApplyHit(victim, in report);
                return;
            }

            NetworkedAircraft shooterNet = shooter.GetComponent<NetworkedAircraft>();
            NetworkedAircraft victimNet = victim.GetComponent<NetworkedAircraft>();
            if (shooterNet && victimNet && shooterNet.IsSpawned)
                shooterNet.ReportWeaponHit(victimNet, report.Point, report.Impulse, report.Damage);
        }

        /// <summary>
        /// Applies impulse and dispatches <c>OnGunHit</c> on a locally simulated victim.
        /// </summary>
        public static void ApplyHit(PlaneRigidbody victim, in GunHit hit)
        {
            if (!victim)
                return;

            if (victim.SimulationEnabled && hit.Impulse.sqrMagnitude > 1e-8f)
                victim.ApplyImpulseAtPosition(hit.Impulse, hit.Point);

            victim.SendMessage("OnGunHit", hit, SendMessageOptions.DontRequireReceiver);
        }

        private AircraftProjectile RentTracer()
        {
            return projectilePrefab
                ? RentPooledPrefab()
                : AircraftProjectile.RentDefault(tracerColor, tracerWidth);
        }

        private AircraftProjectile RentPooledPrefab()
        {
            for (int i = 0; i < _poolCount; i++)
            {
                AircraftProjectile p = _pool[i];
                if (p && !p.IsInFlight)
                    return p;
            }

            AircraftProjectile spawned = Instantiate(projectilePrefab);
            if (_poolCount >= _pool.Length)
            {
                int next = Mathf.Max(4, _pool.Length * 2);
                System.Array.Resize(ref _pool, next);
            }

            _pool[_poolCount++] = spawned;
            return spawned;
        }

        private bool TryRaycast(Vector3 origin, Vector3 direction, float range, PlaneRigidbody self, out RaycastHit hit)
        {
            int n = Physics.RaycastNonAlloc(origin, direction, _hits, range, hitMask, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < n; i++)
            {
                Collider col = _hits[i].collider;
                if (!col)
                    continue;
                if (self && (col.transform == self.transform || col.transform.IsChildOf(self.transform)))
                    continue;
                if (_hits[i].distance < best)
                {
                    best = _hits[i].distance;
                    bestIndex = i;
                }
            }

            if (bestIndex < 0)
            {
                hit = default;
                return false;
            }

            hit = _hits[bestIndex];
            return true;
        }

        private void GetMuzzleWorld(PlaneRigidbody body, out Vector3 point, out Vector3 axisWorld)
        {
            if (!_muzzlePoseCached)
                CacheMuzzlePose();
            point = body.TransformPoint(_muzzleLocalPos);
            axisWorld = (body.Orientation * _muzzleLocalRot) * localMuzzleAxis.normalized;
        }

        private Vector3 ApplySpread(Vector3 axis)
        {
            if (spreadDeg < 0.001f)
                return axis;

            float yaw = Random.Range(-spreadDeg, spreadDeg);
            float pitch = Random.Range(-spreadDeg, spreadDeg);
            Vector3 right = Vector3.Cross(axis, Vector3.up);
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.Cross(axis, Vector3.forward);
            right.Normalize();
            Vector3 up = Vector3.Cross(right, axis).normalized;
            Quaternion q = Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(pitch, right);
            return q * axis;
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos)
                return;

            Transform mount = Muzzle;
            Vector3 p = mount.position;
            Vector3 axis = mount.TransformDirection(localMuzzleAxis.normalized);
            Gizmos.color = triggerChannel == GunTriggerChannel.Secondary
                ? new Color(1f, 0.35f, 0.1f, 0.95f)
                : new Color(1f, 0.7f, 0.15f, 0.95f);
            Gizmos.DrawLine(p, p + axis * gizmoLength);
            Gizmos.DrawWireSphere(p, 0.06f);
        }
    }
}
