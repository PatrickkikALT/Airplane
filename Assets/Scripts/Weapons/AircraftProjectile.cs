using System.Collections.Generic;
using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.UI;
using UnityEngine;

namespace Airplane.Weapons
{
    [DefaultExecutionOrder(110)]
    [AddComponentMenu("Airplane/Weapons/Aircraft Projectile")]
    public sealed class AircraftProjectile : MonoBehaviour
    {
        [SerializeField] private float maxLifetime = 4f;
        [SerializeField] private Color color = new Color(1f, 0.75f, 0.2f, 1f);
        [SerializeField] private float width = 0.045f;

        private static Material _lineMaterial;
        private static readonly AircraftProjectile[] DefaultPool = new AircraftProjectile[64];
        private static int _defaultPoolCount;

        private LineRenderer _line;
        private AircraftGun _gun;
        private PlaneRigidbody _shooter;
        private Collider[] _shooterIgnored = System.Array.Empty<Collider>();
        private Vector3 _position;
        private Vector3 _prevPosition;
        private Vector3 _velocity;
        private Vector3 _launchOrigin;
        private float _launchFixedTime = -1f;
        private float _age;
        private float _life;
        private float _mass = 0.01f;
        private float _area = 0.000046f;
        private float _cd = 0.3f;
        private bool _ballistic;
        private bool _kinematic;
        private bool _visualOnly;
        private bool _inFlight;
        private NetworkedAircraft _homeOn;
        private readonly RaycastHit[] _hits = new RaycastHit[8];

        public bool IsInFlight => _inFlight;

        public static AircraftProjectile RentDefault(Color tracerColor, float tracerWidth)
        {
            for (int i = 0; i < _defaultPoolCount; i++)
            {
                AircraftProjectile p = DefaultPool[i];
                if (p && !p._inFlight)
                {
                    p.color = tracerColor;
                    p.width = tracerWidth;
                    p.EnsureLine();
                    return p;
                }
            }

            if (_defaultPoolCount >= DefaultPool.Length)
                return null;

            GameObject go = new GameObject("Tracer");
            go.hideFlags = HideFlags.HideInHierarchy;
            AircraftProjectile spawned = go.AddComponent<AircraftProjectile>();
            spawned.color = tracerColor;
            spawned.width = tracerWidth;
            spawned.EnsureLine();
            DefaultPool[_defaultPoolCount++] = spawned;
            return spawned;
        }

        private void Awake()
        {
            EnsureLine();
        }

        private void EnsureLine()
        {
            if (_line)
            {
                ApplyLineStyle();
                return;
            }

            _line = GetComponent<LineRenderer>();
            if (!_line)
                _line = gameObject.AddComponent<LineRenderer>();

            _line.positionCount = 2;
            _line.useWorldSpace = true;
            _line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _line.receiveShadows = false;
            _line.numCapVertices = 2;
            _line.textureMode = LineTextureMode.Stretch;
            ApplyLineStyle();
            gameObject.SetActive(false);
        }

        private void ApplyLineStyle()
        {
            if (!_line)
                return;
            _line.startWidth = width;
            _line.endWidth = width * 0.35f;
            _line.startColor = color;
            _line.endColor = new Color(color.r, color.g, color.b, 0f);
            if (!_line.sharedMaterial)
                _line.sharedMaterial = SharedLineMaterial();
        }

        private static Material SharedLineMaterial()
        {
            if (_lineMaterial)
                return _lineMaterial;

            Shader shader = Shader.Find("Sprites/Default");
            if (!shader)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (!shader)
                shader = Shader.Find("Hidden/Internal-Colored");

            _lineMaterial = shader ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
            _lineMaterial.name = "AircraftTracer";
            if (_lineMaterial.HasProperty("_Color"))
                _lineMaterial.SetColor("_Color", Color.white);
            return _lineMaterial;
        }

        public void LaunchKinematic(Vector3 origin, Vector3 velocity, float lifetime)
        {
            EnsureLine();
            SetShooterIgnore(false);
            _gun = null;
            _shooter = null;
            _ballistic = false;
            _kinematic = true;
            _visualOnly = true;
            _inFlight = true;
            _age = 0f;
            _life = Mathf.Max(0.02f, lifetime);
            _velocity = velocity;
            _launchOrigin = origin;
            _launchFixedTime = Time.fixedTime;
            _prevPosition = origin;
            _position = origin;
            _homeOn = null;
            gameObject.SetActive(true);
            DrawWorldTimeVisual();
        }

