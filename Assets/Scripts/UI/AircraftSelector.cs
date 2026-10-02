using System.Collections.Generic;
using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace Airplane.UI
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/UI/Aircraft Selector")]
    public sealed class AircraftSelector : MonoBehaviour
    {
        [SerializeField] private AircraftCatalog catalog;
        [SerializeField] private Button aircraftButton;
        [SerializeField] private GameObject view;
        [SerializeField] private float slideDuration = 0.35f;
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Text massStat;
        [SerializeField] private TMP_Text speedStat;
        [SerializeField] private TMP_Text zeroThrustStat;
        [SerializeField] private TMP_Text thrustStat;
        [SerializeField] private TMP_Text rollStat;
        [SerializeField] private TMP_Text pitchStat;
        [Tooltip("The Aircraft1A already placed in the hangar. Its model is swapped for the selected plane.")]
        [SerializeField] private Transform hangarAircraft;

        private readonly List<GameObject> _originalChildren = new List<GameObject>();
        private GameObject _preview;
        private bool _hidOriginal;
        private RectTransform _panel;
        private Coroutine _slide;
        private float _shown;
        private bool _poseReady;
        private Vector2 _restAnchored;
        private Vector2 _anchorMin;
        private Vector2 _anchorMax;
        private Vector2 _pivot;
        private Vector2 _sizeDelta;
        private float _slideDistance;

        private void Awake()
        {
            if (!aircraftButton)
                aircraftButton = GetComponent<Button>();
            ResolveHangar();
        }

        private void OnEnable()
        {
            ResolveHangar();
            if (!catalog)
                Debug.LogError("Aircraft selector has no catalog.", this);
            if (!hangarAircraft)
                Debug.LogError("Aircraft selector could not find Aircraft1A in the hangar.", this);

            BindCatalog();
            Wire(true);
            if (catalog)
                AircraftSelection.SetLocalIndex(LocalPlayerIdentity.AircraftIndex, catalog.Count);
            ShowCurrent();
        }

        private void OnDisable()
        {
            Wire(false);
            ClearPreview();
            RestoreHangar();
        }

        public void Open()
        {
            if (!ResolvePanel())
                return;

            ShowCurrent();
            SlidePanel(1f);
        }

        public void Close()
        {
            if (!ResolvePanel())
                return;

            SlidePanel(0f);
        }

        private bool ResolvePanel()
        {
            if (!view)
            {
                MenuPanelSlider slider = FindAnyObjectByType<MenuPanelSlider>();
                Transform found = slider ? slider.transform.Find("AircraftSelector") : null;
                if (found)
                    view = found.gameObject;
            }

            _panel = view ? view.transform as RectTransform : null;
            if (!_panel)
                return false;

            if (_poseReady)
                return true;

            _anchorMin = _panel.anchorMin;
            _anchorMax = _panel.anchorMax;
            _pivot = _panel.pivot;
            _sizeDelta = _panel.sizeDelta;
            _restAnchored = _panel.anchoredPosition;
            RectTransform parent = _panel.parent as RectTransform;
            float span = parent ? Mathf.Abs(_anchorMax.x - _anchorMin.x) * parent.rect.width : _panel.rect.width;
            _slideDistance = Mathf.Max(1f, span + _sizeDelta.x);
            _poseReady = true;
            _shown = view.activeSelf ? 1f : 0f;
            return true;
        }

        private void SlidePanel(float target)
        {
            if (_slide != null)
                StopCoroutine(_slide);
            _slide = StartCoroutine(Slide(target));
        }

        private System.Collections.IEnumerator Slide(float target)
        {
            view.SetActive(true);
            ApplyPanel(_shown);
            SetPanelInteractive(false);
            float from = _shown;
            float t = 0f;
            while (t < slideDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.01f, slideDuration)));
                _shown = Mathf.Lerp(from, target, u);
                ApplyPanel(_shown);
                yield return null;
            }

            _shown = target;
            ApplyPanel(_shown);
            SetPanelInteractive(target >= 1f);
            if (target <= 0f)
                view.SetActive(false);
            _slide = null;
        }

        private void ApplyPanel(float shown)
        {
            _panel.anchorMin = _anchorMin;
            _panel.anchorMax = _anchorMax;
            _panel.pivot = _pivot;
            _panel.sizeDelta = _sizeDelta;
            _panel.localRotation = Quaternion.identity;
            _panel.localScale = Vector3.one;
            _panel.anchoredPosition = _restAnchored + new Vector2(-(1f - shown) * _slideDistance, 0f);
        }

        private void SetPanelInteractive(bool on)
        {
            CanvasGroup group = view.GetComponent<CanvasGroup>();
            if (!group)
                group = view.AddComponent<CanvasGroup>();
            group.interactable = on;
            group.blocksRaycasts = on;
        }

        public void Previous()
        {
            Step(-1);
        }

        public void Next()
        {
            Step(1);
        }

        private void ResolveHangar()
        {
            if (hangarAircraft)
                return;
            GameObject placed = GameObject.Find("Aircraft1A");
            if (placed)
                hangarAircraft = placed.transform;
        }

        private void Step(int direction)
        {
            int count = catalog ? catalog.Count : 0;
            if (count == 0)
                return;
            AircraftSelection.SetLocalIndex(LocalPlayerIdentity.AircraftIndex + direction, count);
            ShowCurrent();
        }

        private void BindCatalog()
        {
            if (!catalog)
                return;

            AircraftSelection.Bind(catalog);
            NetworkManager manager = NetworkManager.Singleton;
            if (!manager)
                manager = FindAnyObjectByType<NetworkManager>();
            catalog.Register(manager);
        }

        private void ShowCurrent()
        {
            RefreshLabel();
            int count = catalog ? catalog.Count : 0;
            bool canStep = count > 1;
            if (previousButton)
                previousButton.interactable = canStep;
            if (nextButton)
                nextButton.interactable = canStep;
            if (aircraftButton && (!view || view.activeSelf))
                aircraftButton.interactable = count > 0;

            if (!hangarAircraft || !catalog || count == 0)
            {
                ClearPreview();
                RestoreHangar();
                return;
            }

            GameObject prefab = catalog.GetPreviewPrefab(LocalPlayerIdentity.AircraftIndex);
            if (!prefab)
            {
                Debug.LogError("Selected aircraft has no preview prefab.", catalog);
                ClearPreview();
                RestoreHangar();
                return;
            }

            HideOriginal();
            ClearPreview();
            _preview = Instantiate(prefab, hangarAircraft);
            _preview.transform.localPosition = Vector3.zero;
            _preview.transform.localRotation = Quaternion.identity;
            _preview.transform.localScale = Vector3.one;
            MakeDisplayOnly(_preview);
        }

        private void HideOriginal()
        {
            if (_hidOriginal || !hangarAircraft)
                return;
            _hidOriginal = true;
            for (int i = 0; i < hangarAircraft.childCount; i++)
            {
                GameObject child = hangarAircraft.GetChild(i).gameObject;
                _originalChildren.Add(child);
                child.SetActive(false);
            }
        }

        private void RestoreHangar()
        {
            for (int i = 0; i < _originalChildren.Count; i++)
            {
                if (_originalChildren[i])
                    _originalChildren[i].SetActive(true);
            }

            _originalChildren.Clear();
            _hidOriginal = false;
        }

        private void RefreshLabel()
        {
            int count = catalog ? catalog.Count : 0;
            if (label)
            {
                label.text = count > 0
                    ? catalog.GetDisplayName(LocalPlayerIdentity.AircraftIndex)
                    : "No aircraft";
            }

            GameObject prefab = count > 0 ? catalog.GetPrefab(LocalPlayerIdentity.AircraftIndex) : null;
            PlaneRigidbody body = prefab ? prefab.GetComponent<PlaneRigidbody>() : null;
            AircraftEngine engine = prefab ? prefab.GetComponentInChildren<AircraftEngine>(true) : null;
            AircraftEngine[] engines = prefab ? prefab.GetComponentsInChildren<AircraftEngine>(true) : null;
            AeroSurface[] surfaces = prefab ? prefab.GetComponentsInChildren<AeroSurface>(true) : null;
            float levelSpeed = body ? LevelFlightSpeed.SeaLevelTrueAirspeed(body, engines, surfaces) : 0f;
            SetStat(massStat, body ? $"{body.Mass:0} kg" : "—");
            SetStat(speedStat, levelSpeed > 1f ? $"{levelSpeed * 3.6f:0} km/h" : "—");
            SetStat(zeroThrustStat, engine ? $"{engine.ZeroThrustAirspeed * 3.6f:0} km/h" : "—");
            SetStat(thrustStat, engine ? $"{engine.MaxStaticThrust / 1000f:0.0} kN" : "—");
            SetStat(rollStat, body ? $"{body.MaxAngularSpeedDeg.x:0} °/s" : "—");
            SetStat(pitchStat, body ? $"{body.MaxAngularSpeedDeg.z:0} °/s" : "—");
        }

        private static void SetStat(TMP_Text text, string value)
        {
            if (text)
                text.text = value;
        }

        private void ClearPreview()
        {
            if (!_preview)
                return;
            Destroy(_preview);
            _preview = null;
        }

        private static void MakeDisplayOnly(GameObject instance)
        {
            Behaviour[] behaviours = instance.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is Animator)
                    continue;
                behaviours[i].enabled = false;
            }

            ParticleSystem[] particles = instance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particles.Length; i++)
                particles[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < colliders.Length; i++)
                colliders[i].enabled = false;
        }

        private void Wire(bool subscribe)
        {
            if (subscribe)
            {
                if (aircraftButton) aircraftButton.onClick.AddListener(Open);
                if (previousButton) previousButton.onClick.AddListener(Previous);
                if (nextButton) nextButton.onClick.AddListener(Next);
                if (backButton) backButton.onClick.AddListener(Close);
                return;
            }

            if (aircraftButton) aircraftButton.onClick.RemoveListener(Open);
            if (previousButton) previousButton.onClick.RemoveListener(Previous);
            if (nextButton) nextButton.onClick.RemoveListener(Next);
            if (backButton) backButton.onClick.RemoveListener(Close);
        }
    }
}
