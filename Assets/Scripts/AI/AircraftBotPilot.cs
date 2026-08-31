using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.Weapons;
using UnityEngine;

namespace Airplane.AI
{
    /// <summary>
    /// A computer pilot flying a stock aircraft.
    ///
    /// It has no privileges over a human. It reads its own airframe, it reads <see cref="BotVision"/>
    /// for anything outside the cockpit, and it writes the same seven control channels a player's
    /// stick writes through <see cref="AircraftFlightController.ApplyExternalControls"/> and
    /// <see cref="AircraftWeaponsController.ApplyExternalFire"/>. It cannot see through terrain, it
    /// cannot see behind its own tail, it takes time to notice a contact and it shoots at where it
    /// believes the target is.
    ///
    /// Lives only on the peer that simulates the aircraft, which for a bot is the server. It is
    /// added at runtime by <see cref="AircraftNetworkSpawner"/> rather than living on the prefab, so
    /// a client's copy of the same aircraft is a plain replay proxy with no brain attached.
    ///
    /// Runs ahead of <see cref="PlaneRigidbody"/> (order 100) so the deflections it writes are the
    /// ones the solver integrates this tick.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-60)]
    [RequireComponent(typeof(PlaneRigidbody))]
    [AddComponentMenu("Airplane/AI/Aircraft Bot Pilot")]
    public sealed class AircraftBotPilot : MonoBehaviour
    {
        [Header("Pilot")]
        [SerializeField] private BotSkillProfile profile = BotSkillProfile.FromSkill(0.5f);

        [Header("Patrol")]
        [Tooltip("Centre of the area this pilot loiters in while nothing is in sight.")]
        [SerializeField] private Vector3 patrolCentre = new Vector3(0f, 700f, 0f);

        [SerializeField] private float patrolRadius = 2500f;
        [SerializeField] private float patrolMinAltitude = 400f;
        [SerializeField] private float patrolMaxAltitude = 1200f;

        [Header("Engagement")]
        [Tooltip("Furthest a contact is worth turning towards, metres.")]
        [SerializeField] private float engageRange = 3500f;

        [Tooltip("Range at which the pilot breaks off rather than risk a collision, metres.")]
        [SerializeField] private float minEngageRange = 110f;

        [Header("Senses")]
        [Tooltip("Seconds between perception updates. Part of the reaction lag, not just a budget knob.")]
        [SerializeField] private float perceptionInterval = 0.2f;

        [Tooltip("Layers that block sight and count as terrain for recovery.")]
        [SerializeField] private LayerMask worldMask = ~0;

        [Tooltip("Seconds of flight path checked ahead for terrain.")]
        [SerializeField] private float terrainLookaheadSeconds = 7f;

        private static readonly Vector3 BodyForward = new Vector3(1f, 0f, 0f);

        private readonly BotVision _vision = new BotVision();
        private readonly BotAutopilot _autopilot = new BotAutopilot();
        private readonly RaycastHit[] _probeHits = new RaycastHit[8];

        private PlaneRigidbody _body;
        private AircraftFlightController _controller;
        private AircraftWeaponsController _weapons;
        private AircraftVitality _vitality;
        private NetworkedAircraft _networked;

        private BotState _state = BotState.Patrol;
        private BotContact _target;
        private float _seed;
        private float _perceptionClock;
        private float _terrainClock;
        private float _altitudeAgl = 9999f;
        private float _timeToTerrain = 999f;
        private Vector3 _waypoint;
        private bool _hasWaypoint;
        private float _burstClock;
        private bool _triggerHeld;
        private float _lastDamageTime = -999f;
        private Vector3 _lastThreatDirection = Vector3.forward;
        private float _evadeUntil;
        private float _evadeSide = 1f;
        private BotEvasion _evasion;
        private float _extendUntil;
        private float _breakSide = 1f;

        public BotState State => _state;

        public BotSkillProfile Profile => profile;

