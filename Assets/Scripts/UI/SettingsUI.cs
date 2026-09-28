using System;
using System.Collections.Generic;
using System.Linq;
using Airplane.Multiplayer;
using TMPro;
using UnityEngine;

public class SettingsUI : MonoBehaviour
{
    [SerializeField] private bool fullscreen;
    [SerializeField] private TMP_Dropdown dropdown;
    private Resolution[] _resolutions;
    private List<string> _options;

    public void OnEnable()
    {
        FillResolutions();
        SetToCurrentResolution();
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
