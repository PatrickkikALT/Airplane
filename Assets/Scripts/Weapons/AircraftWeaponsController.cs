using System.Text;
using Airplane.FlightSimulation;
using Airplane.UI;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Airplane.Weapons
{
    /// <summary>
    /// Receives fire triggers from PlayerInput and fans them out to every child
    /// <see cref="AircraftGun"/>. Recoil is applied through <see cref="PlaneRigidbody"/>.
    ///
    /// Input arrives from PlayerInput (Unity Events / Send Messages) via the On* CallbackContext
    /// methods. This class never enables, disables, or resolves InputActions.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    [RequireComponent(typeof(PlaneRigidbody))]
    [AddComponentMenu("Airplane/Weapons/Aircraft Weapons Controller")]
    public sealed class AircraftWeaponsController : MonoBehaviour
    {
        [Header("Debug HUD")]
        [SerializeField] private bool drawHud = true;
        [SerializeField] private Vector2 hudPosition = new Vector2(16f, 236f);

        private PlaneRigidbody _body;
        private AircraftGun[] _guns = System.Array.Empty<AircraftGun>();

        private float _fire01;
        private float _fireSecondary01;
        private bool _inputEnabled = true;
        private readonly StringBuilder _hudBuilder = new StringBuilder(256);
        private string _hudText = "";
        private float _hudClock;

        public float Fire01 => _fire01;
        public float FireSecondary01 => _fireSecondary01;

        /// <summary>False when the trigger positions are being written by something else.</summary>
        public bool InputEnabled => _inputEnabled;

        public AircraftGun[] Guns => _guns;

        /// <summary>
        /// Disables the input path so <see cref="ApplyExternalFire"/> becomes the only writer.
        /// Used for aircraft flown by a remote peer, where the trigger arrives over the wire.
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
        /// Writes trigger positions from an outside source. No edge detection is applied: a remote
        /// peer is showing the held state, and the guns' own rate-of-fire clocks do the rest.
        /// </summary>
        public void ApplyExternalFire(float firePrimary, float fireSecondary)
        {
            _fire01 = FlightSimMath.Saturate(firePrimary);
            _fireSecondary01 = FlightSimMath.Saturate(fireSecondary);
        }

        public float ReadTrigger(GunTriggerChannel channel)
        {
            if (CheatFlags.BlockPlayerInput && _inputEnabled)
                return 0f;
            return channel == GunTriggerChannel.Secondary ? _fireSecondary01 : _fire01;
        }

        public void OnFire(InputAction.CallbackContext context)
        {
            if (_inputEnabled && !CheatFlags.BlockPlayerInput)
                _fire01 = FlightSimMath.Saturate(ReadAxis(context));
        }

        public void OnFireSecondary(InputAction.CallbackContext context)
        {
            if (_inputEnabled && !CheatFlags.BlockPlayerInput)
                _fireSecondary01 = FlightSimMath.Saturate(ReadAxis(context));
        }

        private void Awake()
        {
            _body = GetComponent<PlaneRigidbody>();
            CacheGuns();
        }

        private void OnEnable()
        {
            CacheGuns();
        }

        private void CacheGuns()
        {
            _guns = GetComponentsInChildren<AircraftGun>(true);
        }

        /// <summary>
        /// Called by <see cref="PlaneRigidbody"/> once per FixedUpdate, before sub-steps.
        /// Owner path: lets guns fire with recoil from the last PlayerInput trigger values.
        /// </summary>
        public void PrePhysicsTick(float dt)
        {
            TickGuns(dt, visualOnly: false);
        }

        /// <summary>
        /// Visual-only tick for a remote proxy whose solver is off. Recoil and hit authority stay
        /// with the owning peer; this only keeps tracers and muzzle flashes in sync with the
        /// replicated trigger.
        /// </summary>
        public void TickVisual(float dt)
        {
            TickGuns(dt, visualOnly: true);
        }

        private void LateUpdate()
        {
            // Remotes write the trigger from interpolated snapshots in Update. Tick afterwards
            // so tracers follow this frame's packet instead of last frame's.
            if (_inputEnabled)
                return;
            if (!_body || _body.SimulationEnabled)
                return;

            TickVisual(Time.deltaTime);
        }

        private void TickGuns(float dt, bool visualOnly)
        {
            if (_guns == null)
                CacheGuns();

            for (int i = 0; i < _guns.Length; i++)
            {
                AircraftGun gun = _guns[i];
                if (!gun || !gun.isActiveAndEnabled)
                    continue;
                gun.Tick(this, _body, dt, visualOnly);
            }
        }

        private static float ReadAxis(InputAction.CallbackContext context)
        {
            if (context.canceled)
                return 0f;
            return context.ReadValue<float>();
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
            GUI.Box(new Rect(x, y, 320f, 88f), "Guns");
            GUI.Label(new Rect(x + 10f, y + 24f, 300f, 58f), _hudText);
        }

        private void RebuildHudText()
        {
            _hudBuilder.Length = 0;
            if (_guns == null || _guns.Length == 0)
            {
                _hudBuilder.Append("no guns mounted");
                _hudText = _hudBuilder.ToString();
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

            for (int i = 0; i < _guns.Length; i++)
            {
                AircraftGun gun = _guns[i];
                if (!gun)
                    continue;

                bool inf = gun.AmmoCapacity <= 0;
                if (gun.TriggerChannel == GunTriggerChannel.Secondary)
                {
                    secondaryFiring |= gun.IsFiring;
                    if (inf) secondaryInf = true;
                    else
                    {
                        secondaryRounds += gun.AmmoRemaining;
                        secondaryCap += gun.AmmoCapacity;
                    }
                }
                else
                {
                    primaryFiring |= gun.IsFiring;
                    if (inf) primaryInf = true;
                    else
                    {
                        primaryRounds += gun.AmmoRemaining;
                        primaryCap += gun.AmmoCapacity;
                    }
                }
            }

            _hudBuilder.Append("GUN  ");
            AppendAmmo(_hudBuilder, primaryInf, primaryRounds, primaryCap);
            if (primaryFiring)
                _hudBuilder.Append("  FIRING");
            _hudBuilder.Append('\n');

            _hudBuilder.Append("CAN  ");
            AppendAmmo(_hudBuilder, secondaryInf, secondaryRounds, secondaryCap);
            if (secondaryFiring)
                _hudBuilder.Append("  FIRING");
            _hudBuilder.Append('\n');
            _hudBuilder.Append("LMB guns  LCtrl cannon");
            _hudText = _hudBuilder.ToString();
        }

        private static void AppendAmmo(StringBuilder builder, bool infinite, int remaining, int capacity)
        {
            if (infinite)
            {
                builder.Append("∞");
                return;
            }

            builder.Append(remaining).Append('/').Append(capacity);
        }
    }
}