        /// <summary>The aircraft this pilot is currently working on, or null.</summary>
        public NetworkedAircraft Target => _target != null ? _target.Aircraft : null;

        private void Awake()
        {
            _body = GetComponent<PlaneRigidbody>();
            _controller = GetComponent<AircraftFlightController>();
            _weapons = GetComponent<AircraftWeaponsController>();
            _vitality = GetComponent<AircraftVitality>();
            _networked = GetComponent<NetworkedAircraft>();
            _seed = Random.Range(0f, 500f);
        }

        /// <summary>
        /// Hands the pilot its competence and its patch of sky. Called by the spawner immediately
        /// after the component is added, before the aircraft is network-spawned.
        /// </summary>
        public void Initialize(BotSkillProfile skill, Vector3 area, float radius, float minAltitude, float maxAltitude)
        {
            if (skill != null)
                profile = skill;

            patrolCentre = area;
            patrolRadius = Mathf.Max(300f, radius);
            patrolMinAltitude = Mathf.Min(minAltitude, maxAltitude);
            patrolMaxAltitude = Mathf.Max(minAltitude, maxAltitude);

            _seed = Random.Range(0f, 500f);
            _perceptionClock = Random.Range(0f, perceptionInterval);
            _hasWaypoint = false;
            _vision.Clear();
            _autopilot.Reset();
        }

        private void FixedUpdate()
        {
            if (_body == null || !_body.SimulationEnabled)
                return;

            float dt = Time.fixedDeltaTime;

            _perceptionClock += dt;
            if (_perceptionClock >= perceptionInterval)
            {
                _vision.Tick(_networked, _body, profile, worldMask, _perceptionClock);
                _perceptionClock = 0f;
                SelectTarget();
            }

            _terrainClock += dt;
            if (_terrainClock >= 0.1f)
            {
                SampleSurroundings();
                _terrainClock = 0f;
            }

            UpdateState();

            BotFlightCommand command = BuildCommand(out bool wantsToShoot);
            command.Direction = ApplySeparation(command.Direction);

            if (_controller && _state == BotState.Patrol)
                _controller.TickAutoTrim(dt);

            float trim = _controller ? _controller.ElevatorTrim : -0.1f;
            BotControlOutput output = _autopilot.Tick(_body, in command, profile, trim, dt);

            if (_controller)
            {
                _controller.ApplyExternalControls(
                    output.Aileron,
                    output.Elevator,
                    output.Rudder,
                    output.Throttle,
                    output.Flaps,
                    output.Airbrake,
                    0f);
            }

            if (_weapons)
                _weapons.ApplyExternalFire(UpdateTrigger(wantsToShoot, dt), 0f);
        }

        /// <summary>
        /// Dispatched by <see cref="AircraftGun.ApplyHit"/> on a locally simulated victim. Taking
        /// rounds is the one way a pilot learns about an attacker outside the search cone; it is also
        /// what starts the defensive break that makes bounces survivable.
        /// </summary>
        private void OnGunHit(GunHit hit)
        {
            _lastDamageTime = Time.time;

            if (hit.Impulse.sqrMagnitude > 1e-6f)
                _lastThreatDirection = -hit.Impulse.normalized;

            // Shooter is null when the hit arrived over the wire from another peer's solver: the
            // pilot feels the rounds without learning who fired them, which is the honest outcome.
            if (hit.Shooter == null)
                return;

            NetworkedAircraft shooter = hit.Shooter.GetComponent<NetworkedAircraft>();
            if (!shooter)
                return;

            _vision.NotifyIncomingFire(shooter, hit.Shooter.Position, hit.Shooter.Velocity, 0.8f);
            _lastThreatDirection = (hit.Shooter.Position - _body.Position).normalized;
        }

