using System.Text;
using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.Weapons;
using TMPro;
using UnityEngine;

namespace Airplane.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/UI/Weapons Debug HUD")]
    public sealed class WeaponsDebugHud : MonoBehaviour
    {
        [Header("Panel")]
        [SerializeField] private GameObject panel;

        [Header("Readouts")]
        [SerializeField] private TMP_Text gunText;
        [SerializeField] private TMP_Text cannonText;
        [SerializeField] private TMP_Text controlsText;

        private readonly StringBuilder _builder = new StringBuilder(64);
        private float _hudClock = 1f;
        private bool _shown;
        private bool _hasShown;

        private void Start()
        {
            Set(controlsText, "LMB guns  LCtrl cannon");
        }

        private void Update()
        {
            // Assert that weapons is in fact initialized, because the compiler is stupid lol.
            AircraftWeaponsController weapons = null!;
            bool show = HudVisibility.Visible && TryResolve(out weapons);
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
            Rebuild(weapons);
        }

        private void ApplyShown(bool show)
        {
            if (panel && panel != gameObject)
            {
                if (panel.activeSelf != show)
                    panel.SetActive(show);
                return;
            }

            SetActive(gunText, show);
            SetActive(cannonText, show);
            SetActive(controlsText, show);
        }

        private void Rebuild(AircraftWeaponsController weapons)
        {
            AircraftGun[] guns = weapons.Guns;
            if (guns == null || guns.Length == 0)
            {
                Set(gunText, "no guns mounted");
                Set(cannonText, "");
                return;
            }

            int primaryRounds = 0;
            int secondaryRounds = 0;
            int primaryCap = 0;
            int secondaryCap = 0;
            bool primaryInf = false;
            bool secondaryInf = false;
            bool primaryFiring = false;
            bool secondaryFiring = false;

            foreach (AircraftGun gun in guns)
            {
                if (!gun)
                    continue;

                bool inf = gun.AmmoCapacity <= 0;
                if (gun.TriggerChannel == GunTriggerChannel.Secondary)
                {
                    secondaryFiring |= gun.IsFiring;
                    if (inf)
                        secondaryInf = true;
                    else
                    {
                        secondaryRounds += gun.AmmoRemaining;
                        secondaryCap += gun.AmmoCapacity;
                    }
                }
                else
                {
                    primaryFiring |= gun.IsFiring;
                    if (inf)
                        primaryInf = true;
                    else
                    {
                        primaryRounds += gun.AmmoRemaining;
                        primaryCap += gun.AmmoCapacity;
                    }
                }
            }

            Set(gunText, FormatLine("GUN  ", primaryInf, primaryRounds, primaryCap, primaryFiring));
            Set(cannonText, FormatLine("CAN  ", secondaryInf, secondaryRounds, secondaryCap, secondaryFiring));
        }

        private string FormatLine(string label, bool infinite, int remaining, int capacity, bool firing)
        {
            _builder.Length = 0;
            _builder.Append(label);
            if (infinite)
                _builder.Append('∞');
            else
                _builder.Append(remaining).Append('/').Append(capacity);
            if (firing)
                _builder.Append("  FIRING");
            return _builder.ToString();
        }

        private static bool TryResolve(out AircraftWeaponsController weapons)
        {
            weapons = null;

            NetworkedAircraft local = NetworkedAircraft.Local;
            if (local && local.IsAlive && local.Body)
            {
                weapons = local.GetComponent<AircraftWeaponsController>();
                if (weapons && weapons.InputEnabled)
                    return true;
                weapons = null;
            }

            Transform follow = AircraftChaseCamera.Active ? AircraftChaseCamera.Active.FollowTarget : null;
            if (!follow)
                return false;

            weapons = follow.GetComponentInParent<AircraftWeaponsController>();
            PlaneRigidbody body = follow.GetComponentInParent<PlaneRigidbody>();
            return weapons && weapons.InputEnabled && body && body.SimulationEnabled;
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
