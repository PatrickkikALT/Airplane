using System.Collections.Generic;
using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using TMPro;
using UnityEngine;

namespace Airplane.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/UI/Aircraft Nametag Overlay")]
    public sealed class AircraftNametagOverlay : MonoBehaviour
    {
        [Header("Range")]
        [Tooltip("Furthest a nametag is drawn, metres.")]
        [SerializeField] private float maxDistance = 4000f;

        [Tooltip("Distance at which a nametag starts fading out, metres.")]
        [SerializeField] private float fadeStartDistance = 2500f;

        [Header("Layout")]
        [Tooltip("Metres above the aircraft the tag floats, so it does not sit on top of the airframe.")]
        [SerializeField] private float worldOffset = 9f;

        [SerializeField] private int minFontSize = 10;
        [SerializeField] private int maxFontSize = 17;

        [Header("Appearance")]
        [SerializeField] private Color playerColor = new Color(0.75f, 0.9f, 1f, 1f);
        [SerializeField] private Color botColor = new Color(1f, 0.82f, 0.55f, 1f);

        [Tooltip("Show slant range under the name.")]
        [SerializeField] private bool showDistance = true;

        [Header("Visibility")]
        [Tooltip("Hide a nametag when terrain or a building is in the way.")]
        [SerializeField] private bool occlusionTest = true;

        [SerializeField] private LayerMask occluderMask = ~0;

        [Header("UI")]
        [SerializeField] private RectTransform overlay;
        [SerializeField] private TMP_Text nametagTemplate;

        private static AircraftNametagOverlay _instance;

        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private readonly List<TMP_Text> _pool = new List<TMP_Text>(8);
        private Camera _camera;
        private Canvas _canvas;
        private int _used;

        public static bool Enabled { get; set; } = true;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            if (nametagTemplate && nametagTemplate.gameObject != gameObject)
                nametagTemplate.gameObject.SetActive(false);
            if (overlay)
                _canvas = overlay.GetComponentInParent<Canvas>();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void LateUpdate()
        {
            _used = 0;
            bool show = Enabled && HudVisibility.Visible && nametagTemplate && overlay;
            if (show && ResolveCamera())
                DrawAll();

            for (int i = _used; i < _pool.Count; i++)
            {
                TMP_Text extra = _pool[i];
                if (extra && extra.gameObject.activeSelf)
                    extra.gameObject.SetActive(false);
            }
        }

        private void DrawAll()
        {
            var aircraft = NetworkedAircraft.All;
            if (aircraft.Count == 0)
                return;

            Transform cameraTransform = _camera.transform;
            Vector3 eye = cameraTransform.position;
            Vector3 forward = cameraTransform.forward;
            NetworkedAircraft local = NetworkedAircraft.Local;

            for (int i = 0; i < aircraft.Count; i++)
            {
                NetworkedAircraft target = aircraft[i];
                if (!target || target == local || !target.IsSpawned || !target.IsAlive)
                    continue;

                PlaneRigidbody body = target.Body;
                Vector3 anchor = (body ? body.Position : target.transform.position) + Vector3.up * worldOffset;

                Vector3 toTag = anchor - eye;
                float distance = FlightSimMath.SafeMagnitude(toTag);
                if (distance > maxDistance || distance < 1f)
                    continue;

                if (Vector3.Dot(toTag, forward) <= 0f)
                    continue;

                Vector3 screen = _camera.WorldToScreenPoint(anchor);
                if (screen.z <= 0f)
                    continue;
                if (screen.x < -80f || screen.x > Screen.width + 80f || screen.y < -40f || screen.y > Screen.height + 40f)
                    continue;

                if (occlusionTest && IsOccluded(eye, anchor, distance, target.transform))
                    continue;

                Draw(target, new Vector2(screen.x, screen.y), distance);
            }
        }

        private void Draw(NetworkedAircraft target, Vector2 screen, float distance)
        {
            float proximity = 1f - Mathf.Clamp01(distance / Mathf.Max(1f, maxDistance));
            float alpha = 1f - FlightSimMath.Smoothstep(fadeStartDistance, maxDistance, distance);
            if (alpha <= 0.02f)
                return;

            TMP_Text text = Rent();
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.fontSize = Mathf.Lerp(minFontSize, maxFontSize, proximity * proximity);
            text.text = showDistance
                ? target.DisplayName + "\n" + FormatRange(distance)
                : target.DisplayName;

            Color tint = target.IsBot ? botColor : playerColor;
            tint.a *= alpha;
            text.color = tint;
            HudVisibility.Place(overlay, text.rectTransform, screen, _canvas);
        }

        private TMP_Text Rent()
        {
            TMP_Text text;
            if (_used < _pool.Count)
            {
                text = _pool[_used];
            }
            else
            {
                text = Instantiate(nametagTemplate, overlay);
                text.gameObject.name = "Nametag";
                _pool.Add(text);
            }

            _used++;
            if (!text.gameObject.activeSelf)
                text.gameObject.SetActive(true);
            return text;
        }

        private static string FormatRange(float metres)
        {
            return metres < 1000f
                ? $"{metres:F0} m"
                : $"{metres / 1000f:F1} km";
        }

        private bool IsOccluded(Vector3 eye, Vector3 anchor, float distance, Transform target)
        {
            Vector3 direction = (anchor - eye) / distance;
            int n = Physics.RaycastNonAlloc(eye, direction, _hits, distance - 2f, occluderMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < n; i++)
            {
                Collider col = _hits[i].collider;
                if (!col)
                    continue;

                Transform t = col.transform;
                if (target && (t == target || t.IsChildOf(target)))
                    continue;

                if (col.GetComponentInParent<PlaneRigidbody>() != null)
                    continue;

                return true;
            }

            return false;
        }

        private bool ResolveCamera()
        {
            if (_camera && _camera.isActiveAndEnabled)
                return true;

            _camera = Camera.main;
            return _camera != null;
        }
    }
}