        private void SelectTarget()
        {
            BotContact best = null;
            float bestScore = float.MaxValue;

            for (int i = 0; i < _vision.Contacts.Count; i++)
            {
                BotContact contact = _vision.Contacts[i];
                if (contact.Aircraft == null || !contact.Acquired)
                    continue;
                if (contact.TimeSinceSeen > profile.memorySeconds)
                    continue;

                Vector3 toTarget = contact.EstimatedPosition - _body.Position;
                float distance = FlightSimMath.SafeMagnitude(toTarget);
                if (distance > engageRange)
                    continue;

                float angleOff = Vector3.Angle(_body.TransformDirection(BodyForward), toTarget);

                // Closer and more nearly in front wins. The per-contact seed breaks ties differently
                // for every pilot, so a flight of bots spreads across the targets on offer instead of
                // all converging on whoever happens to be nearest the middle of the map.
                float score = distance + angleOff * 12f + (contact.ErrorSeed % 7f) * 45f;

                if (contact.WasShotBy && Time.time - contact.LastFiredAtMeTime < 8f)
                    score -= 900f;
                if (contact == _target)
                    score -= 500f;

                if (score < bestScore)
                {
                    bestScore = score;
                    best = contact;
                }
            }

            _target = best;
        }

        private void UpdateState()
        {
            BotState next = ResolveState();

            // A manoeuvre that has run its course gets re-picked even if the state has not changed,
            // otherwise a pilot who stays under fire flies the same break turn until it dies.
            if (next == BotState.Evade && (_state != BotState.Evade || Time.time >= _evadeUntil))
                BeginEvasion();

            _state = next;
        }

        private BotState ResolveState()
        {
            if (NeedsRecovery())
                return BotState.Recover;

            if (_state == BotState.Evade && Time.time < _evadeUntil)
                return BotState.Evade;

            if (ShouldEvade())
                return BotState.Evade;

            if (_target == null)
                return BotState.Patrol;

            Vector3 toTarget = _target.EstimatedPosition - _body.Position;
            float distance = FlightSimMath.SafeMagnitude(toTarget);
            float angleOff = Vector3.Angle(_body.TransformDirection(BodyForward), toTarget);

            bool inFiringGeometry = _target.Visible
                                    && distance < profile.gunRange * 1.5f
                                    && angleOff < 55f
                                    && Time.time > _extendUntil;

            return inFiringGeometry ? BotState.Attack : BotState.Pursue;
        }

        private bool NeedsRecovery()
        {
            if (_altitudeAgl < profile.minSafeAltitude || _timeToTerrain < 4f)
                return true;

            if (_state != BotState.Recover)
                return false;

            // Hysteresis: climb clear before handing control back to the fight, or a bot at the
            // recovery boundary oscillates between pulling up and diving at the same patch of ground.
            return _altitudeAgl < profile.minSafeAltitude * 1.8f || _timeToTerrain < 7f;
        }

        private bool ShouldEvade()
        {
            float sinceHit = Time.time - _lastDamageTime;

            bool hurt = _vitality && _vitality.MaxHitPoints > 0f
                        && _vitality.HitPoints / _vitality.MaxHitPoints < profile.evadeHealthFraction;

            // A shot-up aircraft breaks off rather than trading with whoever is still shooting.
            if (hurt && sinceHit < 6f)
                return true;

            if (sinceHit > 1.2f)
                return false;

            // Rounds arriving from outside the forward hemisphere mean somebody is on the six, and
            // pressing on with the current attack is how a pilot dies.
            float angleToThreat = Vector3.Angle(_body.TransformDirection(BodyForward), _lastThreatDirection);
            return angleToThreat > 55f;
        }

        private void BeginEvasion()
        {
            _evadeUntil = Time.time + profile.evadeSeconds;
            _evadeSide = Random.value < 0.5f ? -1f : 1f;

            bool roomBelow = _altitudeAgl > 900f;
            bool roomAbove = _body.Position.y < CombatCeiling - 80f;
            bool fast = _body.TrueAirspeed > profile.cruiseSpeed;

            if (roomBelow && fast && Random.value < 0.4f)
                _evasion = BotEvasion.SplitS;
            else if (!roomBelow && roomAbove && Random.value < 0.35f)
                _evasion = BotEvasion.ClimbingSpiral;
            else
                _evasion = BotEvasion.BreakTurn;

            _triggerHeld = false;
        }

