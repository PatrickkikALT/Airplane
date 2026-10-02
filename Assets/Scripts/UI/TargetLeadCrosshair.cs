using System.Collections.Generic;
using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.Weapons;
using UnityEngine;
using UnityEngine.UI;

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

        [Header("UI")]
        [SerializeField] private RectTransform overlay;
        [SerializeField] private Image pipper;
        [SerializeField] private Image boresight;
        [SerializeField] private Image offscreenCaret;

        private static TargetLeadCrosshair _instance;

        private Camera _camera;
        private Canvas _canvas;
        private readonly PlaneRigidbody[] _bodyScratch = new PlaneRigidbody[32];
        private float _hitFlashUntil;
        private MarkerStyle _pipperStyle;
        private MarkerStyle _boreStyle;
        private MarkerStyle _caretStyle;

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

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            if (overlay)
                _canvas = overlay.GetComponentInParent<Canvas>();
            BuildMarkers();
            Show(pipper, false);
            Show(boresight, false);
            Show(offscreenCaret, false);
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void LateUpdate()
        {
            if (!Enabled || !HudVisibility.Visible || !overlay
                || !TryResolveShooter(out PlaneRigidbody shooter, out AircraftWeaponsController weapons)
                || !ResolveCamera())
            {
                HideMarkers();
                return;
            }

            GunTriggerChannel channel = weapons.FireSecondary01 > 0.5f
                ? GunTriggerChannel.Secondary
                : GunTriggerChannel.Primary;
            if (!HasAmmo(weapons.Guns, channel))
                channel = GunTriggerChannel.Primary;

            if (!TryAcquireTarget(shooter, weapons, channel, out Vector3 targetPosition, out Vector3 targetVelocity))
            {
                HideMarkers();
                return;
            }

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
                    out Vector3 boresightPoint,
                    out _,
                    out _,
                    out Vector3 aimPoint))
            {
                HideMarkers();
                return;
            }

            float errorDeg = Vector3.Angle(shotAxis, boresightPoint);
            Color leadTint = BlendHitFlash(errorDeg <= readyConeDeg ? readyColor : pipperColor);
            Color boreTint = BlendHitFlash(boresightColor);
            Color edgeTint = BlendHitFlash(offscreenColor);

            Vector3 boreWorld = muzzle + shotAxis * FlightSimMath.SafeMagnitude(aimPoint - muzzle);
            if (TryProject(boreWorld, out Vector2 boreScreen, out bool boreOnScreen) && boreOnScreen)
                Place(boresight, boreScreen, boreTint, 0f, _boreStyle);
            else
                Show(boresight, false);

            if (!TryProject(aimPoint, out Vector2 leadScreen, out bool leadOnScreen))
            {
                Show(pipper, false);
                Show(offscreenCaret, false);
                return;
            }

            if (leadOnScreen)
            {
                Place(pipper, leadScreen, leadTint, 0f, _pipperStyle);
                Show(offscreenCaret, false);
            }
            else
            {
                Show(pipper, false);
                Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
                Vector2 dir = leadScreen - center;
                float ang = dir.sqrMagnitude < 1e-4f ? 0f : Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90f;
                Place(offscreenCaret, leadScreen, edgeTint, ang, _caretStyle);
            }
        }

        private void BuildMarkers()
        {
            float radius = pipperSize > 1f ? pipperSize : 14f;
            const float pipperStroke = 1.6f;
            const float tickOut = 5f;
            const float tickIn = 1f;
            float pipperExtent = radius + tickOut + pipperStroke;
            int pipperPx = Mathf.CeilToInt(pipperExtent * 2f);
            Vector2 pipperCenter = new Vector2(pipperPx * 0.5f, pipperPx * 0.5f);
            _pipperStyle = Bake(pipperPx, pipperPx, pipperCenter, p =>
            {
                float ring = Band(Mathf.Abs(Vector2.Distance(p, pipperCenter) - radius), pipperStroke * 0.5f);
                float ticks = Segment(p, pipperCenter + new Vector2(0f, -(radius - tickIn)), pipperCenter + new Vector2(0f, -(radius + tickOut)), pipperStroke * 0.5f);
                ticks = Mathf.Max(ticks, Segment(p, pipperCenter + new Vector2(0f, radius - tickIn), pipperCenter + new Vector2(0f, radius + tickOut), pipperStroke * 0.5f));
                ticks = Mathf.Max(ticks, Segment(p, pipperCenter + new Vector2(-(radius - tickIn), 0f), pipperCenter + new Vector2(-(radius + tickOut), 0f), pipperStroke * 0.5f));
                ticks = Mathf.Max(ticks, Segment(p, pipperCenter + new Vector2(radius - tickIn, 0f), pipperCenter + new Vector2(radius + tickOut, 0f), pipperStroke * 0.5f));
                return Mathf.Max(ring, ticks);
            });

            float arm = boresightSize > 1f ? boresightSize : 8f;
            const float crossStroke = 5f;
            float boreExtent = arm + crossStroke;
            int borePx = Mathf.CeilToInt(boreExtent * 2f);
            Vector2 boreCenter = new Vector2(borePx * 0.5f, borePx * 0.5f);
            _boreStyle = Bake(borePx, borePx, boreCenter, p =>
            {
                float horizontal = Segment(p, boreCenter + new Vector2(-arm, 0f), boreCenter + new Vector2(arm, 0f), crossStroke * 0.5f);
                float vertical = Segment(p, boreCenter + new Vector2(0f, -arm), boreCenter + new Vector2(0f, arm), crossStroke * 0.5f);
                return Mathf.Max(horizontal, vertical);
            });

            const float caretLen = 16f;
            const float caretHalf = 8f;
            const float caretStroke = 1.8f;
            int caretW = Mathf.CeilToInt(caretHalf * 2f + caretStroke + 2f);
            int caretH = Mathf.CeilToInt(caretLen + caretStroke + 2f);
            Vector2 tip = new Vector2(caretW * 0.5f, caretH - 1f - caretStroke * 0.5f);
            Vector2 baseLeft = tip + new Vector2(-caretHalf, -caretLen);
            Vector2 baseRight = tip + new Vector2(caretHalf, -caretLen);
            _caretStyle = Bake(caretW, caretH, tip, p =>
            {
                float a = Segment(p, tip, baseLeft, caretStroke * 0.5f);
                float b = Segment(p, tip, baseRight, caretStroke * 0.5f);
                float c = Segment(p, baseLeft, baseRight, caretStroke * 0.5f);
                return Mathf.Max(a, Mathf.Max(b, c));
            });

            Assign(_pipperStyle, pipper);
            Assign(_boreStyle, boresight);
            Assign(_caretStyle, offscreenCaret);
        }

        private static void Assign(MarkerStyle style, Image marker)
        {
            if (!marker || !style.Sprite)
                return;
            marker.sprite = style.Sprite;
            marker.type = Image.Type.Simple;
            marker.preserveAspect = false;
            marker.raycastTarget = false;
            marker.rectTransform.pivot = style.Pivot;
        }

        private static MarkerStyle Bake(int width, int height, Vector2 pivotPixels, System.Func<Vector2, float> coverage)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.HideAndDontSave;
            tex.filterMode = FilterMode.Bilinear;
            tex.wrapMode = TextureWrapMode.Clamp;
            Color32[] pixels = new Color32[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float a = coverage(new Vector2(x + 0.5f, y + 0.5f));
                    byte alpha = (byte)Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(a) * 255f), 0, 255);
                    pixels[y * width + x] = new Color32(255, 255, 255, alpha);
                }
            }

            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            Vector2 pivot = new Vector2(pivotPixels.x / width, pivotPixels.y / height);
            Sprite sprite = Sprite.Create(tex, new Rect(0f, 0f, width, height), pivot, 100f);
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return new MarkerStyle { Sprite = sprite, Pivot = pivot };
        }

        private static float Band(float distance, float halfWidth)
        {
            return Mathf.Clamp01(halfWidth + 0.65f - distance);
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float halfWidth)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 < 1e-6f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2);
            return Band(Vector2.Distance(p, a + ab * t), halfWidth);
        }

        private void HideMarkers()
        {
            Show(pipper, false);
            Show(boresight, false);
            Show(offscreenCaret, false);
        }

        private void Place(Image marker, Vector2 screen, Color color, float zRotation, MarkerStyle style)
        {
            if (!marker)
                return;
            Show(marker, true);
            marker.color = color;
            if (style.Sprite)
            {
                float scale = _canvas && _canvas.scaleFactor > 0.01f ? _canvas.scaleFactor : 1f;
                Texture2D tex = style.Sprite.texture;
                marker.rectTransform.pivot = style.Pivot;
                marker.rectTransform.sizeDelta = new Vector2(tex.width / scale, tex.height / scale);
            }

            HudVisibility.Place(overlay, marker.rectTransform, screen, _canvas);
            marker.rectTransform.localEulerAngles = new Vector3(0f, 0f, zRotation);
        }

        private struct MarkerStyle
        {
            public Sprite Sprite;
            public Vector2 Pivot;
        }

        private void Show(Image marker, bool visible)
        {
            if (!marker || marker.gameObject == gameObject)
                return;
            if (marker.gameObject.activeSelf != visible)
                marker.gameObject.SetActive(visible);
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
            Vector3 axis = shooter.TransformDirection(Vector3.forward);
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

        private bool TryProject(Vector3 world, out Vector2 screen, out bool onScreen)
        {
            screen = Vector2.zero;
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

            onScreen = !behind && sp.x >= 0f && sp.x <= w && sp.y >= 0f && sp.y <= h;
            if (!onScreen)
            {
                Vector2 center = new Vector2(w * 0.5f, h * 0.5f);
                Vector2 dir = new Vector2(sp.x, sp.y) - center;
                if (dir.sqrMagnitude < 1e-4f)
                    dir = Vector2.up;
                dir.Normalize();
                float pad = 28f;
                float hx = (w * 0.5f) - pad;
                float hy = (h * 0.5f) - pad;
                float sx = Mathf.Abs(dir.x) > 1e-4f ? hx / Mathf.Abs(dir.x) : float.PositiveInfinity;
                float sy = Mathf.Abs(dir.y) > 1e-4f ? hy / Mathf.Abs(dir.y) : float.PositiveInfinity;
                screen = center + dir * Mathf.Min(sx, sy);
                return true;
            }

            screen = new Vector2(sp.x, sp.y);
            return true;
        }
    }
}
