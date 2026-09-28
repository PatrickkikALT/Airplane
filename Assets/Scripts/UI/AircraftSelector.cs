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
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;
        [SerializeField] private Button backButton;
        [SerializeField] private TMP_Text label;
        [SerializeField] private TMP_Text massStat;
        [SerializeField] private TMP_Text speedStat;
        [SerializeField] private TMP_Text thrustStat;
        [SerializeField] private TMP_Text rollStat;
        [SerializeField] private TMP_Text pitchStat;
        [Tooltip("The Aircraft1A already placed in the hangar. Its model is swapped for the selected plane.")]
        [SerializeField] private Transform hangarAircraft;

        private readonly List<GameObject> _originalChildren = new List<GameObject>();
        private GameObject _preview;
        private bool _hidOriginal;

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
            if (!view || view.activeSelf)
                return;

            view.SetActive(true);
            ShowCurrent();
        }

        public void Close()
        {
            if (view)
                view.SetActive(false);
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
            SetStat(massStat, body ? $"{body.Mass:0} kg" : "—");
            SetStat(speedStat, engine ? $"{engine.ZeroThrustAirspeed * 3.6f:0} km/h" : "—");
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