        private BotFlightCommand BuildCommand(out bool wantsToShoot)
        {
            wantsToShoot = false;

            switch (_state)
            {
                case BotState.Recover:
                    return BuildRecoveryCommand();
                case BotState.Evade:
                    return BuildEvasionCommand();
                case BotState.Attack:
                    return BuildAttackCommand(out wantsToShoot);
                case BotState.Pursue:
                    return BuildPursuitCommand();
                default:
                    return BuildPatrolCommand();
            }
        }

        private BotFlightCommand BuildRecoveryCommand()
        {
            Vector3 heading = Flatten(_body.TransformDirection(BodyForward));
            if (heading.sqrMagnitude < 1e-4f)
                heading = Flatten(_body.Velocity);
            if (heading.sqrMagnitude < 1e-4f)
                heading = Vector3.forward;
            heading.Normalize();

            // Steeper the closer the ground is, but never so steep that the wing gives up. Wings
            // level first: pulling while banked just turns the dive.
            float urgency = Mathf.Clamp01(1f - _altitudeAgl / Mathf.Max(1f, profile.minSafeAltitude * 2f));
            float climb = Mathf.Lerp(0.25f, 0.8f, urgency);

            return new BotFlightCommand
            {
                Direction = (heading + Vector3.up * climb).normalized,
                Speed = profile.combatSpeed,
                MaxBankDeg = 25f,
                HoldWingsLevel = true,
                AllowAirbrake = false
            };
        }

        private BotFlightCommand BuildEvasionCommand()
        {
            Vector3 threat = _lastThreatDirection.sqrMagnitude > 1e-4f
                ? _lastThreatDirection.normalized
                : _body.TransformDirection(BodyForward);

            Vector3 direction;
            float maxBank = profile.maxBankDeg;

            switch (_evasion)
            {
                case BotEvasion.SplitS:
                    // Roll and pull down through the vertical: trades altitude for a reversal the
                    // attacker cannot follow without overshooting.
                    return new BotFlightCommand
                    {
                        Direction = (Flatten(-threat).normalized * 0.4f + Vector3.down).normalized,
                        Speed = profile.combatSpeed * 1.1f,
                        MaxBankDeg = maxBank,
                        HoldWingsLevel = false,
                        AllowAirbrake = false
                    };

                case BotEvasion.ClimbingSpiral:
                    direction = Vector3.Cross(Vector3.up, Flatten(threat).normalized) * _evadeSide;
                    return LevelCommand(
                        direction,
                        profile.combatSpeed * 1.1f,
                        maxBank,
                        Mathf.Min(_body.Position.y + 180f, CombatCeiling),
                        18f);

                default:
                    direction = Vector3.Cross(Vector3.up, Flatten(threat).normalized) * _evadeSide;
                    return LevelCommand(direction, profile.combatSpeed * 1.1f, maxBank, CruiseAltitude, 10f);
            }
        }

