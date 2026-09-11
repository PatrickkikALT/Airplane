using Airplane.FlightSimulation;
using UnityEngine;

namespace Airplane.AI
{
    /// <summary>
    /// Turns "point the nose there and hold this speed" into stick, rudder and throttle.
    ///
    /// The bots fly the same aerodynamic aircraft the player does, through the same control
    /// deflections, so this has to be a real controller rather than a lerp on the transform. It is a
    /// conventional bank-to-turn autopilot: roll the lift vector toward the target, then pull.
    ///
    /// Altitude hold closes on flight-path angle. The PI owns the elevator, including the airframe's
    /// hands-off bias, so a frozen −0.1 trim cannot drag them into the dirt. Attitude tracking is
    /// only used when they are pointing at a gun solution or pulling up off the ground.
    /// </summary>
    public sealed class BotAutopilot
    {
        private const float RollP = 1.7f;
        private const float RollD = 0.45f;
        private const float PitchP = 1.35f;
        private const float PitchD = 0.5f;
        private const float PitchI = 0.12f;
        private const float YawToBank = 2.2f;
        private const float RudderCoordination = 1.4f;
        private const float ThrottleGain = 0.022f;
        private const float DerivativeSmoothing = 0.35f;
        private const float DefaultMaxFlightPathDeg = 14f;

        private static readonly Vector3 BodyForward = new Vector3(1f, 0f, 0f);
        private static readonly Vector3 BodyUp = new Vector3(0f, 1f, 0f);
        private static readonly Vector3 BodyRight = new Vector3(0f, 0f, 1f);

        private float _bankErrorPrev;
        private float _pitchErrorPrev;
        private float _bankRate;
        private float _pitchRate;
        private float _pitchIntegral;
        private bool _primed;
        private bool _holdAltitudePrev;

        /// <summary>Bank angle right-wing-down positive, radians. Exposed for the debug overlay.</summary>
        public float BankAngle { get; private set; }

        /// <summary>Angle between the nose and the commanded direction, radians.</summary>
        public float TrackingError { get; private set; }

        public void Reset()
        {
            _bankErrorPrev = 0f;
            _pitchErrorPrev = 0f;
            _bankRate = 0f;
            _pitchRate = 0f;
            _pitchIntegral = 0f;
            _primed = false;
            _holdAltitudePrev = false;
        }

