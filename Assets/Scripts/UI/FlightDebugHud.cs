using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.Weapons;
using TMPro;
using UnityEngine;

namespace Airplane.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/UI/Flight Debug HUD")]
    public sealed class FlightDebugHud : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panel;

        [Header("Readouts")]
        [SerializeField] private TMP_Text altitudeText;
        [SerializeField] private TMP_Text airspeedText;
        [SerializeField] private TMP_Text machText;
        [SerializeField] private TMP_Text anglesText;
        [SerializeField] private TMP_Text loadText;
        [SerializeField] private TMP_Text throttleText;
        [SerializeField] private TMP_Text configurationText;
        [SerializeField] private TMP_Text surfacesText;
        [SerializeField] private TMP_Text vitalityText;
        [SerializeField] private TMP_Text controlsText;

        private float _hudClock = 1f;
        private bool _shown;
        private bool _hasShown;

        private void Start()
        {
            Set(controlsText, "W/S pitch  A/D roll  Q/E yaw\nR/F throttle  X/Z flaps  Shift airbrake  Space wheel");
        }

        private void Update()
        {
            // Assert to the compiler that flight & body is infact initialized, because the compiler is stupid.
            AircraftFlightController flight = null!;
            PlaneRigidbody body = null!;
            bool show = HudVisibility.Visible && TryResolve(out flight, out body);
            if (!_hasShown || show != _shown)
            {
                _hasShown = true;
                _shown = show;
                ApplyShown(show);
                _hudClock = 1f;
            }

            if (!show)
                return;

            _hudClock += Time.unscaledDeltaTime;
            if (_hudClock < 0.2f)
                return;

            _hudClock = 0f;
            Rebuild(flight, body);
        }

        private void ApplyShown(bool show)
        {
            if (panel && panel != gameObject)
            {
                if (panel.activeSelf != show)
                    panel.SetActive(show);
                return;
            }

            SetActive(altitudeText, show);
            SetActive(airspeedText, show);
            SetActive(machText, show);
            SetActive(anglesText, show);
            SetActive(loadText, show);
            SetActive(throttleText, show);
            SetActive(configurationText, show);
            SetActive(surfacesText, show);
            SetActive(vitalityText, show);
            SetActive(controlsText, show);
        }

        private void Rebuild(AircraftFlightController flight, PlaneRigidbody body)
        {
            AtmosphereSample atmo = AtmosphericModel.SampleAt(body.Position);
            float tas = body.TrueAirspeed;
            Vector3 vBody = body.InverseTransformDirection(body.Velocity - AtmosphericModel.SampleWind());
            float aoa = FlightSimMath.BodyAngleOfAttack(vBody) * FlightSimMath.Rad2Deg;
            float beta = FlightSimMath.BodySideslip(vBody) * FlightSimMath.Rad2Deg;
            float ias = tas * Mathf.Sqrt(atmo.Density / AtmosphericModel.StandardSeaLevelDensity);
            float mach = atmo.SpeedOfSound > 1f ? tas / atmo.SpeedOfSound : 0f;
            float knotsToKmh = FlightSimMath.AirSpeedToKnots * FlightSimMath.KnotsToKmh;
            AircraftEngine engine = flight.Engine;
            AircraftVitality vitality = flight.GetComponent<AircraftVitality>();

            Set(altitudeText, "ALT  " + atmo.Altitude.ToString("F0") + " m");
            Set(airspeedText,
                "TAS  " + (tas * knotsToKmh).ToString("F0") + " km/u   IAS " + (ias * knotsToKmh).ToString("F0") + " km/u");
            Set(machText, "M    " + mach.ToString("F2") + "    q " + atmo.DynamicPressure(tas).ToString("F0") + " Pa");
            Set(anglesText, "AoA  " + aoa.ToString("F1") + "°    β " + beta.ToString("F1") + "°");
            Set(loadText,
                "G    " + body.LoadFactorNz.ToString("F2") + "    TRIM " + flight.ElevatorTrim.ToString("F2")
                + "    ρ " + atmo.Density.ToString("F3") + " kg/m³");
            Set(throttleText,
                "THR  " + (flight.Throttle01 * 100f).ToString("F0") + "%   T "
                + (engine != null ? engine.LastThrust.ToString("F0") : "0") + " N");
            Set(configurationText,
                "FLP  " + (flight.Flaps01 * 100f).ToString("F0") + "%   BRK "
                + (flight.Airbrake01 * 100f).ToString("F0") + "%   WHL "
                + (flight.WheelBrake01 * 100f).ToString("F0") + "%");
            Set(surfacesText,
                "A/E/R " + flight.Aileron01.ToString("F2") + "  "
                + flight.Elevator01.ToString("F2") + "  " + flight.Rudder01.ToString("F2"));
            Set(vitalityText, vitality ? "Vitality   " + vitality.HitPoints.ToString("F0") : "");
        }

        private static bool TryResolve(out AircraftFlightController flight, out PlaneRigidbody body)
        {
            flight = null;
            body = null;

            NetworkedAircraft local = NetworkedAircraft.Local;
            if (local && local.IsAlive && local.Body)
            {
                flight = local.GetComponent<AircraftFlightController>();
                if (flight && flight.InputEnabled)
                {
                    body = local.Body;
                    return true;
                }
            }

            Transform follow = AircraftChaseCamera.Active ? AircraftChaseCamera.Active.FollowTarget : null;
            if (!follow)
                return false;

            flight = follow.GetComponentInParent<AircraftFlightController>();
            body = follow.GetComponentInParent<PlaneRigidbody>();
            return flight && body && flight.InputEnabled && body.SimulationEnabled;
        }

        private static void Set(TMP_Text text, string value)
        {
            if (!text || text.text == value)
                return;
            text.text = value;
        }

        private void SetActive(TMP_Text text, bool visible)
        {
            if (!text || text.gameObject == gameObject)
                return;
            if (text.gameObject.activeSelf != visible)
                text.gameObject.SetActive(visible);
        }
    }
}