        private BotFlightCommand BuildAttackCommand(out bool wantsToShoot)
        {
            wantsToShoot = false;

            Vector3 muzzle = _body.Position;
            Vector3 shotAxis = _body.TransformDirection(BodyForward);
            float muzzleSpeed = 800f;
            bool leadMotion = true;
            ResolveGunGeometry(ref muzzle, ref shotAxis, ref muzzleSpeed, ref leadMotion);

            Vector3 targetPosition = _target.EstimatedPosition;
            Vector3 targetVelocity = _target.EstimatedVelocity;
            Vector3 toTarget = targetPosition - _body.Position;
            float distance = FlightSimMath.SafeMagnitude(toTarget);

            // Closing inside gun range with a big overtake ends in a collision or a wild overshoot.
            // Break off, reset the geometry, come back around.
            float closure = Vector3.Dot(_body.Velocity - targetVelocity, toTarget.normalized);
            if (distance < minEngageRange && closure > 10f)
            {
                if (Time.time >= _extendUntil)
                {
                    _extendUntil = Time.time + 2.5f;
                    _breakSide = Random.value < 0.5f ? -1f : 1f;
                }

                return BuildBreakoffCommand(toTarget);
            }

            Vector3 desiredNose = toTarget.normalized;

            if (BotGunSolution.TrySolve(
                    muzzle,
                    _body.Velocity,
                    targetPosition,
                    targetVelocity,
                    muzzleSpeed,
                    leadMotion,
                    out Vector3 boresight,
                    out float _))
            {
                boresight = BotGunSolution.ApplyAimError(boresight, profile.aimErrorDeg, _seed);

                // Steer whatever the guns are bore-sighted along onto the solution, not the nose:
                // the mounts do not have to be aligned with the fuselage axis.
                Quaternion correction = Quaternion.FromToRotation(shotAxis, boresight);
                desiredNose = correction * _body.TransformDirection(BodyForward);

                float error = Vector3.Angle(shotAxis, boresight);
                wantsToShoot = error < profile.firingConeDeg
                               && distance < profile.gunRange
                               && distance > minEngageRange * 0.6f
                               && _target.Visible;
            }

            // Slow down behind a target rather than sliding out in front of it.
            float speed = distance < profile.gunRange * 0.5f && closure > 25f
                ? Mathf.Max(profile.cruiseSpeed * 0.85f, FlightSimMath.SafeMagnitude(targetVelocity) * 1.05f)
                : profile.combatSpeed;

            // Inside gun range, point at the solution even if it means a dive or a climb — that is
            // the shot. Outside it, do not zoom-climb after a target that is already above the band.
            bool closeEnoughToAim = distance < profile.gunRange * 1.25f
                                    && Mathf.Abs(targetPosition.y - _body.Position.y) < 350f;

            if (closeEnoughToAim)
            {
                return new BotFlightCommand
                {
                    Direction = desiredNose,
                    Speed = speed,
                    MaxBankDeg = profile.maxBankDeg,
                    HoldWingsLevel = false,
                    AllowAirbrake = true
                };
            }

            return LevelCommand(desiredNose, speed, profile.maxBankDeg, ClampAimAltitude(targetPosition.y), 10f);
        }

        private BotFlightCommand BuildBreakoffCommand(Vector3 toTarget)
        {
            // The side is chosen once, when the break-off starts. Re-rolling it per tick would leave
            // the aircraft shaking its nose instead of separating.
            Vector3 away = Vector3.Cross(Vector3.up, Flatten(toTarget).normalized) * _breakSide;
            return LevelCommand(away, profile.combatSpeed, profile.maxBankDeg, CruiseAltitude, 8f);
        }

        private BotFlightCommand BuildPursuitCommand()
        {
            Vector3 toTarget = _target.EstimatedPosition - _body.Position;
            float distance = FlightSimMath.SafeMagnitude(toTarget);

            // Still separating after an overshoot: turning straight back in would put the two
            // aircraft nose to nose at closing speed.
            if (Time.time < _extendUntil)
                return BuildBreakoffCommand(toTarget);

            // Lead the belief, not the target. When the contact is stale this walks the aim point
            // along a track that may already be wrong, which is exactly the intended failure mode.
            float lead = Mathf.Clamp(distance / Mathf.Max(60f, profile.combatSpeed), 0f, 4f);
            Vector3 aimPoint = _target.EstimatedPosition + _target.EstimatedVelocity * lead;
            aimPoint.y = ClampAimAltitude(aimPoint.y);

            return LevelCommand(
                aimPoint - _body.Position,
                profile.combatSpeed,
                profile.maxBankDeg,
                aimPoint.y,
                10f);
        }

