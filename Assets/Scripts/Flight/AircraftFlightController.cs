using UnityEngine;
using UnityEngine.InputSystem;
using System.Text;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Reads Inspector-assigned <see cref="InputActionProperty"/> axes and converts them into
    /// rate-limited, dynamic-pressure-scaled control deflections and engine throttle.
    ///
    /// Contract: actions are enabled by a PlayerInput / Input Action asset in the Inspector.
    /// This class never calls Enable(), Disable(), or new InputAction().
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlaneRigidbody))]
    [AddComponentMenu("Airplane/Aircraft Flight Controller")]
    public sealed class AircraftFlightController : MonoBehaviour
    {
        [Header("Input")]
        [SerializeField] private InputActionProperty pitch;
        [SerializeField] private InputActionProperty roll;
        [SerializeField] private InputActionProperty yaw;
        [SerializeField] private InputActionProperty throttle;
        [SerializeField] private InputActionProperty flaps;
        [SerializeField] private InputActionProperty airbrakes;
        [SerializeField] private InputActionProperty wheelBrakes;

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

        [Tooltip("Stick travel rate, mapped as (this / 90) of full deflection per second. 120 ≈ 0.75 s to full.")]
        [SerializeField] private float maxHingeRateDeg = 120f;

        [Header("Trim")]
        [Tooltip("Constant elevator offset, −1..1, for hands-off cruise. Negative = nose down on a conventional tail.")]
        [SerializeField] [Range(-1f, 1f)] private float elevatorTrim;

        [Header("Debug HUD")]
        [SerializeField] private bool drawHud = true;
        [SerializeField] private Vector2 hudPosition = new Vector2(16f, 16f);

        private PlaneRigidbody _body;
        private AircraftEngine _engine;
        private PlayerInput _playerInput;
        private InputAction _pitchAction;
        private InputAction _rollAction;
        private InputAction _yawAction;
        private InputAction _throttleAction;
        private InputAction _flapsAction;
        private InputAction _airbrakesAction;
        private InputAction _wheelBrakesAction;

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
        private readonly StringBuilder _hudBuilder = new StringBuilder(512);
        private string _hudText = "";
        private float _hudClock;

        public float Throttle01 => _throttle01;
        public float Flaps01 => _flaps01;
        public float Airbrake01 => _airbrake01;
        public float WheelBrake01 => _wheelBrake01;
        public float Aileron01 => _aileron01;
        public float Elevator01 => _elevator01;
        public float Rudder01 => _rudder01;
        public float RawYaw => _rawYaw;
        public float RawPitch => _rawPitch;
        public float RawRoll => _rawRoll;

        public void SetInitialThrottle(float value)
        {
            initialThrottle = FlightSimMath.Saturate(value);
            _throttle01 = initialThrottle;
        }

        private void Awake()
        {
            _body = GetComponent<PlaneRigidbody>();
            _engine = GetComponentInChildren<AircraftEngine>(true);
            _playerInput = GetComponent<PlayerInput>();
            _throttle01 = FlightSimMath.Saturate(initialThrottle);
        }
        
        /// <summary>
        /// Resolves Inspector references, preferring the PlayerInput clone when present
        /// so we never have to call Enable() ourselves.
        /// </summary>
        private InputAction Resolve(InputActionProperty property, string actionName)
        {
            if (_playerInput)
            {
                InputActionAsset asset = _playerInput.actions;
                if (asset)
                {
                    InputAction fromPlayer = asset.FindAction(actionName, false);
                    if (fromPlayer != null)
                        return fromPlayer;
                }
            }

            return property.action;
        }

        private void CacheActions()
        {
            if (_pitchAction != null)
                return;
            _pitchAction = Resolve(pitch, "Pitch");
            _rollAction = Resolve(roll, "Roll");
            _yawAction = Resolve(yaw, "Yaw");
            _throttleAction = Resolve(throttle, "Throttle");
            _flapsAction = Resolve(flaps, "Flaps");
            _airbrakesAction = Resolve(airbrakes, "Airbrakes");
            _wheelBrakesAction = Resolve(wheelBrakes, "WheelBrakes");
        }

        /// <summary>
        /// Called by <see cref="PlaneRigidbody"/> once per FixedUpdate, before sub-steps.
        /// Hinge rate limits run at the outer physics tick so RK4 sub-steps see a frozen deflection.
        /// </summary>
        public void PrePhysicsTick(float dt)
        {
            CacheActions();
            _rawPitch = ReadAxis(_pitchAction);
            _rawRoll = ReadAxis(_rollAction);
            _rawYaw = ReadAxis(_yawAction);

            if (invertPitch) _rawPitch = -_rawPitch;
            if (invertRoll) _rawRoll = -_rawRoll;
            if (invertYaw) _rawYaw = -_rawYaw;

            float pitchCmd = pitchStickBackNoseUp ? _rawPitch : -_rawPitch;

            AtmosphereSample atmo = AtmosphericModel.SampleAt(_body != null ? _body.Position : transform.position);
            float tas = _body ? _body.TrueAirspeed : 0f;
            // Dynamic pressure only slows the hinge, it must not shrink max deflection.
            // Otherwise the tails still restore at full q and the stick "bounces back".
            float qRate = Mathf.Lerp(1f, ComputeQScale(atmo, tas), 0.3f);
            float hinge = maxHingeRateDeg < 1f ? 120f : maxHingeRateDeg;
            float maxStep = (hinge / 90f) * qRate * dt;

            float rollSens = rollSpeed > 0.01f ? rollSpeed : 1f;
            float yawSens = yawSpeed > 0.01f ? yawSpeed : 1f;

            _aileron01 = MoveToward(_aileron01, Clamp11(_rawRoll * rollSens), maxStep);
            _elevator01 = MoveToward(_elevator01, Clamp11(pitchCmd + elevatorTrim), maxStep);
            _rudder01 = MoveToward(_rudder01, Clamp11(_rawYaw * yawSens), maxStep);

            float throttleRaw = ReadAxis(_throttleAction);
            if (throttleIsRate)
                _throttle01 = FlightSimMath.Saturate(_throttle01 + throttleRaw * throttleRate * dt);
            else
                _throttle01 = FlightSimMath.Saturate(throttleRaw);

            float flapsRaw = ReadAxis(_flapsAction);
            if (flapsIsRate)
                _flaps01 = FlightSimMath.Saturate(_flaps01 + flapsRaw * flapsRate * dt);
            else
                _flaps01 = FlightSimMath.Saturate(flapsRaw);

            float brakeRaw = ReadAxis(_airbrakesAction);
            _airbrake01 = MoveToward(_airbrake01, FlightSimMath.Saturate(brakeRaw), airbrakeRate * dt);

            _wheelBrake01 = FlightSimMath.Saturate(ReadAxis(_wheelBrakesAction));
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

        private static float ReadAxis(InputAction action)
        {
            if (action == null)
                return 0f;
            return action.ReadValue<float>();
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
            Vector3 g = AtmosphericModel.SampleGravity();
            float gMag = g.magnitude;
            float gLoad = 1f;
            if (gMag > 0.1f)
            {
                Vector3 properAcc = _body.Acceleration - g;
                gLoad = Vector3.Dot(properAcc, transform.up) / gMag;
                gLoad = gLoad / 10;
            }

            _hudBuilder.Length = 0;
            _hudBuilder.Append("ALT  ").Append(atmo.Altitude.ToString("F0")).Append(" m\n");
            _hudBuilder.Append("TAS  ").Append(((tas * FlightSimMath.AirSpeedToKnots) * FlightSimMath.KnotsToKmh).ToString("F0")).Append(" km/u   IAS ");
            _hudBuilder.Append(((ias * FlightSimMath.AirSpeedToKnots) * FlightSimMath.KnotsToKmh).ToString("F0")).Append(" km/u\n");
            _hudBuilder.Append("M    ").Append(mach.ToString("F2")).Append("    q ");
            _hudBuilder.Append(atmo.DynamicPressure(tas).ToString("F0")).Append(" Pa\n");
            _hudBuilder.Append("AoA  ").Append(aoa.ToString("F1")).Append("°    β ");
            _hudBuilder.Append(beta.ToString("F1")).Append("°\n");
            _hudBuilder.Append("G    ").Append(gLoad.ToString("F2")).Append("    ρ ");
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