        public void LaunchHitscanVisual(Vector3 origin, Vector3 end, float lifetime)
        {
            Vector3 delta = end - origin;
            float dist = delta.magnitude;
            Vector3 velocity = dist > 1e-4f
                ? delta / Mathf.Max(0.02f, lifetime)
                : Vector3.zero;
            LaunchKinematic(origin, velocity, lifetime);
        }

        public void LaunchBallistic(
            AircraftGun gun,
            PlaneRigidbody shooter,
            Vector3 origin,
            Vector3 velocity,
            bool visualOnly)
        {
            EnsureLine();
            SetShooterIgnore(false);
            _gun = gun;
            _shooter = shooter;
            _ballistic = true;
            _kinematic = false;
            _visualOnly = visualOnly;
            _inFlight = true;
            _age = 0f;
            _life = maxLifetime;
            _velocity = velocity;
            _launchOrigin = origin;
            _launchFixedTime = Time.fixedTime;
            _prevPosition = origin;
            _position = origin;
            _homeOn = !visualOnly && CheatFlags.HomingBullets && CheatFlags.AppliesTo(shooter)
                ? PickHomingTarget(shooter, origin, velocity)
                : null;
            gameObject.SetActive(true);
            _shooterIgnored = GetComponentsInChildren<Collider>(true);
            SetShooterIgnore(true);
            DrawWorldTimeVisual();
        }

        private void SetShooterIgnore(bool ignore)
        {
            if (!_shooter || _shooterIgnored == null)
                return;
            for (int i = 0; i < _shooterIgnored.Length; i++)
            {
                Collider col = _shooterIgnored[i];
                if (col)
                    _shooter.IgnoreCollision(col, ignore);
            }

            if (!ignore)
                _shooterIgnored = System.Array.Empty<Collider>();
        }

        public void SetBallistics(float mass, float area, float cd, float lifetime)
        {
            _mass = Mathf.Max(0.0001f, mass);
            _area = Mathf.Max(1e-8f, area);
            _cd = Mathf.Max(0f, cd);
            maxLifetime = Mathf.Max(0.05f, lifetime);
        }

        private void FixedUpdate()
        {
            if (!_inFlight || _kinematic)
                return;

            float dt = Time.fixedDeltaTime;
            if (dt <= 0f)
                return;

            _age += dt;
            if (_age >= _life)
            {
                Stop();
                return;
            }

            StepBallistic(dt);
        }

        private void LateUpdate()
        {
            if (!_inFlight || !_line)
                return;

            if (_kinematic)
            {
                float t = TravelledTime();
                if (t >= _life)
                {
                    Stop();
                    return;
                }

                DrawStreak(_launchOrigin + _velocity * t, t);
                return;
            }

            if (_ballistic)
            {
                float t = TravelledTime();
                Vector3 vis;
                if (_age <= 0f)
                    vis = _launchOrigin + _velocity * t;
                else
                {
                    float alpha = Time.fixedDeltaTime > 0f
                        ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                        : 1f;
                    vis = Vector3.Lerp(_prevPosition, _position, alpha);
                }

                DrawStreak(vis, t);
            }
        }

        /// <summary>
        /// World-time pose from the muzzle. Same clock the airframe interpolates with:
        /// visual ≈ firePose + velocity × (Time.time − fireFixedTime), so inherited airspeed
        /// keeps the round ahead of the interpolated guns instead of sitting on the old muzzle.
        /// The streak only extends back toward the muzzle, never behind it.
        /// </summary>
        private void DrawWorldTimeVisual()
        {
            float t = TravelledTime();
            DrawStreak(_launchOrigin + _velocity * t, t);
        }

        /// <summary>
        /// Seconds since fire on the same clock as PlaneRigidbody interpolation:
        /// completed physics time plus the current render remainder.
        /// </summary>
        private float TravelledTime()
        {
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f)
                return Mathf.Max(0f, Time.time - _launchFixedTime);

            float alpha = Mathf.Clamp01((Time.time - Time.fixedTime) / dt);
            return Mathf.Max(0f, Time.fixedTime - _launchFixedTime + alpha * dt);
        }

        private void DrawStreak(Vector3 vis, float travelledTime)
        {
            Vector3 dir = _velocity;
            float speed = dir.magnitude;
            if (speed > 0.01f)
                dir /= speed;
            else
                dir = Vector3.right;

            Vector3 up = Mathf.Abs(dir.y) > 0.98f ? Vector3.forward : Vector3.up;
            float length = Mathf.Min(4f, speed * travelledTime);
            _line.SetPosition(0, vis - dir * length);
            _line.SetPosition(1, vis);
            transform.SetPositionAndRotation(vis, Quaternion.LookRotation(dir, up));
        }

