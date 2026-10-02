using System;
using System.Collections.Generic;
using System.Linq;
using Airplane.Multiplayer;
using Airplane.Weather;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingsUI : MonoBehaviour
{
    [SerializeField] private bool fullscreen;
    [SerializeField] private TMP_Dropdown dropdown;
    [SerializeField] private Slider environmentFlashSlider;
    private Resolution[] _resolutions;
    private List<string> _options;

    public void OnEnable()
    {
        FillResolutions();
        SetToCurrentResolution();
        if (environmentFlashSlider)
            environmentFlashSlider.SetValueWithoutNotify(PlayerPrefs.GetFloat(LightningSystem.EnvironmentFlashPrefKey, 1f));
    }

    private void FillResolutions()
    {
        _resolutions = Screen.resolutions;
        _options = _resolutions.Select(resolution => resolution.width + "x" + resolution.height).ToList();
        dropdown.ClearOptions();
        dropdown.AddOptions(_options);
    }

    private void SetToCurrentResolution()
    {
        if (_resolutions.Contains(Screen.currentResolution))
        {
            int index = Array.IndexOf(_resolutions, Screen.currentResolution);
            dropdown.SetValueWithoutNotify(index);    
        }
    }

    public void SetEnvironmentFlash(float value)
    {
        PlayerPrefs.SetFloat(LightningSystem.EnvironmentFlashPrefKey, Mathf.Max(0f, value));
        PlayerPrefs.Save();
    }

    public void SetFullscreen(bool value)
    {
        fullscreen = value;
        SetResolution(Screen.currentResolution);
    }
    
    public void SetResolution(int index)
    {
        Resolution resolution = _resolutions[index];
        SetResolution(resolution);
    }

    private void SetResolution(Resolution resolution)
    {
        Screen.SetResolution(resolution.width, resolution.height, fullscreen);
    }
}
