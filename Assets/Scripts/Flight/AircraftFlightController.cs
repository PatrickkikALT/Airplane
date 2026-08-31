using UnityEngine;
using UnityEngine.InputSystem;
using Airplane.UI;
using System.Text;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Converts PlayerInput stick axes into rate-limited, dynamic-pressure-scaled control
    /// deflections and engine throttle.
    ///
    /// PlayerInput Unity Events still feed the On* methods, but stick axes are re-read from
    /// device/action state each physics tick. Value-action <c>canceled</c> callbacks drop the
    /// opposite WASD key when pitch is held, so latching those events leaves roll/yaw stuck.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlaneRigidbody))]
    [AddComponentMenu("Airplane/Aircraft Flight Controller")]
    public sealed class AircraftFlightController : MonoBehaviour
    {
        [Tooltip("Stick-to-aileron gain. 1 = full deflection at full stick. Values like 0.2 make the tails overpower you.")]
        [SerializeField] private float rollSpeed = 1f;

        [Tooltip("Stick-to-rudder gain. 1 = full deflection at full stick.")]
        [SerializeField] private float yawSpeed = 1f;

        [Header("Stick")]
        [Tooltip("If true, positive pitch input (S / stick back) commands nose UP.")]
        [SerializeField] private bool pitchStickBackNoseUp = true;

        [Tooltip("If true, invert the pitch axis. PlaneInput maps W = negative, S = positive.")]
        [SerializeField] private bool invertPitch;

        [Tooltip("If true, invert roll. PlaneInput maps A = negative, D = positive = roll right.")]
        [SerializeField] private bool invertRoll;

        [Tooltip("If true, invert yaw. PlaneInput maps Q = negative, E = positive = yaw right.")]
        [SerializeField] private bool invertYaw;

        [Header("Throttle")]
        [Tooltip("Rate: R/F (or a 1D axis) increment the lever. Absolute: the axis IS the lever (0–1 gamepad trigger).")]
        [SerializeField] private bool throttleIsRate = true;

        [Tooltip("Throttle lever rate, 1/s, when Throttle Is Rate is set. 0.4 ≈ 2.5 s idle-to-full.")]
        [SerializeField] private float throttleRate = 0.45f;

        [Tooltip("Throttle at Start(), 0–1.")]
        [SerializeField] [Range(0f, 1f)] private float initialThrottle = 0.65f;

        [Header("Flaps / Speedbrakes")]
        [Tooltip("If true, the flaps axis is a rate (hold to deploy/retract). If false, the axis is an analog 0–1 position.")]
        [SerializeField] private bool flapsIsRate = true;

        [SerializeField] private float flapsRate = 0.5f;
        [SerializeField] private float airbrakeRate = 1.2f;

        [Header("High-speed stiffening")]
        [Tooltip("Dynamic pressure (Pa) at which full mechanical deflection is still available. ~½ ρ V² at manoeuvre speed. 80 m/s ISA ≈ 3920 Pa.")]
        [SerializeField] private float referenceDynamicPressure = 3900f;

        [Tooltip("Stiffening gain k in  limit = 1 / (1 + k · max(0, q/q_ref − 1)). Keep this low; high values let the tails overpower the stick.")]
        [SerializeField] private float qStiffeningGain = 0.25f;

        [Tooltip("Seconds for aileron/elevator/rudder to catch the stick. 0 = immediate. The old hinge limiter made press and release feel late.")]
        [SerializeField] private float stickFollowSeconds;

        [Header("Turn coordination")]
        [Tooltip("Rudder mixed in with aileron so A/D rolls around the nose instead of skidding. 1 = full rudder at full stick, which is what holding Q/E with A/D used to do by hand. 0 = raw aero (adverse yaw).")]
        [SerializeField] [Range(0f, 1.5f)] private float aileronRudderMix = 1f;

        [Tooltip("Extra rudder per radian of sideslip, airborne only. Washes out leftover drift after the mix. Player yaw overrides this.")]
        [SerializeField] private float sideslipYawGain = 1.2f;

        [Header("Trim")]
        [Tooltip("Hands-off elevator offset, −1..1. Written by auto-trim; positive = nose up.")]
        [SerializeField] [Range(-1f, 1f)] private float elevatorTrim;

        [Tooltip("If true, elevator trim tracks load factor so stick-neutral holds level (1 / cos φ G).")]
        [SerializeField] private bool autoElevatorTrim = true;

        [Tooltip("Trim units per second per G of error. ~0.4 finds cruise without fighting the stick.")]
        [SerializeField] private float autoTrimRate = 0.4f;

        [Tooltip("Do not auto-trim while |pitch stick| is above this.")]
        [SerializeField] [Range(0f, 0.5f)] private float autoTrimStickDeadzone = 0.08f;

        [Header("Debug HUD")]
        [SerializeField] private bool drawHud = true;
        [SerializeField] private Vector2 hudPosition = new Vector2(16f, 16f);

        private PlaneRigidbody _body;
        private AircraftEngine _engine;

        private float _throttleRaw;
        private float _flapsRaw;
        private float _airbrakesRaw;
        private float _wheelBrakesRaw;
        private float _throttle01;
        private float _flaps01;
        private float _airbrake01;
        private float _aileron01;
        private float _elevator01;
        private float _rudder01;
        private float _wheelBrake01;
        private float _rawPitch;
        private float _rawRoll;
        private float _rawYaw;
        private bool _inputEnabled = true;
        private PlayerInput _playerInput;
        private InputAction _pitchAction;
        private InputAction _rollAction;
        private InputAction _yawAction;
        private readonly StringBuilder _hudBuilder = new StringBuilder(512);
        private string _hudText = "";
        private float _hudClock;

        public AircraftEngine Engine => _engine;
        public float Throttle01 => _throttle01;
        public float Flaps01 => _flaps01;
        public float Airbrake01 => _airbrake01;
        public float WheelBrake01 => _wheelBrake01;
        public float Aileron01 => _aileron01;
        public float Elevator01 => _elevator01;
        public float Rudder01 => _rudder01;
        public float RawYaw => invertYaw ? -_rawYaw : _rawYaw;
        public float RawPitch => invertPitch ? -_rawPitch : _rawPitch;
        public float RawRoll => invertRoll ? -_rawRoll : _rawRoll;

        /// <summary>
        /// Hands-off elevator offset, −1..1. Positive = nose up. Bots have to add this themselves
        /// because <see cref="ApplyExternalControls"/> writes the surface, not the stick, and this
        /// airframe climbs with the stick at zero (the prefab trims it to about −0.1).
        /// </summary>
        public float ElevatorTrim => elevatorTrim;

        /// <summary>False when the lever and surface positions are being written by something else.</summary>
        public bool InputEnabled => _inputEnabled;

        public void SetInitialThrottle(float value)
        {
            initialThrottle = FlightSimMath.Saturate(value);
            _throttle01 = initialThrottle;
        }

        /// <summary>
        /// Disables the input path so <see cref="ApplyExternalControls"/> becomes the only writer.
        /// Used for aircraft flown by a remote peer, where the deflections arrive over the wire.
        /// </summary>
        public void SetInputEnabled(bool enable)
        {
            _inputEnabled = enable;
        }

        public void SetHudVisible(bool visible)
        {
            drawHud = visible;
        }

        /// <summary>
        /// Runs the 1G / 1-over-cosine-bank auto-trim as if the stick were released. Used by bot
        /// pilots: their input path is off, so <see cref="PrePhysicsTick"/> never gets here, but they
        /// still need the same hands-off elevator the player trimmed to −0.1 for.
        /// </summary>
        public void TickAutoTrim(float dt)
        {
            UpdateAutoTrim(0f, dt);
        }

        /// <summary>
        /// Writes lever and surface positions from an outside source. No rate limiting is applied:
        /// the values already went through the hinge model on the machine that owns the aircraft, and
        /// limiting them twice would lag the visible surfaces behind the replicated attitude.
        /// </summary>
        public void ApplyExternalControls(
            float aileron,
            float elevator,
            float rudder,
            float throttle,
            float flaps,
            float airbrake,
            float wheelBrake)
        {
            _aileron01 = Clamp11(aileron);
            _elevator01 = Clamp11(elevator);
            _rudder01 = Clamp11(rudder);
            _throttle01 = FlightSimMath.Saturate(throttle);
            _flaps01 = FlightSimMath.Saturate(flaps);
            _airbrake01 = FlightSimMath.Saturate(airbrake);
            _wheelBrake01 = FlightSimMath.Saturate(wheelBrake);
        }

        public void OnPitch(InputAction.CallbackContext context)
        {
            if (_inputEnabled)
                _rawPitch = ReadAxis(context);
        }

        public void OnRoll(InputAction.CallbackContext context)
        {
            if (_inputEnabled)
                _rawRoll = ReadAxis(context);
        }

        public void OnYaw(InputAction.CallbackContext context)
        {
            if (_inputEnabled)
                _rawYaw = ReadAxis(context);
        }

        public void OnThrottle(InputAction.CallbackContext context)
        {
            if (_inputEnabled)
                _throttleRaw = ReadAxis(context);
        }

        public void OnFlaps(InputAction.CallbackContext context)
        {
            if (_inputEnabled)
                _flapsRaw = ReadAxis(context);
        }

        public void OnAirbrakes(InputAction.CallbackContext context)
        {
            if (_inputEnabled)
                _airbrakesRaw = ReadAxis(context);
        }

        public void OnWheelBrakes(InputAction.CallbackContext context)
        {
            if (_inputEnabled)
                _wheelBrakesRaw = ReadAxis(context);
        }

        private void Awake()
        {
            _body = GetComponent<PlaneRigidbody>();
            _engine = GetComponentInChildren<AircraftEngine>(true);
            _playerInput = GetComponent<PlayerInput>();
            _throttle01 = FlightSimMath.Saturate(initialThrottle);
            CacheStickActions();
        }

        private void OnEnable()
        {
            CacheStickActions();
        }

        private void Update()
        {
            if (_inputEnabled && !CheatFlags.BlockPlayerInput)
                PullStickAxes();
        }

        /// <summary>
        /// Called by <see cref="PlaneRigidbody"/> once per FixedUpdate, before sub-steps.
        /// </summary>
        public void PrePhysicsTick(float dt)
        {
            if (!_inputEnabled)
                return;

            if (CheatFlags.BlockPlayerInput)
            {
                _rawPitch = 0f;
                _rawRoll = 0f;
                _rawYaw = 0f;
                _throttleRaw = 0f;
                _flapsRaw = 0f;
                _airbrakesRaw = 0f;
                _wheelBrakesRaw = 0f;
            }
            else
            {
                PullStickAxes();
            }

            float pitch = invertPitch ? -_rawPitch : _rawPitch;
            float roll = invertRoll ? -_rawRoll : _rawRoll;
            float yaw = invertYaw ? -_rawYaw : _rawYaw;

            float pitchCmd = pitchStickBackNoseUp ? pitch : -pitch;
            UpdateAutoTrim(pitchCmd, dt);

            float rollSens = rollSpeed > 0.01f ? rollSpeed : 1f;
            float yawSens = yawSpeed > 0.01f ? yawSpeed : 1f;
            float aileronT = Clamp11(roll * rollSens);
            float elevatorT = Clamp11(pitchCmd + elevatorTrim);
            float rudderT = Clamp11(yaw * yawSens + CoordinatedRudder(aileronT, yaw));

            // Aircraft inertia is the smoothing. Rate-limiting the stick here is what
            // made input start late and keep going after release.
            if (stickFollowSeconds > 0.001f)
            {
                float maxStep = dt / stickFollowSeconds;
                _aileron01 = MoveToward(_aileron01, aileronT, maxStep);
                _elevator01 = MoveToward(_elevator01, elevatorT, maxStep);
                _rudder01 = MoveToward(_rudder01, rudderT, maxStep);
            }
            else
            {
                _aileron01 = aileronT;
                _elevator01 = elevatorT;
                _rudder01 = rudderT;
            }

            _throttle01 = throttleIsRate
                ? FlightSimMath.Saturate(_throttle01 + _throttleRaw * throttleRate * dt)
                : FlightSimMath.Saturate(_throttleRaw);

            _flaps01 = flapsIsRate
                ? FlightSimMath.Saturate(_flaps01 + _flapsRaw * flapsRate * dt)
                : FlightSimMath.Saturate(_flapsRaw);

            _airbrake01 = MoveToward(_airbrake01, FlightSimMath.Saturate(_airbrakesRaw), airbrakeRate * dt);
            _wheelBrake01 = FlightSimMath.Saturate(_wheelBrakesRaw);
        }

        private void UpdateAutoTrim(float pitchCmd, float dt)
        {
            if (!autoElevatorTrim || _body == null || _body.AnyGearDown)
                return;
            if (Mathf.Abs(pitchCmd) > autoTrimStickDeadzone)
                return;

            Vector3 worldUp = -AtmosphericModel.SampleGravity();
            if (worldUp.sqrMagnitude < 0.01f)
                worldUp = Vector3.up;
            worldUp.Normalize();

            float cosBank = Vector3.Dot(_body.Orientation * Vector3.up, worldUp);
            if (cosBank < 0.25f)
                return;

            float targetNz = 1f / cosBank;
            float err = targetNz - _body.LoadFactorNz;
            elevatorTrim = Clamp11(elevatorTrim + autoTrimRate * err * dt);
        }

        /// <summary>
        /// Aileron drag yaws the nose off the roll axis, so A/D alone skids unless Q/E is held
        /// with it. Mix the same rudder in automatically, then wash out leftover sideslip.
        /// Stays off on the ground so A/D cannot steal nosewheel steering, and fades when the
        /// player is already on the rudder so a slip is still possible.
        /// </summary>
        private float CoordinatedRudder(float aileron, float manualYaw)
        {
            if (_body == null || _body.AnyGearDown)
                return 0f;

            float mix = aileron * aileronRudderMix;
            float damper = 0f;
            if (sideslipYawGain > 0.01f)
            {
                Vector3 vBody = _body.InverseTransformDirection(
                    _body.Velocity - AtmosphericModel.SampleWind());
                damper = FlightSimMath.Sideslip(vBody) * sideslipYawGain;
            }

            float manual = Mathf.Abs(manualYaw);
            if (manual > 0.05f)
                damper *= 1f - FlightSimMath.Saturate((manual - 0.05f) / 0.25f);

            return mix + damper;
        }

        /// <summary>
        /// Dynamic-pressure-scaled deflection limit, radians. Surfaces call this so ailerons,
        /// elevators and the rudder all share the same q-stiffening law.
        /// </summary>
        public float GetLimitedDeflectionRad(float maxDeflectionRad, in AtmosphereSample atmo, float tas)
        {
            return maxDeflectionRad * ComputeQScale(atmo, tas);
        }

        private float ComputeQScale(in AtmosphereSample atmo, float tas)
        {
            float q = atmo.DynamicPressure(tas);
            float qRef = Mathf.Max(50f, referenceDynamicPressure);
            float excess = q / qRef - 1f;
            if (excess < 0f)
                excess = 0f;
            return 1f / (1f + qStiffeningGain * excess);
        }

        private void CacheStickActions()
        {
            if (!_playerInput)
                _playerInput = GetComponent<PlayerInput>();
            if (!_playerInput || _playerInput.actions == null)
                return;

            _pitchAction = _playerInput.actions.FindAction("Pitch", false);
            _rollAction = _playerInput.actions.FindAction("Roll", false);
            _yawAction = _playerInput.actions.FindAction("Yaw", false);
        }

        /// <summary>
        /// Keyboard WASD/QE is read from the device so swapping A/D while W/S is held cannot
        /// get stuck on a missed PlayerInput event. Gamepad still comes from the actions.
        /// </summary>
        private void PullStickAxes()
        {
            CacheStickActions();

            Keyboard kb = Keyboard.current;
            bool keyboardStick = kb != null && (
                kb.wKey.isPressed || kb.sKey.isPressed ||
                kb.aKey.isPressed || kb.dKey.isPressed ||
                kb.qKey.isPressed || kb.eKey.isPressed);

            if (keyboardStick)
            {
                _rawPitch = AxisFromKeys(kb.wKey.isPressed, kb.sKey.isPressed);
                _rawRoll = AxisFromKeys(kb.dKey.isPressed, kb.aKey.isPressed);
                _rawYaw = AxisFromKeys(kb.eKey.isPressed, kb.qKey.isPressed);
                return;
            }

            if (_pitchAction != null)
                _rawPitch = _pitchAction.ReadValue<float>();
            if (_rollAction != null)
                _rawRoll = _rollAction.ReadValue<float>();
            if (_yawAction != null)
                _rawYaw = _yawAction.ReadValue<float>();
        }

        private static float AxisFromKeys(bool negative, bool positive)
        {
            if (negative == positive)
                return 0f;
            return positive ? 1f : -1f;
        }

        private static float ReadAxis(InputAction.CallbackContext context)
        {
            return context.ReadValue<float>();
        }

        private static float Clamp11(float x)
        {
            if (x < -1f) return -1f;
            if (x > 1f) return 1f;
            return x;
        }

        private static float MoveToward(float current, float target, float maxDelta)
        {
            if (maxDelta < 0f)
                maxDelta = -maxDelta;
            float d = target - current;
            if (d > maxDelta) return current + maxDelta;
            if (d < -maxDelta) return current - maxDelta;
            return target;
        }

        private void OnGUI()
        {
            if (!drawHud || !_body)
                return;

            _hudClock += Time.unscaledDeltaTime;
            if (_hudClock >= 0.2f || _hudText.Length == 0)
            {
                _hudClock = 0f;
                RebuildHudText();
            }

            float x = hudPosition.x;
            float y = hudPosition.y;
            GUI.Box(new Rect(x, y, 320f, 210f), "6-DOF Flight");
            GUI.Label(new Rect(x + 10f, y + 24f, 300f, 180f), _hudText);
        }

        // Temporary HUD. We will replace this with a premade canvas later.
        private void RebuildHudText()
        {
            AtmosphereSample atmo = AtmosphericModel.SampleAt(_body.Position);
            float tas = _body.TrueAirspeed;
            Vector3 vBody = _body.InverseTransformDirection(_body.Velocity - AtmosphericModel.SampleWind());
            float aoa = FlightSimMath.AngleOfAttack(vBody) * FlightSimMath.Rad2Deg;
            float beta = FlightSimMath.Sideslip(vBody) * FlightSimMath.Rad2Deg;
            float ias = tas * Mathf.Sqrt(atmo.Density / AtmosphericModel.StandardSeaLevelDensity);
            float mach = atmo.SpeedOfSound > 1f ? tas / atmo.SpeedOfSound : 0f;
            float gLoad = _body.LoadFactorNz;

            _hudBuilder.Length = 0;
            _hudBuilder.Append("ALT  ").Append(atmo.Altitude.ToString("F0")).Append(" m\n");
            _hudBuilder.Append("TAS  ").Append(((tas * FlightSimMath.AirSpeedToKnots) * FlightSimMath.KnotsToKmh).ToString("F0")).Append(" km/u   IAS ");
            _hudBuilder.Append(((ias * FlightSimMath.AirSpeedToKnots) * FlightSimMath.KnotsToKmh).ToString("F0")).Append(" km/u\n");
            _hudBuilder.Append("M    ").Append(mach.ToString("F2")).Append("    q ");
            _hudBuilder.Append(atmo.DynamicPressure(tas).ToString("F0")).Append(" Pa\n");
            _hudBuilder.Append("AoA  ").Append(aoa.ToString("F1")).Append("°    β ");
            _hudBuilder.Append(beta.ToString("F1")).Append("°\n");
            _hudBuilder.Append("G    ").Append(gLoad.ToString("F2")).Append("    TRIM ");
            _hudBuilder.Append(elevatorTrim.ToString("F2")).Append("    ρ ");
            _hudBuilder.Append(atmo.Density.ToString("F3")).Append(" kg/m³\n");
            _hudBuilder.Append("THR  ").Append((_throttle01 * 100f).ToString("F0")).Append("%   T ");
            _hudBuilder.Append(_engine != null ? _engine.LastThrust.ToString("F0") : "0").Append(" N\n");
            _hudBuilder.Append("FLP  ").Append((_flaps01 * 100f).ToString("F0")).Append("%   BRK ");
            _hudBuilder.Append((_airbrake01 * 100f).ToString("F0")).Append("%   WHL ");
            _hudBuilder.Append((_wheelBrake01 * 100f).ToString("F0")).Append("%\n");
            _hudBuilder.Append("A/E/R ").Append(_aileron01.ToString("F2")).Append("  ");
            _hudBuilder.Append(_elevator01.ToString("F2")).Append("  ").Append(_rudder01.ToString("F2")).Append('\n');
            _hudBuilder.Append("W/S pitch  A/D roll  Q/E yaw\n");
            _hudBuilder.Append("R/F throttle  X/Z flaps  Shift airbrake  Space wheel");
            _hudText = _hudBuilder.ToString();
        }
    }
}