        private void StepBallistic(float dt)
        {
            AtmosphereSample atmo = AtmosphericModel.SampleAt(_position);
            Vector3 g = AtmosphericModel.SampleGravity();
            Vector3 wind = AtmosphericModel.SampleWind();
            Vector3 vRel = _velocity - wind;
            float speed = FlightSimMath.SafeMagnitude(vRel);

            Vector3 drag = Vector3.zero;
            if (speed > 0.5f && _mass > 1e-6f)
            {
                float mag = 0.5f * atmo.Density * speed * speed * _cd * _area;
                drag = -(vRel / speed) * mag;
            }

            _velocity += (g + drag / _mass) * dt;
            SteerHoming(dt);
            _prevPosition = _position;
            Vector3 next = _position + _velocity * dt;
            Vector3 delta = next - _position;
            float dist = delta.magnitude;
            if (dist > 1e-4f && TrySweep(_position, delta / dist, dist, out RaycastHit hit))
            {
                _position = hit.point;
                if (_gun)
                    _gun.NotifyImpact(_shooter, in hit, _velocity, _visualOnly);
                Stop();
                return;
            }

            _position = next;
        }

        private void SteerHoming(float dt)
        {
            if (!CheatFlags.HomingBullets)
            {
                _homeOn = null;
                return;
            }

            if (!_homeOn || !_homeOn.IsAlive || _homeOn.Body == null)
            {
                _homeOn = _shooter
                    ? PickHomingTarget(_shooter, _position, _velocity)
                    : null;
                if (!_homeOn)
                    return;
            }

            PlaneRigidbody target = _homeOn.Body;
            Vector3 speedVec = _velocity;
            float speed = FlightSimMath.SafeMagnitude(speedVec);
            if (speed < 1f)
                return;

            Vector3 toTarget = target.Position - _position;
            float range = FlightSimMath.SafeMagnitude(toTarget);
            if (range < 0.5f)
                return;

            float eta = range / speed;
            Vector3 aim = target.Position + target.Velocity * eta;
            Vector3 desired = aim - _position;
            float desiredMag = FlightSimMath.SafeMagnitude(desired);
            if (desiredMag < 1e-4f)
                return;

            float maxRad = Mathf.Max(10f, CheatFlags.HomingTurnRateDeg) * Mathf.Deg2Rad * dt;
            Vector3 newDir = Vector3.RotateTowards(speedVec / speed, desired / desiredMag, maxRad, 0f);
            _velocity = newDir * speed;
        }

        private static NetworkedAircraft PickHomingTarget(PlaneRigidbody shooter, Vector3 origin, Vector3 velocity)
        {
            Vector3 dir = velocity;
            float dirMag = FlightSimMath.SafeMagnitude(dir);
            if (dirMag < 1e-4f)
                return null;
            dir /= dirMag;

            NetworkedAircraft best = null;
            float bestScore = float.NegativeInfinity;
            IReadOnlyList<NetworkedAircraft> all = NetworkedAircraft.All;
            for (int i = 0; i < all.Count; i++)
            {
                NetworkedAircraft other = all[i];
                if (!other || !other.IsSpawned || !other.IsAlive || other.Body == null)
                    continue;
                if (other.Body == shooter)
                    continue;

                Vector3 to = other.Body.Position - origin;
                float dist = FlightSimMath.SafeMagnitude(to);
                if (dist < 8f || dist > 4000f)
                    continue;

                float align = Vector3.Dot(dir, to / dist);
                if (align < 0f)
                    continue;

                float score = align * 2.5f - dist / 4000f;
                if (score > bestScore)
                {
                    bestScore = score;
                    best = other;
                }
            }

            return best;
        }

        private bool TrySweep(Vector3 origin, Vector3 direction, float range, out RaycastHit hit)
        {
            int n = Physics.RaycastNonAlloc(origin, direction, _hits, range, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < n; i++)
            {
                Collider col = _hits[i].collider;
                if (!col)
                    continue;
                if (_shooter && (col.transform == _shooter.transform || col.transform.IsChildOf(_shooter.transform)))
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

        private void Stop()
        {
            _inFlight = false;
            _kinematic = false;
            _ballistic = false;
            _launchFixedTime = -1f;
            _homeOn = null;
            SetShooterIgnore(false);
            _gun = null;
            _shooter = null;
            if (_line)
            {
                _line.SetPosition(0, Vector3.zero);
                _line.SetPosition(1, Vector3.zero);
            }

            gameObject.SetActive(false);
        }
    }
}