        private BotFlightCommand BuildPatrolCommand()
        {
            if (!_hasWaypoint || (_body.Position - _waypoint).sqrMagnitude < 300f * 300f)
                PickWaypoint();

            Vector3 toWaypoint = _waypoint - _body.Position;

            // A slow weave keeps the search cone sweeping instead of staring down one bearing for
            // the whole leg.
            float weave = (Mathf.PerlinNoise(Time.time * 0.06f + _seed, 0.5f) * 2f - 1f) * 0.28f;
            Vector3 lateral = Vector3.Cross(Vector3.up, Flatten(toWaypoint).normalized) * weave;

            return LevelCommand(
                Flatten(toWaypoint) + lateral,
                profile.cruiseSpeed,
                45f,
                _waypoint.y,
                8f);
        }

        private void PickWaypoint()
        {
            Vector2 disc = Random.insideUnitCircle * patrolRadius;
            // Stay near the current height so patrol is a cruise, not a climb to a random ceiling.
            float alt = Mathf.Clamp(_body ? _body.Position.y : patrolCentre.y, CombatFloor, CombatCeiling);
            alt += Random.Range(-80f, 80f);
            _waypoint = new Vector3(
                patrolCentre.x + disc.x,
                ClampAimAltitude(alt),
                patrolCentre.z + disc.y);
            _hasWaypoint = true;
        }

        private float CombatFloor => Mathf.Max(patrolMinAltitude, profile.minSafeAltitude + 80f);

        private float CombatCeiling => Mathf.Max(CombatFloor + 100f, patrolMaxAltitude);

        /// <summary>Hold current height if it is already in the band; otherwise drive back into it.</summary>
        private float CruiseAltitude
        {
            get
            {
                float y = _body ? _body.Position.y : patrolCentre.y;
                return Mathf.Clamp(y, CombatFloor, CombatCeiling);
            }
        }

        private float ClampAimAltitude(float y)
        {
            return Mathf.Clamp(y, CombatFloor, CombatCeiling);
        }

        private BotFlightCommand LevelCommand(
            Vector3 direction,
            float speed,
            float maxBankDeg,
            float targetAltitude,
            float maxFlightPathDeg)
        {
            return new BotFlightCommand
            {
                Direction = direction,
                Speed = speed,
                MaxBankDeg = maxBankDeg,
                HoldWingsLevel = false,
                AllowAirbrake = false,
                HoldAltitude = true,
                TargetAltitude = targetAltitude,
                MaxFlightPathDeg = maxFlightPathDeg
            };
        }

        /// <summary>
        /// See-and-avoid at knife range. Only applies to aircraft the pilot is not attacking: inside
        /// a fight the guns have to point at somebody, and real pilots accept that risk.
        /// </summary>
        private Vector3 ApplySeparation(Vector3 direction)
        {
            const float radius = 170f;
            const float targetOverrideRadius = 60f;

            Vector3 position = _body.Position;
            Vector3 avoidance = Vector3.zero;

            var all = NetworkedAircraft.All;
            for (int i = 0; i < all.Count; i++)
            {
                NetworkedAircraft other = all[i];
                if (!other || other == _networked || !other.IsAlive || other.Body == null)
                    continue;

                Vector3 offset = other.Body.Position - position;
                float distance = FlightSimMath.SafeMagnitude(offset);
                if (distance > radius || distance < 0.1f)
                    continue;

                // The aircraft being attacked is not avoided until it is close enough that the
                // fight would end in a ramming rather than a kill.
                bool isTarget = _target != null && other == _target.Aircraft;
                if (isTarget && distance > targetOverrideRadius)
                    continue;

                float closure = Vector3.Dot(_body.Velocity - other.Body.Velocity, offset / distance);
                if (closure <= 0f)
                    continue;

                float weight = (1f - distance / radius) * Mathf.Clamp01(closure / 60f);
                avoidance -= offset.normalized * weight;
            }

            if (avoidance.sqrMagnitude < 1e-6f)
                return direction;

            return (direction.normalized + avoidance * 1.5f).normalized;
        }