        public BotControlOutput Tick(
            PlaneRigidbody body,
            in BotFlightCommand command,
            BotSkillProfile profile,
            float elevatorTrim,
            float dt)
        {
            BotControlOutput output = default;
            if (body == null || dt <= 0f)
                return output;

            Vector3 desired = command.Direction;
            if (desired.sqrMagnitude < 1e-6f)
                desired = body.TransformDirection(BodyForward);
            desired.Normalize();

            Vector3 worldUp = -AtmosphericModel.SampleGravity();
            if (worldUp.sqrMagnitude < 0.01f)
                worldUp = Vector3.up;
            worldUp.Normalize();

            Vector3 upWorld = body.TransformDirection(BodyUp);
            Vector3 rightWorld = body.TransformDirection(BodyRight);

            // Positive = right wing down. Derived from the airframe axes against local up, so it
            // stays correct through the vertical where Euler angles fall apart.
            float bank = Mathf.Atan2(-Vector3.Dot(rightWorld, worldUp), Vector3.Dot(upWorld, worldUp));
            BankAngle = bank;

            Vector3 flow = body.Velocity - AtmosphericModel.SampleWind();
            float horizSpeed = Mathf.Sqrt(flow.x * flow.x + flow.z * flow.z);
            float actualFpa = Mathf.Atan2(flow.y, Mathf.Max(1f, horizSpeed));

            float yawError;
            float pitchError;
            if (command.HoldAltitude)
            {
                Vector3 horiz = desired;
                horiz.y = 0f;
                if (horiz.sqrMagnitude < 1e-4f)
                {
                    horiz = body.TransformDirection(BodyForward);
                    horiz.y = 0f;
                }

                if (horiz.sqrMagnitude < 1e-4f)
                    horiz = Vector3.forward;

                horiz.Normalize();
                Vector3 desiredBody = body.InverseTransformDirection(horiz);
                yawError = Mathf.Atan2(desiredBody.z, desiredBody.x);

                // Close altitude on the flight path, not the nose. Pointing the nose at the horizon
                // with leftover thrust is still a 1G climb; pushing the path down is what actually
                // stops them going upstairs.
                float altError = command.TargetAltitude - body.Position.y;
                float maxFpaDeg = command.MaxFlightPathDeg > 0.1f ? command.MaxFlightPathDeg : DefaultMaxFlightPathDeg;
                // Climb is allowed to be steeper than a dive so leftover nose-down bias cannot
                // walk them into the ground, and they cannot zoom-climb their way into space.
                float desiredVs = Mathf.Clamp(altError * 0.4f, -22f, 55f);
                float tas = Mathf.Max(30f, body.TrueAirspeed);
                float desiredFpa = Mathf.Atan2(desiredVs, tas);
                float maxFpa = maxFpaDeg * Mathf.Deg2Rad;
                float minFpa = -Mathf.Min(maxFpa, 10f * Mathf.Deg2Rad);
                desiredFpa = Mathf.Clamp(desiredFpa, minFpa, maxFpa);
                pitchError = FlightSimMath.WrapPi(desiredFpa - actualFpa);
                TrackingError = Mathf.Abs(pitchError);
            }
            else
            {
                Vector3 desiredBody = body.InverseTransformDirection(desired);
                float horizontal = Mathf.Sqrt(desiredBody.x * desiredBody.x + desiredBody.z * desiredBody.z);
                yawError = Mathf.Atan2(desiredBody.z, desiredBody.x);
                pitchError = Mathf.Atan2(desiredBody.y, horizontal);
                TrackingError = Vector3.Angle(body.TransformDirection(BodyForward), desired) * Mathf.Deg2Rad;
            }

            float maxBank = Mathf.Clamp(command.MaxBankDeg > 0f ? command.MaxBankDeg : profile.maxBankDeg, 5f, 89f) * Mathf.Deg2Rad;
            float bankCommand = Mathf.Clamp(yawError * YawToBank, -maxBank, maxBank);

            if (command.HoldWingsLevel)
                bankCommand = 0f;

            float bankError = FlightSimMath.WrapPi(bankCommand - bank);

            if (!_primed)
            {
                _bankErrorPrev = bankError;
                _pitchErrorPrev = pitchError;
                _primed = true;
            }

            float bankDerivative = (bankError - _bankErrorPrev) / dt;
            float pitchDerivative = (pitchError - _pitchErrorPrev) / dt;
            _bankErrorPrev = bankError;
            _pitchErrorPrev = pitchError;
            _bankRate = Mathf.Lerp(_bankRate, bankDerivative, DerivativeSmoothing);
            _pitchRate = Mathf.Lerp(_pitchRate, pitchDerivative, DerivativeSmoothing);

            output.Aileron = Clamp11(RollP * bankError + RollD * _bankRate);

            if (command.HoldAltitude != _holdAltitudePrev)
            {
                _pitchIntegral = 0f;
                _holdAltitudePrev = command.HoldAltitude;
            }

            float pitchP = command.HoldAltitude ? 2.1f : PitchP;
            float pitchI = command.HoldAltitude ? 0.55f : PitchI;
            float pitchILimit = command.HoldAltitude ? 0.9f : 0.35f;
            float stick = pitchP * pitchError + PitchD * _pitchRate;

            if (Mathf.Abs(pitchError) < 0.5f)
            {
                _pitchIntegral = Mathf.Clamp(_pitchIntegral + pitchError * dt, -pitchILimit, pitchILimit);
                stick += pitchI * _pitchIntegral;
            }
            else
            {
                _pitchIntegral = Mathf.MoveTowards(_pitchIntegral, 0f, dt);
            }

            stick = ApplyEnvelopeLimits(body, profile, stick);
            // Altitude-hold PI includes the hands-off elevator. Adding the −0.1 trim on top of a
            // zero path-error stick was a constant descent, and the old cruise target followed it.
            output.Elevator = command.HoldAltitude
                ? Clamp11(stick)
                : Clamp11(stick + elevatorTrim);

            // Turn coordination by geometry: yaw the nose toward the velocity vector. Reading the
            // slip out of the flow this way needs no sign convention from the aero code.
            float speed = FlightSimMath.SafeMagnitude(flow);
            if (speed > 5f)
            {
                float slip = Vector3.Dot(flow / speed, rightWorld);
                output.Rudder = Clamp11(slip * RudderCoordination);
            }

            float targetSpeed = command.Speed > 1f ? command.Speed : profile.cruiseSpeed;
            float speedError = targetSpeed - body.TrueAirspeed;
            output.Throttle = FlightSimMath.Saturate(0.6f + speedError * ThrottleGain);

            float overspeed = -speedError;
            output.Airbrake = command.AllowAirbrake && overspeed > 20f
                ? FlightSimMath.Saturate((overspeed - 20f) / 45f)
                : 0f;

            return output;
        }

