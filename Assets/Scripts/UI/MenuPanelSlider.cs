using System.Collections;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
[AddComponentMenu("Airplane/UI/Menu Panel Slider")]
public class MenuPanelSlider : MonoBehaviour
{
    [SerializeField] private RectTransform mainMenu;
    [SerializeField] private RectTransform settings;
    [SerializeField] private float duration = 0.35f;

    private RectTransform _window;
    private RectTransform _track;
    private Coroutine _slide;
    private float _shown;

    private void Start()
    {
        if (!mainMenu || !settings)
        {
            Debug.LogError("MenuPanelSlider needs the main menu and settings panels.", this);
            return;
        }

        mainMenu.gameObject.SetActive(true);
        settings.gameObject.SetActive(true);

        var windowObject = new GameObject("MenuSlideWindow", typeof(RectTransform), typeof(RectMask2D));
        _window = windowObject.GetComponent<RectTransform>();
        _window.SetParent(mainMenu.parent, false);
        _window.SetSiblingIndex(mainMenu.GetSiblingIndex());
        _window.anchorMin = mainMenu.anchorMin;
        _window.anchorMax = mainMenu.anchorMax;
        _window.pivot = mainMenu.pivot;
        _window.anchoredPosition = mainMenu.anchoredPosition;
        _window.sizeDelta = mainMenu.sizeDelta;
        _window.localRotation = mainMenu.localRotation;
        _window.localScale = mainMenu.localScale;

        var trackObject = new GameObject("MenuSlideTrack", typeof(RectTransform));
        _track = trackObject.GetComponent<RectTransform>();
        _track.SetParent(_window, false);
        _track.anchorMin = new Vector2(0f, 0f);
        _track.anchorMax = new Vector2(0f, 1f);
        _track.pivot = new Vector2(0f, 0.5f);
        _track.localScale = Vector3.one;
        _track.localRotation = Quaternion.identity;

        Place(mainMenu, 0f, 0.5f);
        Place(settings, 0.5f, 1f);
        _shown = 0f;
        Canvas.ForceUpdateCanvases();
        Apply();
        SetInteractable(true);
    }

    public void ShowSettings()
    {
        SlideTo(1f);
    }

    public void ShowMainMenu()
    {
        SlideTo(0f);
    }

    private void LateUpdate()
    {
        if (_track && _slide == null)
            Apply();
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
        if (!_track)
            return;
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
        _track.sizeDelta = new Vector2(width * 2f, 0f);
        _track.anchoredPosition = new Vector2(-_shown * width, 0f);
    }

    private void SetInteractable(bool settled)
    {
        SetGroup(mainMenu, settled && _shown < 0.5f);
        SetGroup(settings, settled && _shown >= 0.5f);
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
