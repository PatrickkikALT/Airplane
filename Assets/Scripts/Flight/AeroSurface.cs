using UnityEngine;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Discrete lifting / drag surface. The airframe is a composition of these:
    /// left/right wing, H-stab halves, V-stab, fuselage.
    ///
    /// Local airflow:
    ///   v_surf = v_com + ω × r − wind + prop-wash(r)
    ///
    /// Lift / drag (q = ½ ρ V²):
    ///   L = q S C_L ,  D = q S C_D
    ///
    /// C_L / C_D cover attached (linear + induced), stall, and post-stall (flat-plate) regimes
    /// so the polar is defined over a full 360° angle of attack.
    /// Control deflection changes camber (zero-lift α), C_L offset, stall boundaries, and C_D.
    /// </summary>
    [AddComponentMenu("Airplane/Aero Surface")]
    public sealed class AeroSurface : MonoBehaviour
    {
        [Header("Geometry")]
        [Tooltip("Reference area S, m². Lift and drag both scale with S.")]
        [SerializeField] private float area = 8f;

        [Tooltip("Span b, metres. Aspect ratio AR = b² / S is used for induced drag.")]
        [SerializeField] private float span = 5f;

        [Tooltip("Mean aerodynamic chord, metres. Used for gizmo drawing and optional pitching-moment (currently visualisation).")]
        [SerializeField] private float chord = 1.6f;

        [Tooltip("If > 0, overrides AR = b² / S. Leave 0 to compute from span and area.")]
        [SerializeField] private float aspectRatioOverride;

        [Tooltip("Oswald efficiency e. 0.7–0.85 for a rectangular trainer wing, lower for a low-AR tail.")]
        [SerializeField] [Range(0.4f, 1f)] private float oswaldEfficiency = 0.8f;

        [Header("Airfoil — attached")]
        [Tooltip("Zero-lift pitching of the section, degrees (negative = cambered wing produces lift at α = 0).")]
        [SerializeField] private float zeroLiftAlphaDeg = -2f;

        [Tooltip("Lift-curve slope C_Lα, per radian. Thin-airfoil 2π ≈ 6.28; finite wing ≈ 2π AR/(AR+2) ≈ 4.5–5.5.")]
        [SerializeField] private float clAlphaPerRadian = 4.8f;

        [Tooltip("C_L at α = 0 after zero-lift shift (section camber). Typical cambered wing 0.2–0.4.")]
        [SerializeField] private float cl0;

        [Tooltip("Profile parasitic drag C_D0 of the section (skin friction + form). Wing ~0.02–0.03, fuselage much higher.")]
        [SerializeField] private float cd0 = 0.022f;

        [Header("Stall")]
        [Tooltip("Positive stall angle, degrees, flaps up.")]
        [SerializeField] private float stallAlphaPositiveDeg = 14f;

        [Tooltip("Negative stall angle, degrees (usually milder camber stall on the underside).")]
        [SerializeField] private float stallAlphaNegativeDeg = -12f;

        [Tooltip("Half-width of the stall blend, degrees. Larger = gentler break.")]
        [SerializeField] private float stallSoftnessDeg = 6f;

        [Tooltip("Peak separated (flat-plate) C_L scale. Flat plate C_L ≈ sin(2α); 1.0–1.2 is typical.")]
        [SerializeField] private float separatedClPeak = 1.05f;

        [Tooltip("Separated C_D scale multiplying sin²(α). Flat plate ~2.0 in 2D, ~1.2–1.6 in 3D.")]
        [SerializeField] private float separatedCd = 1.35f;

        [Header("Control Surface")]
        [SerializeField] private AeroControlType controlType = AeroControlType.None;

        [Tooltip("Sign applied to the stick axis. Left aileron = +1, right aileron = −1 so a right-stick roll drops the left trailing edge.")]
        [SerializeField] private float controlSign = 1f;

        [Tooltip("Maximum trailing-edge deflection, degrees, at q ≈ 0 (low speed).")]
        [SerializeField] private float maxDeflectionDeg = 20f;

        [Tooltip("How many degrees of zero-lift α the surface shifts per degree of deflection (control effectiveness η · τ). 0.4–0.7.")]
        [SerializeField] [Range(0f, 1.2f)] private float deflectionAlphaEffectiveness = 0.55f;

        [Tooltip("Additional C_L per radian of deflection (camber increment), on top of the α shift.")]
        [SerializeField] private float clPerRadianDeflection = 0.8f;

        [Tooltip("Extra C_D per (radian of deflection)². Hinge / form drag of a deflected surface.")]
        [SerializeField] private float cdPerDeflectionSq = 0.12f;

        [Tooltip("Degrees the stall angle walks per degree of trailing-edge-down deflection.")]
        [SerializeField] private float stallShiftPerDeflection = 0.35f;

        [Header("Secondary Devices")]
        [Tooltip("Flap C_L increment at flaps = 1. Applied in addition to the primary control.")]
        [SerializeField] private float flapClIncrement = 0f;

        [Tooltip("Flap C_D increment at flaps = 1.")]
        [SerializeField] private float flapCdIncrement = 0f;

        [Tooltip("Flap-induced shift of stall α (degrees) at flaps = 1. Positive = stall delayed (Fowler); negative = earlier.")]
        [SerializeField] private float flapStallShiftDeg = 0f;

        [Tooltip("Airbrake C_D increment at airbrakes = 1.")]
        [SerializeField] private float airbrakeCdIncrement = 0f;

        [Header("Fuselage / Bluff")]
        [SerializeField] private AeroSurfaceMode surfaceMode = AeroSurfaceMode.LiftingAirfoil;

        [Tooltip("Side-force derivative C_Yβ per radian (bluff / fuselage). Positive produces a restoring weathervane.")]
        [SerializeField] private float cyBetaPerRadian = -0.6f;

        [Tooltip("Additional C_D from sin²(α) on a bluff body.")]
        [SerializeField] private float bluffCdAlpha = 0.35f;

        [Header("Flow")]
        [Tooltip("Multiplier on the geometric prop-wash cylinder. Tails ~1, outboard wings ~0.")]
        [SerializeField] [Range(0f, 1.5f)] private float propWashInfluence = 0.25f;

        [Tooltip("Tail downwash coupling: α_eff -= factor · α_body. Keep this small or pitch will spring back to trim.")]
        [SerializeField] [Range(0f, 0.8f)] private float wingDownwashFactor;

        [Tooltip("Scales CL from angle of attack only, not from stick deflection. <1 on tails = less weathercock.")]
        [SerializeField] [Range(0.05f, 1.5f)] private float alphaRestoringScale = 1f;

        [Tooltip("Skip force evaluation below this local airspeed (m/s) to avoid 0/0 at rest.")]
        [SerializeField] private float minAirspeed = 0.6f;

        [Tooltip("If true, apply a simple Prandtl-Glauert C_Lα boost for M < 0.8.")]
        [SerializeField] private bool compressibilityCorrection = true;

        [Header("Debug")]
        [SerializeField] private bool drawGizmos = true;
        [SerializeField] private float gizmoForceScale = 0.0008f;
        [SerializeField] private Color gizmoColor = new Color(0.3f, 0.75f, 1f, 0.9f);

        private AircraftFlightController _controller;
        private PlaneRigidbody _body;
        private Vector3 _localPos;
        private Quaternion _localRot;
        private bool _localPoseCached;
        private float _appliedDeflectionRad;
        private Vector3 _lastForceWorld;
        private Vector3 _lastLiftWorld;
        private Vector3 _lastDragWorld;
        private Vector3 _lastCenterWorld;
        private float _lastCl;
        private float _lastCd;
        private float _lastAlphaDeg;

        public Vector3 LastForceWorld => _lastForceWorld;
        public Vector3 LastLiftWorld => _lastLiftWorld;
        public Vector3 LastDragWorld => _lastDragWorld;
        public Vector3 LastCenterWorld => _lastCenterWorld;
        public float LastCl => _lastCl;
        public float LastCd => _lastCd;
        public float LastAlphaDeg => _lastAlphaDeg;
        public float Area => area;
        public AeroControlType ControlType => controlType;

        private void Awake()
        {
            _controller = GetComponentInParent<AircraftFlightController>();
            _body = GetComponentInParent<PlaneRigidbody>();
            CacheLocalPose();
        }

        private void OnEnable()
        {
            if (_body == null)
                _body = GetComponentInParent<PlaneRigidbody>();
            CacheLocalPose();
        }

        private void CacheLocalPose()
        {
            if (_body == null)
                return;
            Transform root = _body.transform;
            _localPos = root.InverseTransformPoint(transform.position);
            _localRot = Quaternion.Inverse(root.rotation) * transform.rotation;
            _localPoseCached = true;
        }

        private void OnValidate()
        {
            area = Mathf.Max(0.01f, area);
            span = Mathf.Max(0.05f, span);
            chord = Mathf.Max(0.05f, chord);
        }

        public void ContributeForces(
            PlaneRigidbody body,
            in AtmosphereSample atmo,
            Vector3 windWorld,
            in PropWashState wash)
        {
            if (!_localPoseCached)
                CacheLocalPose();

            // Evaluate in the solver pose, not transform.position. The visible transform is
            // interpolated in Update and is stale during FixedUpdate sub-steps.
            Quaternion worldRot = body.Orientation * _localRot;
            Vector3 center = body.TransformPoint(_localPos);
            _lastCenterWorld = center;

            Vector3 v = body.GetPointVelocity(center) - windWorld;
            if (propWashInfluence > 0f)
                v += wash.VelocityAt(center) * propWashInfluence;

            float speed = FlightSimMath.SafeMagnitude(v);
            if (speed < minAirspeed)
            {
                _lastForceWorld = Vector3.zero;
                _lastLiftWorld = Vector3.zero;
                _lastDragWorld = Vector3.zero;
                _lastCl = 0f;
                _lastCd = 0f;
                return;
            }

            Vector3 vLocal = Quaternion.Inverse(worldRot) * v;
            float alpha = FlightSimMath.AngleOfAttack(vLocal);
            float beta = FlightSimMath.Sideslip(vLocal);
            if (wingDownwashFactor > 0f)
            {
                Vector3 vBody = body.InverseTransformDirection(body.Velocity - windWorld);
                alpha -= wingDownwashFactor * FlightSimMath.AngleOfAttack(vBody);
            }
            _lastAlphaDeg = alpha * FlightSimMath.Rad2Deg;

            float delta = ResolveDeflection();
            _appliedDeflectionRad = delta;

            float flaps = _controller != null ? _controller.Flaps01 : 0f;
            float brakes = _controller != null ? _controller.Airbrake01 : 0f;

            float cl, cd, cy;
            if (surfaceMode == AeroSurfaceMode.BluffBody)
                EvaluateBluff(alpha, beta, flaps, brakes, out cl, out cd, out cy);
            else
            {
                EvaluateAirfoil(alpha, delta, flaps, brakes, speed, atmo, out cl, out cd);
                cy = 0f;
            }

            _lastCl = cl;
            _lastCd = cd;

            float q = atmo.DynamicPressure(speed);
            float qS = q * area;

            Vector3 vHat = v / speed;
            Vector3 dragDir = -vHat;
            Vector3 spanHat = worldRot * Vector3.forward;
            Vector3 liftDir = Vector3.Cross(spanHat, vHat);
            float liftDirMag = liftDir.magnitude;
            if (liftDirMag > 1e-5f)
                liftDir /= liftDirMag;
            else
                liftDir = worldRot * Vector3.up;

            _lastLiftWorld = liftDir * (qS * cl);
            _lastDragWorld = dragDir * (qS * cd);
            Vector3 sideWorld = spanHat * (qS * cy);
            _lastForceWorld = _lastLiftWorld + _lastDragWorld + sideWorld;

            body.AddForceAtPosition(_lastForceWorld, center);
        }

        private float ResolveDeflection()
        {
            if (!_controller || controlType == AeroControlType.None)
                return 0f;

            float cmd = 0f;
            switch (controlType)
            {
                case AeroControlType.Aileron:
                    cmd = _controller.Aileron01 * controlSign;
                    break;
                case AeroControlType.Elevator:
                    cmd = _controller.Elevator01 * controlSign;
                    break;
                case AeroControlType.Rudder:
                    cmd = _controller.Rudder01 * controlSign;
                    break;
                case AeroControlType.Flap:
                    cmd = _controller.Flaps01 * controlSign;
                    break;
                case AeroControlType.Airbrake:
                    cmd = _controller.Airbrake01 * controlSign;
                    break;
            }

            return cmd * (maxDeflectionDeg * FlightSimMath.Deg2Rad);
        }

        private void EvaluateAirfoil(
            float alpha,
            float delta,
            float flaps,
            float brakes,
            float speed,
            in AtmosphereSample atmo,
            out float cl,
            out float cd)
        {
            float ar = aspectRatioOverride > 0.05f ? aspectRatioOverride : (span * span) / Mathf.Max(0.01f, area);
            if (ar < 0.4f)
                ar = 0.4f;

            float clAlpha = clAlphaPerRadian;
            if (compressibilityCorrection && atmo.SpeedOfSound > 1f)
            {
                float mach = speed / atmo.SpeedOfSound;
                if (mach > 0.2f && mach < 0.85f)
                {
                    float prandtl = Mathf.Sqrt(Mathf.Max(0.12f, 1f - mach * mach));
                    clAlpha = clAlphaPerRadian / prandtl;
                }
            }

            float a0 = zeroLiftAlphaDeg * FlightSimMath.Deg2Rad - deflectionAlphaEffectiveness * delta;
            float aEff = FlightSimMath.WrapPi(alpha - a0);

            float stallP = (stallAlphaPositiveDeg + stallShiftPerDeflection * (delta * FlightSimMath.Rad2Deg) + flapStallShiftDeg * flaps)
                           * FlightSimMath.Deg2Rad;
            float stallN = (stallAlphaNegativeDeg + stallShiftPerDeflection * (delta * FlightSimMath.Rad2Deg) - 0.4f * flapStallShiftDeg * flaps)
                           * FlightSimMath.Deg2Rad;
            float softness = stallSoftnessDeg * FlightSimMath.Deg2Rad;

            // Control CL must survive stall blend. S (nose up) puts the wing past stallP; the
            // separated polar used to ignore delta, so left and right ailerons cancelled out.
            float clControl = clPerRadianDeflection * delta;
            float clLin = cl0 + clAlpha * aEff * alphaRestoringScale + clControl + flapClIncrement * flaps;

            float s = Mathf.Sin(alpha);
            float c = Mathf.Cos(alpha);
            float clSep = separatedClPeak * (2f * s * c) + clControl;
            float cdSep = cd0 + separatedCd * (s * s);

            float absEff = aEff >= 0f ? aEff : -aEff;
            float stallAbs = aEff >= 0f ? stallP : -stallN;
            float sep = FlightSimMath.Smoothstep(stallAbs, stallAbs + softness, absEff);

            cl = clLin + (clSep - clLin) * sep;

            float clInd = clLin;
            float cdInd = (clInd * clInd) / (Mathf.PI * ar * Mathf.Max(0.35f, oswaldEfficiency));
            float cdAtt = cd0 + cdInd + cdPerDeflectionSq * (delta * delta) + flapCdIncrement * flaps + airbrakeCdIncrement * brakes;
            cd = cdAtt + (cdSep + airbrakeCdIncrement * brakes - cdAtt) * sep;
            if (cd < 0.008f)
                cd = 0.008f;
        }

        private void EvaluateBluff(float alpha, float beta, float flaps, float brakes, out float cl, out float cd, out float cy)
        {
            float sA = Mathf.Sin(alpha);
            float sB = Mathf.Sin(beta);
            cl = 0.35f * Mathf.Sin(2f * alpha);
            cd = cd0 + bluffCdAlpha * (sA * sA) + 0.25f * (sB * sB) + flapCdIncrement * flaps + airbrakeCdIncrement * brakes;
            cy = cyBetaPerRadian * beta * alphaRestoringScale;
            if (cd < cd0)
                cd = cd0;
        }

        private void OnDrawGizmos()
        {
            if (!drawGizmos)
                return;

            Vector3 c = transform.position;
            Vector3 chordDir = transform.right;
            Vector3 spanDir = transform.forward;
            Vector3 nrm = transform.up;
            float halfSpan = span * 0.5f;
            float halfChord = chord * 0.5f;

            Vector3 leL = c + chordDir * halfChord + spanDir * halfSpan;
            Vector3 leR = c + chordDir * halfChord - spanDir * halfSpan;
            Vector3 teL = c - chordDir * halfChord + spanDir * halfSpan;
            Vector3 teR = c - chordDir * halfChord - spanDir * halfSpan;

            Gizmos.color = gizmoColor;
            Gizmos.DrawLine(leL, leR);
            Gizmos.DrawLine(leR, teR);
            Gizmos.DrawLine(teR, teL);
            Gizmos.DrawLine(teL, leL);
            Gizmos.DrawSphere(c, 0.07f);

            if (_appliedDeflectionRad * _appliedDeflectionRad > 1e-6f)
            {
                Vector3 teC = c - chordDir * halfChord;
                Vector3 hingeAxis = spanDir;
                Vector3 deflected = Quaternion.AngleAxis(_appliedDeflectionRad * FlightSimMath.Rad2Deg, hingeAxis) * (-chordDir);
                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(teC, teC + deflected * (chord * 0.35f));
            }

            Gizmos.color = gizmoColor * 0.7f;
            Gizmos.DrawLine(c, c + nrm * 0.45f);

            if (!Application.isPlaying)
                return;

            Gizmos.color = Color.green;
            Gizmos.DrawLine(c, c + _lastLiftWorld * gizmoForceScale);
            Gizmos.color = Color.red;
            Gizmos.DrawLine(c, c + _lastDragWorld * gizmoForceScale);
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(c, c + _lastForceWorld * gizmoForceScale);
        }
    }
}