        /// <summary>
        /// Burst discipline. Holding the trigger down forever is both unrealistic and a fast way to
        /// run a magazine dry, so the trigger is chopped into bursts with gaps.
        /// </summary>
        private float UpdateTrigger(bool wantsToShoot, float dt)
        {
            if (!wantsToShoot)
            {
                _triggerHeld = false;
                _burstClock = Mathf.Max(0f, _burstClock - dt);
                return 0f;
            }

            _burstClock += dt;

            if (_triggerHeld)
            {
                if (_burstClock >= profile.burstSeconds)
                {
                    _triggerHeld = false;
                    _burstClock = 0f;
                }
            }
            else if (_burstClock >= profile.burstGapSeconds)
            {
                _triggerHeld = true;
                _burstClock = 0f;
            }

            return _triggerHeld ? 1f : 0f;
        }

        private void ResolveGunGeometry(ref Vector3 muzzle, ref Vector3 axis, ref float muzzleSpeed, ref bool leadMotion)
        {
            if (_weapons == null)
                return;

            AircraftGun[] guns = _weapons.Guns;
            if (guns == null)
                return;

            Vector3 positionSum = Vector3.zero;
            Vector3 axisSum = Vector3.zero;
            float speedSum = 0f;
            int count = 0;
            bool anyBallistic = false;

            for (int i = 0; i < guns.Length; i++)
            {
                AircraftGun gun = guns[i];
                if (!gun || !gun.isActiveAndEnabled || gun.TriggerChannel != GunTriggerChannel.Primary)
                    continue;
                if (gun.AmmoRemaining == 0)
                    continue;

                positionSum += gun.MuzzlePosition;
                axisSum += gun.ShotAxisWorld;
                speedSum += gun.MuzzleSpeed;
                anyBallistic |= gun.FireMode == GunFireMode.Projectile;
                count++;
            }

            if (count == 0 || axisSum.sqrMagnitude < 1e-6f)
                return;

            muzzle = positionSum / count;
            axis = axisSum.normalized;
            muzzleSpeed = speedSum / count;

            // A hitscan round arrives the instant it is fired, so leading the target's motion would
            // put the burst ahead of it. The shooter's own velocity still skews the path and is
            // handled inside the solver either way.
            leadMotion = anyBallistic;
        }

        private void SampleSurroundings()
        {
            Vector3 position = _body.Position;

            _altitudeAgl = Probe(position, Vector3.down, 6000f, out float groundDistance)
                ? groundDistance
                : 9999f;

            _timeToTerrain = 999f;
            Vector3 velocity = _body.Velocity;
            float speed = FlightSimMath.SafeMagnitude(velocity);
            if (speed < 5f)
                return;

            float reach = speed * Mathf.Max(1f, terrainLookaheadSeconds);
            if (Probe(position, velocity / speed, reach, out float pathDistance))
                _timeToTerrain = pathDistance / speed;
        }

        private bool Probe(Vector3 origin, Vector3 direction, float distance, out float hitDistance)
        {
            hitDistance = distance;
            int n = Physics.RaycastNonAlloc(origin, direction, _probeHits, distance, worldMask, QueryTriggerInteraction.Ignore);

            bool found = false;
            float best = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                Collider col = _probeHits[i].collider;
                if (!col)
                    continue;

                Transform t = col.transform;
                if (t == transform || t.IsChildOf(transform))
                    continue;

                // Another aircraft is not terrain. Separation handles those.
                if (col.GetComponentInParent<PlaneRigidbody>() != null)
                    continue;

                if (_probeHits[i].distance < best)
                {
                    best = _probeHits[i].distance;
                    found = true;
                }
            }

            if (found)
                hitDistance = best;
            return found;
        }

        private static Vector3 Flatten(Vector3 v)
        {
            v.y = 0f;
            return v;
        }
    }

    public enum BotState
    {
        Patrol = 0,
        Pursue = 1,
        Attack = 2,
        Evade = 3,
        Recover = 4
    }

    internal enum BotEvasion
    {
        BreakTurn = 0,
        SplitS = 1,
        ClimbingSpiral = 2
    }
}