        /// <summary>
        /// Soft angle-of-attack and load-factor limiters. Positive (nose-up) command is progressively
        /// washed out as either limit is approached and reversed once it is exceeded, so a bot that
        /// over-demands unloads and flies out of it rather than mushing into a spin.
        /// </summary>
        private static float ApplyEnvelopeLimits(PlaneRigidbody body, BotSkillProfile profile, float pitchCommand)
        {
            if (pitchCommand <= 0f)
                return pitchCommand;

            Vector3 flowBody = body.InverseTransformDirection(body.Velocity - AtmosphericModel.SampleWind());
            float aoaDeg = FlightSimMath.AngleOfAttack(flowBody) * FlightSimMath.Rad2Deg;
            float aoaLimit = Mathf.Max(6f, profile.aoaLimitDeg);
            float aoaScale = 1f - FlightSimMath.Smoothstep(aoaLimit * 0.7f, aoaLimit, aoaDeg);

            float gLimit = Mathf.Max(2f, profile.maxLoadFactor);
            float gScale = 1f - FlightSimMath.Smoothstep(gLimit * 0.8f, gLimit, body.LoadFactorNz);

            float limited = pitchCommand * Mathf.Min(aoaScale, gScale);

            if (aoaDeg > aoaLimit * 1.25f)
                limited = -0.3f;

            return limited;
        }

        private static float Clamp11(float x)
        {
            if (x < -1f) return -1f;
            if (x > 1f) return 1f;
            return x;
        }
    }

    /// <summary>What the state machine asks of the autopilot this tick.</summary>
    public struct BotFlightCommand
    {
        /// <summary>World-space direction to put the nose on. Vertical is ignored when HoldAltitude is set.</summary>
        public Vector3 Direction;

        /// <summary>Target true airspeed, m/s.</summary>
        public float Speed;

        /// <summary>Bank limit for this manoeuvre, degrees. 0 falls back to the profile.</summary>
        public float MaxBankDeg;

        /// <summary>Roll upright and stay there, ignoring the turn demand.</summary>
        public bool HoldWingsLevel;

        public bool AllowAirbrake;

        /// <summary>
        /// If true, the autopilot holds <see cref="TargetAltitude"/> with a shallow flight-path
        /// instead of pointing at the vertical component of <see cref="Direction"/>.
        /// </summary>
        public bool HoldAltitude;

        public float TargetAltitude;

        /// <summary>Hard cap on climb/dive angle while holding altitude, degrees.</summary>
        public float MaxFlightPathDeg;
    }

    /// <summary>Deflections and lever positions to hand to the flight controller.</summary>
    public struct BotControlOutput
    {
        public float Aileron;
        public float Elevator;
        public float Rudder;
        public float Throttle;
        public float Airbrake;
        public float Flaps;
    }
}
