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
            SetSibling(throttleText, 0);
            SetSibling(airspeedText, 1);
            SetSibling(machText, 2);
            SetSibling(altitudeText, 3);
            SetSibling(configurationText, 4);
            SetSibling(surfacesText, 5);
            Hide(anglesText);
            Hide(loadText);
            Hide(vitalityText);
            Hide(controlsText);
        }

        private void Update()
        {
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

            SetActive(throttleText, show);
            SetActive(airspeedText, show);
            SetActive(machText, show);
            SetActive(altitudeText, show);
            SetActive(configurationText, show);
            SetActive(surfacesText, show);
            Hide(anglesText);
            Hide(loadText);
            Hide(vitalityText);
            Hide(controlsText);
        }

        private void Rebuild(AircraftFlightController flight, PlaneRigidbody body)
        {
            AtmosphereSample atmo = AtmosphericModel.SampleAt(body.Position);
            float tas = body.TrueAirspeed;
            float ias = tas * Mathf.Sqrt(atmo.Density / AtmosphericModel.StandardSeaLevelDensity);
            float kmh = FlightSimMath.AirSpeedToKnots * FlightSimMath.KnotsToKmh;
            CountAmmo(flight, out int mg, out int cannon);

            Set(throttleText, "Throttle - %" + (flight.Throttle01 * 100f).ToString("F0"));
            Set(airspeedText, "IAS - " + (ias * kmh).ToString("F0") + "km/h");
            Set(machText, "TAS - " + (tas * kmh).ToString("F0") + "km/h");
            Set(altitudeText, "ALT - " + atmo.Altitude.ToString("F0") + "m");
            Set(configurationText, "MG - " + mg.ToString());
            Set(surfacesText, "CNN - " + cannon.ToString());
        }

        private static void CountAmmo(AircraftFlightController flight, out int mg, out int cannon)
        {
            mg = 0;
            cannon = 0;
            AircraftWeaponsController weapons = flight.GetComponent<AircraftWeaponsController>();
            if (!weapons || weapons.Guns == null)
                return;

            foreach (AircraftGun gun in weapons.Guns)
            {
                if (!gun || gun.AmmoCapacity <= 0)
                    continue;

                if (gun.TriggerChannel == GunTriggerChannel.Secondary)
                    cannon += gun.AmmoRemaining;
                else
                    mg += gun.AmmoRemaining;
            }
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

        private static void SetSibling(TMP_Text text, int index)
        {
            if (!text)
                return;
            text.transform.SetSiblingIndex(index);
        }

        private void Hide(TMP_Text text)
        {
            Set(text, "");
            SetActive(text, false);
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
