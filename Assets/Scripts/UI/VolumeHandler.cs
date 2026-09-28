using System;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class VolumeHandler : MonoBehaviour
{
    [SerializeField] private AudioMixer audioMixer;

    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;
    public void OnEnable()
    {
        audioMixer.GetFloat("Master", out float currentMaster);
        float masterValue = Mathf.Pow(10f, currentMaster / 20f);
        masterSlider.SetValueWithoutNotify(masterValue);
        
        audioMixer.GetFloat("Music", out float currentMusic);
        float musicValue = Mathf.Pow(10f, currentMusic / 20f);
        musicSlider.SetValueWithoutNotify(musicValue);
        
        audioMixer.GetFloat("SFX", out float currentSfx);
        float sfxValue = Mathf.Pow(10f, currentSfx / 20f);
        sfxSlider.SetValueWithoutNotify(sfxValue);
    }

    public void SetMaster(float value)
    {
        audioMixer.SetFloat("Master", Mathf.Log10(value) * 20);
    }

    public void SetMusic(float value)
    {
        audioMixer.SetFloat("Music", Mathf.Log10(value) * 20);
    }

    public void SetSFX(float value)
    {
        audioMixer.SetFloat("SFX", Mathf.Log10(value) * 20);
    }
}
