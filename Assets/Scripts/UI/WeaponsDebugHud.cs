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

        private float _hudClock = 1f;
        private bool _shown;
        private bool _hasShown;

        private void Start()
        {
            Hide(controlsText);
        }

        private void Update()
        {
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
            }
            else
            {
                SetActive(gunText, show);
                SetActive(cannonText, show);
            }

            Hide(controlsText);
        }

        private void Rebuild(AircraftWeaponsController weapons)
        {
            CountAmmo(weapons, out int mg, out int cannon);
            Set(gunText, "MG - " + mg.ToString());
            Set(cannonText, "CNN - " + cannon.ToString());
        }

        private static void CountAmmo(AircraftWeaponsController weapons, out int mg, out int cannon)
        {
            mg = 0;
            cannon = 0;
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
