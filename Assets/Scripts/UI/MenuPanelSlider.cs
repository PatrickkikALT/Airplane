using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[DisallowMultipleComponent]
[AddComponentMenu("Airplane/UI/Menu Panel Slider")]
public class MenuPanelSlider : MonoBehaviour
{
    [SerializeField] private RectTransform mainMenu;
    [SerializeField] private RectTransform settings;
    [SerializeField] private RectTransform[] extraPanels;
    [SerializeField] private float duration = 0.35f;

    private RectTransform _window;
    private RectTransform _track;
    private RectTransform[] _panels;
    private Coroutine _slide;
    private float _shown;
    private bool _ready;
    private bool _hasPending;
    private float _pending;

    private void Start()
    {
        _panels = CollectPanels();
        if (_panels.Length == 0)
        {
            Debug.LogError("MenuPanelSlider needs at least one panel.", this);
            return;
        }

        RectTransform first = _panels[0];
        for (int i = 0; i < _panels.Length; i++)
            _panels[i].gameObject.SetActive(true);

        var windowObject = new GameObject("MenuSlideWindow", typeof(RectTransform), typeof(RectMask2D));
        _window = windowObject.GetComponent<RectTransform>();
        _window.SetParent(first.parent, false);
        _window.SetSiblingIndex(first.GetSiblingIndex());
        _window.anchorMin = first.anchorMin;
        _window.anchorMax = first.anchorMax;
        _window.pivot = first.pivot;
        _window.anchoredPosition = first.anchoredPosition;
        _window.sizeDelta = first.sizeDelta;
        _window.localRotation = first.localRotation;
        _window.localScale = first.localScale;

        var trackObject = new GameObject("MenuSlideTrack", typeof(RectTransform));
        _track = trackObject.GetComponent<RectTransform>();
        _track.SetParent(_window, false);
        _track.anchorMin = new Vector2(0f, 0f);
        _track.anchorMax = new Vector2(0f, 1f);
        _track.pivot = new Vector2(0f, 0.5f);
        _track.localScale = Vector3.one;
        _track.localRotation = Quaternion.identity;

        for (int i = 0; i < _panels.Length; i++)
        {
            float min = i / (float)_panels.Length;
            float max = (i + 1) / (float)_panels.Length;
            Place(_panels[i], min, max);
        }

        _ready = true;
        _shown = _hasPending ? _pending : 0f;
        _hasPending = false;
        Canvas.ForceUpdateCanvases();
        Apply();
        SetInteractable(true);
    }

    public void ShowMainMenu()
    {
        Show(0);
    }

    public void ShowSettings()
    {
        Show(1);
    }

    public void Show(int index)
    {
        if (_ready && (index < 0 || index >= _panels.Length))
            return;
        SlideTo(index);
    }

    public void Hide()
    {
        SlideTo(-1f);
    }

    private void LateUpdate()
    {
        if (_track && _slide == null)
            Apply();
    }

    private RectTransform[] CollectPanels()
    {
        var list = new List<RectTransform>();
        AddPanel(list, mainMenu);
        AddPanel(list, settings);
        if (extraPanels == null)
            return list.ToArray();
        for (int i = 0; i < extraPanels.Length; i++)
            AddPanel(list, extraPanels[i]);
        return list.ToArray();
    }

    private static void AddPanel(List<RectTransform> list, RectTransform panel)
    {
        if (panel && !list.Contains(panel))
            list.Add(panel);
    }

    private void Place(RectTransform panel, float anchorMinX, float anchorMaxX)
    {
        panel.SetParent(_track, false);
        panel.anchorMin = new Vector2(anchorMinX, 0f);
        panel.anchorMax = new Vector2(anchorMaxX, 1f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = Vector2.zero;
        panel.anchoredPosition = Vector2.zero;
        panel.localRotation = Quaternion.identity;
        panel.localScale = Vector3.one;
    }

    private void SlideTo(float target)
    {
        if (!_ready)
        {
            _pending = target;
            _hasPending = true;
            return;
        }

        if (_slide == null && Mathf.Approximately(_shown, target)) 
            return;
        if (_slide != null)
            StopCoroutine(_slide);
        _slide = StartCoroutine(Slide(target));
    }

    private IEnumerator Slide(float target)
    {
        float from = _shown;
        float t = 0f;
        SetInteractable(false);
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / Mathf.Max(0.01f, duration)));
            _shown = Mathf.Lerp(from, target, u);
            Apply();
            yield return null;
        }

        _shown = target;
        Apply();
        SetInteractable(true);
        _slide = null;
    }

    private void Apply()
    {
        float width = _window.rect.width;
        _track.sizeDelta = new Vector2(width * _panels.Length, 0f);
        _track.anchoredPosition = new Vector2(-_shown * width, 0f);
    }

    private void SetInteractable(bool settled)
    {
        for (int i = 0; i < _panels.Length; i++)
            SetGroup(_panels[i], settled && Mathf.Abs(_shown - i) < 0.5f);
    }

    private static void SetGroup(RectTransform panel, bool on)
    {
        var group = panel.GetComponent<CanvasGroup>();
        if (!group)
            group = panel.gameObject.AddComponent<CanvasGroup>();
        group.interactable = on;
        group.blocksRaycasts = on;
    }
}
