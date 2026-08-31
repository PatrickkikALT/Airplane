using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using UnityEngine;

namespace Airplane.UI
{
    /// <summary>
    /// Draws a nametag over every other aircraft in the air, human or bot.
    ///
    /// IMGUI on purpose: it matches the flight, weapons and session HUDs this project already draws
    /// that way, and it needs no canvas, font asset or prefab wiring, so nametags work in the scene
    /// as it stands. When the premade canvas lands, the projection and culling here port across
    /// unchanged; only the drawing calls change.
    ///
    /// Creates itself on load, so nothing has to be added to the scene.
    /// </summary>
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

        private static AircraftNametagOverlay _instance;

        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private GUIStyle _style;
        private Camera _camera;

        /// <summary>Global switch, in case a cinematic or a screenshot wants a clean frame.</summary>
        public static bool Enabled { get; set; } = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            GameObject host = new GameObject("Aircraft Nametags");
            DontDestroyOnLoad(host);
            host.AddComponent<AircraftNametagOverlay>();
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
            if (!Enabled)
                return;

            var aircraft = NetworkedAircraft.All;
            if (aircraft.Count == 0)
                return;

            if (!ResolveCamera())
                return;

            EnsureStyle();

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

                // Behind the camera, or so far off-axis it would be drawn clamped to an edge.
                if (Vector3.Dot(toTag, forward) <= 0f)
                    continue;

                Vector3 screen = _camera.WorldToScreenPoint(anchor);
                if (screen.z <= 0f)
                    continue;
                if (screen.x < -80f || screen.x > Screen.width + 80f || screen.y < -40f || screen.y > Screen.height + 40f)
                    continue;

                if (occlusionTest && IsOccluded(eye, anchor, distance, target.transform))
                    continue;

                Draw(target, screen, distance);
            }
        }

        private void Draw(NetworkedAircraft target, Vector3 screen, float distance)
        {
            float proximity = 1f - Mathf.Clamp01(distance / Mathf.Max(1f, maxDistance));
            float alpha = 1f - FlightSimMath.Smoothstep(fadeStartDistance, maxDistance, distance);
            if (alpha <= 0.02f)
                return;

            _style.fontSize = Mathf.RoundToInt(Mathf.Lerp(minFontSize, maxFontSize, proximity * proximity));

            string label = showDistance
                ? $"{target.DisplayName}\n{FormatRange(distance)}"
                : target.DisplayName;

            float width = 220f;
            float height = _style.fontSize * (showDistance ? 2.6f : 1.4f);
            Rect rect = new Rect(screen.x - width * 0.5f, Screen.height - screen.y - height, width, height);

            Color tint = target.IsBot ? botColor : playerColor;
            tint.a *= alpha;

            // Cheap outline: the same text in near-black behind the label keeps it readable against
            // both sky and terrain without needing a shader or a background sprite.
            Color shadow = new Color(0f, 0f, 0f, alpha * 0.85f);
            _style.normal.textColor = shadow;
            GUI.Label(new Rect(rect.x + 1f, rect.y + 1f, rect.width, rect.height), label, _style);

            _style.normal.textColor = tint;
            GUI.Label(rect, label, _style);
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

                // The camera sits behind the player's own aircraft, which would otherwise mask every
                // tag in front of it.
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

        private void EnsureStyle()
        {
            if (_style != null)
                return;

            _style = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.LowerCenter,
                fontStyle = FontStyle.Bold,
                richText = false,
                wordWrap = false
            };
        }
    }
}
