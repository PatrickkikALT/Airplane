using System.Collections.Generic;
using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.Weapons;
using UnityEngine;

namespace Airplane.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/UI/Target Lead Crosshair")]
    public sealed class TargetLeadCrosshair : MonoBehaviour
    {
        [SerializeField] private float maxDistance = 2500f;
        [SerializeField] private float acquireConeDeg = 18f;
        [SerializeField] private float readyConeDeg = 1.35f;
        [SerializeField] private float pipperSize = 14f;
        [SerializeField] private float boresightSize = 8f;
        [SerializeField] private Color pipperColor = new Color(1f, 0.82f, 0.28f, 0.95f);
        [SerializeField] private Color readyColor = new Color(0.45f, 1f, 0.55f, 0.95f);
        [SerializeField] private Color boresightColor = new Color(0.2f, 0.9f, 0.3f, 0.55f);
        [SerializeField] private Color offscreenColor = new Color(1f, 0.82f, 0.28f, 0.75f);
        [SerializeField] private Color hitColor = new Color(1f, 0.12f, 0.1f, 1f);
        [SerializeField] private float hitFlashSeconds = 0.45f;

        private static TargetLeadCrosshair _instance;
        private static Texture2D _pixel;

        private Camera _camera;
        private readonly PlaneRigidbody[] _bodyScratch = new PlaneRigidbody[32];
        private float _hitFlashUntil;

        public static bool Enabled { get; set; } = true;

        public static void NotifyShooterHit(PlaneRigidbody shooter)
        {
            if (_instance == null || !shooter)
                return;
            if (!IsLocalPlayerShooter(shooter))
                return;
            _instance._hitFlashUntil = Time.unscaledTime + _instance.hitFlashSeconds;
        }

        private static bool IsLocalPlayerShooter(PlaneRigidbody shooter)
        {
            NetworkedAircraft local = NetworkedAircraft.Local;
            if (local)
                return local.Body == shooter;
            AircraftWeaponsController weapons = shooter.GetComponent<AircraftWeaponsController>();
            return weapons && weapons.InputEnabled;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            GameObject host = new GameObject("Target Lead Crosshair");
            DontDestroyOnLoad(host);
            host.AddComponent<TargetLeadCrosshair>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void OnGUI()
        {
            if (!Enabled || !HudVisibility.Visible)
                return;
            if (!TryResolveShooter(out PlaneRigidbody shooter, out AircraftWeaponsController weapons))
                return;
            if (!ResolveCamera())
                return;

            GunTriggerChannel channel = weapons.FireSecondary01 > 0.5f
                ? GunTriggerChannel.Secondary
                : GunTriggerChannel.Primary;
            if (!HasAmmo(weapons.Guns, channel))
                channel = GunTriggerChannel.Primary;

            if (!TryAcquireTarget(shooter, weapons, channel, out Vector3 targetPosition, out Vector3 targetVelocity))
                return;

            if (!GunLeadSolution.TrySolveBattery(
                    weapons.Guns,
                    channel,
                    shooter,
                    targetPosition,
                    targetVelocity,
                    out Vector3 muzzle,
                    out Vector3 shotAxis,
                    out _,
                    out _,
                    out _,
                    out Vector3 boresight,
                    out _,
                    out _,
                    out Vector3 aimPoint))
            {
                return;
            }

            float errorDeg = Vector3.Angle(shotAxis, boresight);
            Color leadTint = BlendHitFlash(errorDeg <= readyConeDeg ? readyColor : pipperColor);
            Color boreTint = BlendHitFlash(boresightColor);
            Color edgeTint = BlendHitFlash(offscreenColor);

            Vector3 boreWorld = muzzle + shotAxis * FlightSimMath.SafeMagnitude(aimPoint - muzzle);
            if (TryProject(boreWorld, out Vector2 boreGui, out bool boreOnScreen) && boreOnScreen)
                DrawCross(boreGui, boresightSize, boreTint);

            if (!TryProject(aimPoint, out Vector2 leadGui, out bool leadOnScreen))
                return;

            if (leadOnScreen)
                DrawPipper(leadGui, pipperSize, leadTint);
            else
                DrawOffscreenCaret(leadGui, edgeTint);
        }

        private bool TryAcquireTarget(
            PlaneRigidbody shooter,
            AircraftWeaponsController weapons,
            GunTriggerChannel channel,
            out Vector3 position,
            out Vector3 velocity)
        {
            position = Vector3.zero;
            velocity = Vector3.zero;

            Vector3 muzzle = shooter.Position;
            Vector3 axis = shooter.TransformDirection(Vector3.right);
            if (TryShotAxis(weapons.Guns, channel, shooter, out Vector3 batteryMuzzle, out Vector3 batteryAxis))
            {
                muzzle = batteryMuzzle;
                axis = batteryAxis;
            }

            PlaneRigidbody bestBody = null;
            float bestScore = float.NegativeInfinity;
            float coneCos = Mathf.Cos(acquireConeDeg * Mathf.Deg2Rad);

            IReadOnlyList<NetworkedAircraft> all = NetworkedAircraft.All;
            if (all != null)
            {
                NetworkedAircraft local = NetworkedAircraft.Local;
                for (int i = 0; i < all.Count; i++)
                {
                    NetworkedAircraft other = all[i];
                    if (!other || other == local || !other.IsSpawned || !other.IsAlive || other.Body == null)
                        continue;
                    if (other.Body == shooter)
                        continue;
                    ScoreCandidate(muzzle, axis, coneCos, other.Body, ref bestBody, ref bestScore);
                }
            }

            if (bestBody == null)
            {
                int n = GatherBodies();
                for (int i = 0; i < n; i++)
                {
                    PlaneRigidbody body = _bodyScratch[i];
                    if (!body || body == shooter)
                        continue;
                    if (body.GetComponent<NetworkedAircraft>())
                        continue;
                    ScoreCandidate(muzzle, axis, coneCos, body, ref bestBody, ref bestScore);
                }
            }

            if (!bestBody)
                return false;

            NetworkedAircraft networked = bestBody.GetComponent<NetworkedAircraft>();
            if (networked && networked.TryGetFireControlKinematics(out position, out velocity))
                return true;

            position = bestBody.Position;
            velocity = bestBody.Velocity;
            return true;
        }

        private int GatherBodies()
        {
            PlaneRigidbody[] found = FindObjectsByType<PlaneRigidbody>();
            int n = Mathf.Min(found.Length, _bodyScratch.Length);
            for (int i = 0; i < n; i++)
                _bodyScratch[i] = found[i];
            return n;
        }

        private void ScoreCandidate(
            Vector3 muzzle,
            Vector3 axis,
            float coneCos,
            PlaneRigidbody body,
            ref PlaneRigidbody bestBody,
            ref float bestScore)
        {
            Vector3 to = body.Position - muzzle;
            float dist = FlightSimMath.SafeMagnitude(to);
            if (dist < 8f || dist > maxDistance)
                return;

            float align = Vector3.Dot(axis, to / dist);
            if (align < coneCos)
                return;

            float score = align * 3f - dist / maxDistance;
            if (score <= bestScore)
                return;

            bestScore = score;
            bestBody = body;
        }

        private static bool TryShotAxis(
            AircraftGun[] guns,
            GunTriggerChannel channel,
            PlaneRigidbody shooter,
            out Vector3 muzzle,
            out Vector3 axis)
        {
            muzzle = Vector3.zero;
            axis = Vector3.zero;
            if (guns == null)
                return false;

            Vector3 p = Vector3.zero;
            Vector3 a = Vector3.zero;
            int count = 0;
            for (int i = 0; i < guns.Length; i++)
            {
                AircraftGun gun = guns[i];
                if (!gun || !gun.isActiveAndEnabled || gun.TriggerChannel != channel)
                    continue;
                if (gun.AmmoRemaining == 0)
                    continue;
                gun.GetMuzzleWorld(shooter, out Vector3 origin, out Vector3 shot);
                p += origin;
                a += shot;
                count++;
            }

            if (count == 0 || a.sqrMagnitude < 1e-6f)
                return false;

            muzzle = p / count;
            axis = a.normalized;
            return true;
        }

        private static bool HasAmmo(AircraftGun[] guns, GunTriggerChannel channel)
        {
            if (guns == null)
                return false;
            for (int i = 0; i < guns.Length; i++)
            {
                AircraftGun gun = guns[i];
                if (!gun || !gun.isActiveAndEnabled || gun.TriggerChannel != channel)
                    continue;
                if (gun.AmmoRemaining != 0)
                    return true;
            }

            return false;
        }

        private static bool TryResolveShooter(out PlaneRigidbody shooter, out AircraftWeaponsController weapons)
        {
            shooter = null;
            weapons = null;

            NetworkedAircraft local = NetworkedAircraft.Local;
            if (local && local.IsAlive && local.Body)
            {
                shooter = local.Body;
                weapons = shooter.GetComponent<AircraftWeaponsController>();
                if (weapons && weapons.InputEnabled)
                    return true;
                shooter = null;
                weapons = null;
            }

            Transform follow = AircraftChaseCamera.Active ? AircraftChaseCamera.Active.FollowTarget : null;
            if (follow)
            {
                shooter = follow.GetComponentInParent<PlaneRigidbody>();
                weapons = follow.GetComponentInParent<AircraftWeaponsController>();
                if (shooter && weapons && shooter.SimulationEnabled)
                    return true;
            }

            return false;
        }

        private Color BlendHitFlash(Color rest)
        {
            float remaining = _hitFlashUntil - Time.unscaledTime;
            if (remaining <= 0f)
                return rest;

            float fade = 1f - remaining / Mathf.Max(0.01f, hitFlashSeconds);
            fade = fade * fade;
            return Color.Lerp(hitColor, rest, fade);
        }

        private bool ResolveCamera()
        {
            if (_camera && _camera.isActiveAndEnabled)
                return true;
            _camera = Camera.main;
            return _camera;
        }

        private bool TryProject(Vector3 world, out Vector2 gui, out bool onScreen)
        {
            gui = Vector2.zero;
            onScreen = false;
            if (!_camera)
                return false;

            Vector3 sp = _camera.WorldToScreenPoint(world);
            float w = Screen.width;
            float h = Screen.height;
            Vector3 to = world - _camera.transform.position;
            bool behind = Vector3.Dot(to, _camera.transform.forward) <= 0f || sp.z <= 0f;
            if (behind)
            {
                sp.x = w - sp.x;
                sp.y = h - sp.y;
            }

            float x = sp.x;
            float y = h - sp.y;
            onScreen = !behind && x >= 0f && x <= w && y >= 0f && y <= h;
            if (!onScreen)
            {
                Vector2 center = new Vector2(w * 0.5f, h * 0.5f);
                Vector2 dir = new Vector2(x, y) - center;
                if (dir.sqrMagnitude < 1e-4f)
                    dir = Vector2.up;
                dir.Normalize();
                float pad = 28f;
                float hx = (w * 0.5f) - pad;
                float hy = (h * 0.5f) - pad;
                float sx = Mathf.Abs(dir.x) > 1e-4f ? hx / Mathf.Abs(dir.x) : float.PositiveInfinity;
                float sy = Mathf.Abs(dir.y) > 1e-4f ? hy / Mathf.Abs(dir.y) : float.PositiveInfinity;
                gui = center + dir * Mathf.Min(sx, sy);
                return true;
            }

            gui = new Vector2(x, y);
            return true;
        }

        private static void DrawPipper(Vector2 center, float size, Color color)
        {
            float r = size;
            DrawCircle(center, r, color, 1.6f, 28);
            DrawLine(new Vector2(center.x, center.y - r - 5f), new Vector2(center.x, center.y - r + 1f), 1.6f, color);
            DrawLine(new Vector2(center.x, center.y + r - 1f), new Vector2(center.x, center.y + r + 5f), 1.6f, color);
            DrawLine(new Vector2(center.x - r - 5f, center.y), new Vector2(center.x - r + 1f, center.y), 1.6f, color);
            DrawLine(new Vector2(center.x + r - 1f, center.y), new Vector2(center.x + r + 5f, center.y), 1.6f, color);
        }

        private static void DrawCross(Vector2 center, float size, Color color)
        {
            DrawLine(new Vector2(center.x - size, center.y), new Vector2(center.x + size, center.y), 5, color);
            DrawLine(new Vector2(center.x, center.y - size), new Vector2(center.x, center.y + size), 5, color);
        }

        private static void DrawOffscreenCaret(Vector2 tip, Color color)
        {
            Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            Vector2 dir = tip - center;
            if (dir.sqrMagnitude < 1e-4f)
                dir = Vector2.up;
            dir.Normalize();
            Vector2 n = new Vector2(-dir.y, dir.x);
            Vector2 a = tip;
            Vector2 b = tip - dir * 16f + n * 8f;
            Vector2 c = tip - dir * 16f - n * 8f;
            DrawLine(a, b, 1.8f, color);
            DrawLine(a, c, 1.8f, color);
            DrawLine(b, c, 1.8f, color);
        }

        private static void DrawCircle(Vector2 center, float radius, Color color, float width, int segments)
        {
            float step = Mathf.PI * 2f / segments;
            Vector2 prev = center + new Vector2(radius, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float ang = i * step;
                Vector2 next = center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * radius;
                DrawLine(prev, next, width, color);
                prev = next;
            }
        }

        private static void DrawLine(Vector2 a, Vector2 b, float width, Color color)
        {
            EnsurePixel();
            Vector2 d = b - a;
            float len = d.magnitude;
            if (len < 0.4f)
                return;

            Matrix4x4 matrix = GUI.matrix;
            Color prev = GUI.color;
            GUI.color = color;
            float ang = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;
            GUIUtility.RotateAroundPivot(ang, a);
            GUI.DrawTexture(new Rect(a.x, a.y - width * 0.5f, len, width), _pixel);
            GUI.matrix = matrix;
            GUI.color = prev;
        }

        private static void EnsurePixel()
        {
            if (_pixel)
                return;
            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
            _pixel.hideFlags = HideFlags.HideAndDontSave;
        }
    }
}
