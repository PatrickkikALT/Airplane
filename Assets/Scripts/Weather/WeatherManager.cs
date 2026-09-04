using System;
using System.Linq;
using Airplane.Extensions;
using Airplane.FlightSimulation;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public class WeatherPreset
{
    public string Name;
    public int RainCount;

    [Header("Thunder")]
    public bool LightningEnabled;
    public float StrikesPerMinute;
    public float LightningFlashIntensity = 1f;
    [Range(0f, 1f)] public float LightningBranchiness = 0.6f;
    public Color LightningCoreColor = Color.white;
    public Color LightningGlowColor = new(0.55f, 0.7f, 1f, 1f);
    [Range(0f, 1f)] public float ThunderVolume = 1f;

    [Header("Tornado")]
    [Range(0, 150)] public int TornadoCount;
    public int TornadoDebrisCount = 40000;
    public float TornadoRadius = 45f;
    public float TornadoHeight = 900f;
    public float TornadoSpinSpeed = 85f;
    public float TornadoUpdraft = 40f;

    [Header("General")]
    public bool CloudsEnabled = true;
    public bool LocalClouds;
    public VolumetricClouds.CloudPresets CloudPreset = VolumetricClouds.CloudPresets.Custom;

    [Header("Fog")]
    public bool FogEnabled = true;
    public Color FogColor = Color.white;
    public float FogDensity = 1.0f;
    public float FogStart = 50f;
    public float FogEnd = 50f;
    public FogMode Mode = FogMode.Linear;
    
    [Header("Shape")]
    [Range(0f, 1f)] public float DensityMultiplier = 0.4f;
    public AnimationCurve DensityCurve = new(new Keyframe(0f, 0f), new Keyframe(0.15f, 1.0f), new Keyframe(1.0f, 0.1f));
    [Range(0f, 1f)] public float ShapeFactor = 0.9f;
    public float ShapeScale = 5.0f;
    [Range(0f, 1f)] public float ErosionFactor = 0.8f;
    public float ErosionScale = 107.0f;
    public AnimationCurve ErosionCurve = new(new Keyframe(0f, 1f), new Keyframe(0.1f, 0.9f), new Keyframe(1.0f, 1.0f));
    public AnimationCurve AmbientOcclusionCurve = new(new Keyframe(0f, 0f), new Keyframe(0.25f, 0.4f), new Keyframe(1.0f, 0.0f));
    public bool MicroErosion;
    [Range(0f, 1f)] public float MicroErosionFactor = 0.5f;
    public float MicroErosionScale = 200.0f;
    public float BottomAltitude = 1200.0f;
    public float AltitudeRange = 2000.0f;
    public Vector3 ShapeOffset;
    [Range(0f, 1f)] public float EarthCurvature;

    [Header("Wind")]
    public float GlobalSpeed;
    [Range(0f, 360f)] public float GlobalOrientation;
    [Range(0f, 1f)] public float ShapeSpeedMultiplier = 1.0f;
    [Range(0f, 1f)] public float ErosionSpeedMultiplier = 0.25f;
    [Range(-1f, 1f)] public float AltitudeDistortion = 0.25f;
    public float VerticalShapeWindSpeed;
    public float VerticalErosionWindSpeed;

    [Header("Lighting")]
    [Range(0f, 2f)] public float AmbientLightProbeDimmer = 1.0f;
    [Range(0f, 2f)] public float SunLightDimmer = 1.0f;
    [Range(0f, 1f)] public float ErosionOcclusion = 0.1f;
    public Color ScatteringTint = new(0f, 0f, 0f, 1f);
    [Range(0f, 1f)] public float PowderEffectIntensity = 0.25f;
    [Range(0f, 1f)] public float MultiScattering = 0.5f;

    [Header("Shadows")]
    public bool Shadows;
    public VolumetricClouds.CloudShadowResolution ShadowResolution = VolumetricClouds.CloudShadowResolution.Medium256;
    public float ShadowDistance = 8000.0f;
    [Range(0f, 1f)] public float ShadowOpacity = 1.0f;
    [Range(0f, 1f)] public float ShadowOpacityFallback;

    [Header("Quality")]
    [Range(0f, 1f)] public float TemporalAccumulationFactor = 0.95f;
    [Range(0f, 1f)] public float PerceptualBlending = 1.0f;
    [Range(24, 256)] public int NumPrimarySteps = 32;
    [Range(1, 16)] public int NumLightSteps = 2;
    public VolumetricClouds.CloudFadeInMode FadeInMode = VolumetricClouds.CloudFadeInMode.Automatic;
    public float FadeInStart;
    public float FadeInDistance = 5000.0f;

    public void ApplyCloudLook(VolumetricClouds.CloudPresets look)
    {
        CloudPreset = look;
        bool micro = MicroErosion;
        switch (look)
        {
            case VolumetricClouds.CloudPresets.Sparse:
                DensityMultiplier = 0.4f;
                ShapeFactor = micro ? 0.925f : 0.95f;
                ShapeScale = 5.0f;
                ErosionFactor = micro ? 0.85f : 0.8f;
                ErosionScale = micro ? 75.0f : 107.0f;
                if (micro)
                {
                    MicroErosionFactor = 0.65f;
                    MicroErosionScale = 300.0f;
                }

                DensityCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.05f, 1.0f),
                    new Keyframe(0.75f, 1.0f), new Keyframe(1.0f, 0.0f));
                ErosionCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.1f, 0.9f),
                    new Keyframe(1.0f, 1.0f));
                AmbientOcclusionCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 0.5f),
                    new Keyframe(1.0f, 0.0f));
                BottomAltitude = 3000.0f;
                AltitudeRange = 1000.0f;
                break;
            case VolumetricClouds.CloudPresets.Overcast:
                DensityMultiplier = 0.3f;
                ShapeFactor = micro ? 0.45f : 0.5f;
                ShapeScale = 5.0f;
                ErosionFactor = micro ? 0.7f : 0.5f;
                ErosionScale = micro ? 75.0f : 107.0f;
                if (micro)
                {
                    MicroErosionFactor = 0.5f;
                    MicroErosionScale = 300.0f;
                }

                DensityCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.05f, 1.0f),
                    new Keyframe(0.9f, 0.0f), new Keyframe(1.0f, 0.0f));
                ErosionCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.1f, 0.9f),
                    new Keyframe(1.0f, 1.0f));
                AmbientOcclusionCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1.0f, 0.0f));
                BottomAltitude = 1500.0f;
                AltitudeRange = 2500.0f;
                break;
            case VolumetricClouds.CloudPresets.Stormy:
                DensityMultiplier = 0.35f;
                ShapeFactor = micro ? 0.825f : 0.85f;
                ShapeScale = 5.0f;
                ErosionFactor = micro ? 0.9f : 0.75f;
                ErosionScale = micro ? 75.0f : 107.0f;
                if (micro)
                {
                    MicroErosionFactor = 0.6f;
                    MicroErosionScale = 300.0f;
                }

                DensityCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.037f, 1.0f),
                    new Keyframe(0.6f, 1.0f), new Keyframe(1.0f, 0.0f));
                ErosionCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.05f, 0.8f),
                    new Keyframe(0.2438f, 0.9498f), new Keyframe(0.5f, 1.0f), new Keyframe(0.93f, 0.9268f),
                    new Keyframe(1.0f, 1.0f));
                AmbientOcclusionCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.1f, 0.4f),
                    new Keyframe(1.0f, 0.0f));
                BottomAltitude = 1000.0f;
                AltitudeRange = 5000.0f;
                break;
            case VolumetricClouds.CloudPresets.Cloudy:
                DensityMultiplier = 0.4f;
                ShapeFactor = micro ? 0.875f : 0.9f;
                ShapeScale = 5.0f;
                ErosionFactor = micro ? 0.9f : 0.8f;
                ErosionScale = micro ? 75.0f : 107.0f;
                if (micro)
                {
                    MicroErosionFactor = 0.65f;
                    MicroErosionScale = 300.0f;
                }

                DensityCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.15f, 1.0f),
                    new Keyframe(1.0f, 0.1f));
                ErosionCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.1f, 0.9f),
                    new Keyframe(1.0f, 1.0f));
                AmbientOcclusionCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.25f, 0.4f),
                    new Keyframe(1.0f, 0.0f));
                BottomAltitude = 1200.0f;
                AltitudeRange = 2000.0f;
                break;
        }
    }

    public static WeatherPreset[] CreateDefaults()
    {
        return new[]
        {
            Clear(),
            Fair(),
            Cloudy(),
            Overcast(),
            Rain(),
            Storm(),
            Thunderstorm(),
            Thunder(),
            Tornado()
        };
    }

    public static WeatherPreset Clear()
    {
        WeatherPreset p = Base("Clear", VolumetricClouds.CloudPresets.Sparse);
        p.RainCount = 0;
        p.DensityMultiplier = 0.12f;
        p.ShapeFactor = 0.97f;
        p.BottomAltitude = 4000.0f;
        p.AltitudeRange = 800.0f;
        p.GlobalSpeed = 8.0f;
        p.SunLightDimmer = 1.05f;
        p.AmbientLightProbeDimmer = 1.0f;
        p.Shadows = false;
        return p;
    }

    public static WeatherPreset Fair()
    {
        WeatherPreset p = Base("Fair", VolumetricClouds.CloudPresets.Sparse);
        p.RainCount = 0;
        p.GlobalSpeed = 20.0f;
        p.Shadows = false;
        return p;
    }

    public static WeatherPreset Cloudy()
    {
        WeatherPreset p = Base("Cloudy", VolumetricClouds.CloudPresets.Cloudy);
        p.RainCount = 0;
        p.GlobalSpeed = 35.0f;
        p.Shadows = true;
        p.ShadowOpacity = 0.45f;
        return p;
    }

    public static WeatherPreset Overcast()
    {
        WeatherPreset p = Base("Overcast", VolumetricClouds.CloudPresets.Overcast);
        p.RainCount = 0;
        p.GlobalSpeed = 45.0f;
        p.SunLightDimmer = 0.7f;
        p.AmbientLightProbeDimmer = 0.85f;
        p.ScatteringTint = new Color(0.08f, 0.09f, 0.12f, 1f);
        p.Shadows = true;
        p.ShadowOpacity = 0.7f;
        return p;
    }

    public static WeatherPreset Rain()
    {
        WeatherPreset p = Base("Rain", VolumetricClouds.CloudPresets.Overcast);
        p.RainCount = 80000;
        p.GlobalSpeed = 55.0f;
        p.SunLightDimmer = 0.55f;
        p.AmbientLightProbeDimmer = 0.7f;
        p.ScatteringTint = new Color(0.12f, 0.13f, 0.16f, 1f);
        p.Shadows = true;
        p.ShadowOpacity = 0.8f;
        p.DensityMultiplier = 0.38f;
        return p;
    }

    public static WeatherPreset Storm()
    {
        WeatherPreset p = Base("Storm", VolumetricClouds.CloudPresets.Stormy);
        p.RainCount = 180000;
        p.GlobalSpeed = 90.0f;
        p.AltitudeDistortion = 0.45f;
        p.SunLightDimmer = 0.4f;
        p.AmbientLightProbeDimmer = 0.55f;
        p.ScatteringTint = new Color(0.18f, 0.16f, 0.2f, 1f);
        p.MultiScattering = 0.65f;
        p.Shadows = true;
        p.ShadowOpacity = 0.95f;
        p.ShadowDistance = 10000.0f;
        p.LightningEnabled = true;
        p.StrikesPerMinute = 4.0f;
        p.LightningFlashIntensity = 0.8f;
        p.LightningBranchiness = 0.45f;
        p.ThunderVolume = 0.7f;
        return p;
    }

    public static WeatherPreset Thunderstorm()
    {
        WeatherPreset p = Base("Thunderstorm", VolumetricClouds.CloudPresets.Stormy);
        p.RainCount = 280000;
        p.GlobalSpeed = 120.0f;
        p.AltitudeDistortion = 0.6f;
        p.VerticalShapeWindSpeed = 8.0f;
        p.SunLightDimmer = 0.28f;
        p.AmbientLightProbeDimmer = 0.4f;
        p.ScatteringTint = new Color(0.22f, 0.18f, 0.28f, 1f);
        p.MultiScattering = 0.75f;
        p.PowderEffectIntensity = 0.15f;
        p.Shadows = true;
        p.ShadowOpacity = 1.0f;
        p.ShadowDistance = 12000.0f;
        p.DensityMultiplier = 0.42f;
        p.BottomAltitude = 700.0f;
        p.AltitudeRange = 5500.0f;
        p.LightningEnabled = true;
        p.StrikesPerMinute = 14.0f;
        p.LightningFlashIntensity = 1.0f;
        p.LightningBranchiness = 0.65f;
        p.ThunderVolume = 1.0f;
        return p;
    }

    public static WeatherPreset Tornado()
    {
        WeatherPreset p = Base("Tornado", VolumetricClouds.CloudPresets.Stormy);
        p.RainCount = 220000;
        p.TornadoCount = 2;
        p.TornadoDebrisCount = 60000;
        p.TornadoRadius = 55.0f;
        p.TornadoHeight = 1100.0f;
        p.TornadoSpinSpeed = 110.0f;
        p.TornadoUpdraft = 55.0f;
        p.GlobalSpeed = 150.0f;
        p.AltitudeDistortion = 0.75f;
        p.VerticalShapeWindSpeed = 14.0f;
        p.VerticalErosionWindSpeed = 6.0f;
        p.SunLightDimmer = 0.22f;
        p.AmbientLightProbeDimmer = 0.35f;
        p.ScatteringTint = new Color(0.26f, 0.2f, 0.14f, 1f);
        p.MultiScattering = 0.8f;
        p.PowderEffectIntensity = 0.1f;
        p.Shadows = true;
        p.ShadowOpacity = 1.0f;
        p.ShadowDistance = 14000.0f;
        p.DensityMultiplier = 0.48f;
        // wall cloud sits low so the funnels visually reach it
        p.BottomAltitude = 600.0f;
        p.AltitudeRange = 6000.0f;
        p.FogEnabled = true;
        p.FogColor = new Color(0.35f, 0.3f, 0.24f, 1f);
        p.FogDensity = 0.012f;
        p.Mode = FogMode.ExponentialSquared;
        p.LightningEnabled = true;
        p.StrikesPerMinute = 20.0f;
        p.LightningFlashIntensity = 1.1f;
        p.LightningBranchiness = 0.7f;
        p.ThunderVolume = 1.0f;
        return p;
    }

    public static WeatherPreset Thunder()
    {
        WeatherPreset p = Base("Thunder", VolumetricClouds.CloudPresets.Stormy);
        p.RainCount = 120000;
        p.GlobalSpeed = 70.0f;
        p.AltitudeDistortion = 0.5f;
        p.VerticalShapeWindSpeed = 10.0f;
        p.SunLightDimmer = 0.25f;
        p.AmbientLightProbeDimmer = 0.38f;
        p.ScatteringTint = new Color(0.2f, 0.19f, 0.26f, 1f);
        p.MultiScattering = 0.7f;
        p.Shadows = true;
        p.ShadowOpacity = 1.0f;
        p.ShadowDistance = 12000.0f;
        p.DensityMultiplier = 0.45f;
        p.BottomAltitude = 900.0f;
        p.AltitudeRange = 5000.0f;
        // the whole point of this one: near-constant strikes, mostly cloud to ground
        p.LightningEnabled = true;
        p.StrikesPerMinute = 36.0f;
        p.LightningFlashIntensity = 1.25f;
        p.LightningBranchiness = 0.8f;
        p.LightningCoreColor = Color.white;
        p.LightningGlowColor = new Color(0.62f, 0.72f, 1f, 1f);
        p.ThunderVolume = 1.0f;
        return p;
    }

    private static WeatherPreset Base(string name, VolumetricClouds.CloudPresets look)
    {
        WeatherPreset p = new() { Name = name };
        p.ApplyCloudLook(look);
        p.CloudPreset = VolumetricClouds.CloudPresets.Custom;
        return p;
    }
}

namespace Airplane.Weather
{
    public class WeatherManager : SingletonMonoBehaviour<WeatherManager>
    {
        private const int CurveSamples = 8;

        [SerializeField] private Volume volume;
        [SerializeField] private WeatherSystem weatherSystem;
        [SerializeField] private TornadoSystem tornadoSystem;
        [SerializeField] private LightningSystem lightningSystem;
        [SerializeField] private WeatherPreset[] presets;
        [SerializeField] private int currentPreset;
        [SerializeField] [Min(0f)] private float transitionDuration = 10f;
        private readonly Keyframe[] _curveKeys = new Keyframe[CurveSamples];
        private float _blend;

        private bool _blending;
        private CloudSnapshot _fromClouds;
        private int _fromRain;
        private Vector3 _fromWind;
        private VolumetricClouds.CloudPresets _toCloudPreset;
        private CloudSnapshot _toClouds;
        private int _toRain;
        private Vector3 _toWind;
        private float _toDensity;
        private int _fromTornadoes;
        private int _toTornadoes;
        private int _fromDebris;
        private int _toDebris;
        private TornadoShape _fromTornadoShape;
        private TornadoShape _toTornadoShape;
        private LightningSnapshot _fromLightning;
        private LightningSnapshot _toLightning;

        private void Reset()
        {
            presets = WeatherPreset.CreateDefaults();
        }

        private void Start()
        {
            ApplyImmediate();
        }

        private void Update()
        {
            if (!_blending)
                return;

            float duration = Mathf.Max(transitionDuration, 0.0001f);
            _blend = Mathf.MoveTowards(_blend, 1f, Time.deltaTime / duration);
            ApplyBlend(Mathf.SmoothStep(0f, 1f, _blend));

            if (_blend >= 1f)
                _blending = false;
        }

        public void UpdateWeather()
        {
            if (!TryGetTarget(out WeatherPreset target))
                return;

            if (!Application.isPlaying || transitionDuration <= 0.001f)
            {
                ApplyImmediate(target);
                return;
            }

            BeginBlend(target);
        }

        public void LoadDefaultPresets()
        {
            presets = WeatherPreset.CreateDefaults();
            currentPreset = 0;
            UpdateWeather();
        }

        private bool TryGetTarget(out WeatherPreset target)
        {
            target = null;
            if (presets == null || presets.Length == 0)
                return false;

            currentPreset = Mathf.Clamp(currentPreset, 0, presets.Length - 1);
            target = presets[currentPreset];
            return target != null;
        }

        private void ApplyImmediate()
        {
            if (TryGetTarget(out WeatherPreset target))
                ApplyImmediate(target);
        }

        private void ApplyImmediate(WeatherPreset target)
        {
            _blending = false;
            _blend = 1f;
            CaptureFrom();
            CaptureTo(target);
            PrepareRainCapacity();
            PrepareTornadoCapacity();
            ApplyBlend(1f);
        }

        private void ApplyWind(WeatherPreset target)
        {
            _toWind = WindFromPreset(target);
        }

        private Vector3 WindFromPreset(WeatherPreset preset)
        {
            float theta = preset.GlobalOrientation * Mathf.Deg2Rad;
            float speedMs = preset.GlobalSpeed * (1000f / 3600f);
            return new Vector3(Mathf.Cos(theta), 0f, Mathf.Sin(theta)) * speedMs;
        }

        private void BeginBlend(WeatherPreset target)
        {
            CaptureFrom();
            CaptureTo(target);
            PrepareRainCapacity();
            PrepareTornadoCapacity();
            SetFog(target.FogEnabled, target.FogColor, target.FogStart, target.FogEnd, target.Mode);
            _blend = 0f;
            _blending = true;
            ApplyBlend(0f);
        }

        private void PrepareRainCapacity()
        {
            if (weatherSystem == null)
                return;

            weatherSystem.EnsureCapacity(Mathf.Max(_fromRain, _toRain, 1));
        }

        private void PrepareTornadoCapacity()
        {
            if (tornadoSystem == null)
                return;

            tornadoSystem.EnsureDebrisCapacity(Mathf.Max(_fromDebris, _toDebris, 1));
        }

        private void CaptureFrom()
        {
            _fromRain = weatherSystem != null ? weatherSystem.ParticleCount : 0;
            _fromTornadoes = tornadoSystem != null ? tornadoSystem.TornadoCount : 0;
            _fromDebris = tornadoSystem != null ? tornadoSystem.DebrisCount : 0;
            _fromTornadoShape = TornadoShape.FromSystem(tornadoSystem);
            _fromLightning = LightningSnapshot.FromSystem(lightningSystem);
            _fromWind = weatherSystem != null ? weatherSystem.Wind : AtmosphericModel.SampleWind();
            _fromClouds = TryGetClouds(out VolumetricClouds clouds)
                ? CloudSnapshot.FromVolume(clouds)
                : CloudSnapshot.FromPreset(new WeatherPreset());
        }

        private void CaptureTo(WeatherPreset target)
        {
            _toRain = Mathf.Max(0, target.RainCount);
            _toTornadoes = Mathf.Clamp(target.TornadoCount, 0, TornadoSystem.MaxTornadoes);
            _toDebris = _toTornadoes > 0 ? Mathf.Max(0, target.TornadoDebrisCount) : 0;
            _toTornadoShape = TornadoShape.FromPreset(target);
            _toLightning = LightningSnapshot.FromPreset(target);
            _toCloudPreset = target.CloudPreset;
            _toClouds = CloudSnapshot.FromPreset(target);
            _toDensity = target.FogDensity;
            ApplyWind(target);
        }

        private void ApplyBlend(float t)
        {
            Vector3 wind = Vector3.Lerp(_fromWind, _toWind, t);
            AtmosphericModel.Instance?.SetWind(wind);

            if (weatherSystem != null)
            {
                weatherSystem.SetWind(wind);
                if (t > 0.5f)
                {
                    float rainT = GetRainT(t);
                    weatherSystem.SetParticleCount(Mathf.RoundToInt(Mathf.Lerp(_fromRain, _toRain, rainT)));
                }
            }

            ApplyTornadoBlend(t, wind);
            ApplyLightningBlend(t);

            if (!TryGetClouds(out VolumetricClouds clouds))
                return;

            CloudSnapshot.Lerp(_fromClouds, _toClouds, t, _curveKeys, clouds);
            OverrideCloudParams(clouds);
            float fogT = GetRainT(t);
            LerpFog(_toDensity, fogT);
            
            if (t >= 1f - Mathf.Epsilon)
            {
                clouds.cloudPreset = _toCloudPreset;
            }

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                VolumeProfile profile = GetProfile();
                if (profile != null)
                    EditorUtility.SetDirty(profile);
                EditorUtility.SetDirty(volume);
            }
#endif
        }

        private void ApplyTornadoBlend(float t, Vector3 wind)
        {
            if (tornadoSystem == null)
                return;

            tornadoSystem.SetWind(wind);

            TornadoShape shape = TornadoShape.Lerp(_fromTornadoShape, _toTornadoShape, t);
            tornadoSystem.SetShape(shape.Radius, shape.Height);
            tornadoSystem.SetForces(shape.SpinSpeed, shape.Updraft);

            // Funnels only swap over at the halfway point, same as the rain count does.
            float fadeIn = GetRainT(t);
            float fadeOut = 1f - Mathf.Clamp01(t * 2f);
            bool showing = t > 0.5f;

            tornadoSystem.SetTornadoCount(showing ? _toTornadoes : _fromTornadoes);
            tornadoSystem.SetDebrisCount(Mathf.RoundToInt(Mathf.Lerp(_fromDebris, _toDebris,
                showing ? fadeIn : 0f)));

            float intensity;
            if (_fromTornadoes > 0 && _toTornadoes > 0)
                intensity = 1f;
            else if (_toTornadoes > 0)
                intensity = fadeIn;
            else
                intensity = fadeOut;

            tornadoSystem.SetIntensity(intensity);
        }

        private void ApplyLightningBlend(float t)
        {
            if (lightningSystem == null)
                return;

            LightningSnapshot lightning = LightningSnapshot.Lerp(_fromLightning, _toLightning, t);
            lightningSystem.SetStrikeRate(lightning.Rate);
            lightningSystem.SetFlashIntensity(lightning.FlashIntensity);
            lightningSystem.SetBranchiness(lightning.Branchiness);
            lightningSystem.SetColors(lightning.CoreColor, lightning.GlowColor);
            lightningSystem.SetThunderVolume(lightning.ThunderVolume);
            lightningSystem.SetIntensity(1f);
        }

        private float GetRainT(float t)
        {
            return Mathf.Max(0, Mathf.Min(1, (t - 0.5f) * 2.0f));
        }

        public void LerpFog(float density, float t)
        {
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, density, t);
        }
        public void SetFog(bool enabled, Color fogColor, float fogStart, float fogEnd, FogMode fogMode)
        {
            if (fogMode == FogMode.Linear)
            {
                RenderSettings.fog = enabled;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogEndDistance = 0;
                RenderSettings.fogStartDistance = 0;
                RenderSettings.fogMode = fogMode;
            }
            else
            {
                RenderSettings.fog = enabled;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogEndDistance = fogStart;
                RenderSettings.fogStartDistance = fogEnd;
                RenderSettings.fogMode = fogMode;
            }
        }

        private bool TryGetClouds(out VolumetricClouds clouds)
        {
            clouds = null;
            VolumeProfile profile = GetProfile();
            return profile != null && profile.TryGet(out clouds);
        }

        private VolumeProfile GetProfile()
        {
            if (volume == null)
                return null;

            return volume.HasInstantiatedProfile()
                ? volume.profile
                : volume.sharedProfile;
        }

        private static void OverrideCloudParams(VolumetricClouds clouds)
        {
            clouds.state.overrideState = true;
            clouds.localClouds.overrideState = true;
            clouds.densityMultiplier.overrideState = true;
            clouds.densityCurve.overrideState = true;
            clouds.shapeFactor.overrideState = true;
            clouds.shapeScale.overrideState = true;
            clouds.erosionFactor.overrideState = true;
            clouds.erosionScale.overrideState = true;
            clouds.erosionCurve.overrideState = true;
            clouds.ambientOcclusionCurve.overrideState = true;
            clouds.microErosion.overrideState = true;
            clouds.microErosionFactor.overrideState = true;
            clouds.microErosionScale.overrideState = true;
            clouds.bottomAltitude.overrideState = true;
            clouds.altitudeRange.overrideState = true;
            clouds.shapeOffset.overrideState = true;
            clouds.earthCurvature.overrideState = true;
            clouds.globalSpeed.overrideState = true;
            clouds.globalOrientation.overrideState = true;
            clouds.shapeSpeedMultiplier.overrideState = true;
            clouds.erosionSpeedMultiplier.overrideState = true;
            clouds.altitudeDistortion.overrideState = true;
            clouds.verticalShapeWindSpeed.overrideState = true;
            clouds.verticalErosionWindSpeed.overrideState = true;
            clouds.ambientLightProbeDimmer.overrideState = true;
            clouds.sunLightDimmer.overrideState = true;
            clouds.erosionOcclusion.overrideState = true;
            clouds.scatteringTint.overrideState = true;
            clouds.powderEffectIntensity.overrideState = true;
            clouds.multiScattering.overrideState = true;
            clouds.shadows.overrideState = true;
            clouds.shadowResolution.overrideState = true;
            clouds.shadowDistance.overrideState = true;
            clouds.shadowOpacity.overrideState = true;
            clouds.shadowOpacityFallback.overrideState = true;
            clouds.temporalAccumulationFactor.overrideState = true;
            clouds.perceptualBlending.overrideState = true;
            clouds.numPrimarySteps.overrideState = true;
            clouds.numLightSteps.overrideState = true;
            clouds.fadeInMode.overrideState = true;
            clouds.fadeInStart.overrideState = true;
            clouds.fadeInDistance.overrideState = true;
        }

        public string[] GetWeathers()
        {
            return presets.Select(preset => preset.Name.ToLower()).ToArray();
        }

        public string CurrentWeatherName
        {
            get
            {
                if (presets == null || presets.Length == 0)
                    return "";

                int index = Mathf.Clamp(currentPreset, 0, presets.Length - 1);
                WeatherPreset preset = presets[index];
                return preset != null && !string.IsNullOrEmpty(preset.Name)
                    ? preset.Name.ToLowerInvariant()
                    : "";
            }
        }

        public bool HasWeather(string name)
        {
            return TryFindPreset(name, out _);
        }

        public bool TrySetWeather(string name)
        {
            if (!TryFindPreset(name, out int index))
                return false;

            currentPreset = index;
            UpdateWeather();
            return true;
        }

        public void SetWeather(string name)
        {
            TrySetWeather(name);
        }

        private bool TryFindPreset(string name, out int index)
        {
            index = -1;
            if (presets == null || string.IsNullOrWhiteSpace(name))
                return false;

            for (int i = 0; i < presets.Length; i++)
            {
                WeatherPreset preset = presets[i];
                if (preset == null || string.IsNullOrEmpty(preset.Name))
                    continue;
                if (!string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                index = i;
                return true;
            }

            return false;
        }

        private struct LightningSnapshot
        {
            public float Rate;
            public float FlashIntensity;
            public float Branchiness;
            public Color CoreColor;
            public Color GlowColor;
            public float ThunderVolume;

            public static LightningSnapshot FromPreset(WeatherPreset preset)
            {
                return new LightningSnapshot
                {
                    Rate = preset.LightningEnabled ? Mathf.Max(0f, preset.StrikesPerMinute) : 0f,
                    FlashIntensity = preset.LightningFlashIntensity,
                    Branchiness = preset.LightningBranchiness,
                    CoreColor = preset.LightningCoreColor,
                    GlowColor = preset.LightningGlowColor,
                    ThunderVolume = preset.ThunderVolume
                };
            }

            public static LightningSnapshot FromSystem(LightningSystem system)
            {
                if (system == null)
                    return FromPreset(new WeatherPreset());

                return new LightningSnapshot
                {
                    Rate = system.StrikeRate,
                    FlashIntensity = system.FlashIntensity,
                    Branchiness = system.Branchiness,
                    CoreColor = system.CoreColor,
                    GlowColor = system.GlowColor,
                    ThunderVolume = system.ThunderVolume
                };
            }

            public static LightningSnapshot Lerp(in LightningSnapshot a, in LightningSnapshot b, float t)
            {
                return new LightningSnapshot
                {
                    Rate = Mathf.Lerp(a.Rate, b.Rate, t),
                    FlashIntensity = Mathf.Lerp(a.FlashIntensity, b.FlashIntensity, t),
                    Branchiness = Mathf.Lerp(a.Branchiness, b.Branchiness, t),
                    CoreColor = Color.Lerp(a.CoreColor, b.CoreColor, t),
                    GlowColor = Color.Lerp(a.GlowColor, b.GlowColor, t),
                    ThunderVolume = Mathf.Lerp(a.ThunderVolume, b.ThunderVolume, t)
                };
            }
        }

        private struct TornadoShape
        {
            public float Radius;
            public float Height;
            public float SpinSpeed;
            public float Updraft;

            public static TornadoShape FromPreset(WeatherPreset preset)
            {
                return new TornadoShape
                {
                    Radius = preset.TornadoRadius,
                    Height = preset.TornadoHeight,
                    SpinSpeed = preset.TornadoSpinSpeed,
                    Updraft = preset.TornadoUpdraft
                };
            }

            public static TornadoShape FromSystem(TornadoSystem system)
            {
                if (system == null)
                    return FromPreset(new WeatherPreset());

                return new TornadoShape
                {
                    Radius = system.Radius,
                    Height = system.Height,
                    SpinSpeed = system.SpinSpeed,
                    Updraft = system.Updraft
                };
            }

            public static TornadoShape Lerp(in TornadoShape a, in TornadoShape b, float t)
            {
                return new TornadoShape
                {
                    Radius = Mathf.Lerp(a.Radius, b.Radius, t),
                    Height = Mathf.Lerp(a.Height, b.Height, t),
                    SpinSpeed = Mathf.Lerp(a.SpinSpeed, b.SpinSpeed, t),
                    Updraft = Mathf.Lerp(a.Updraft, b.Updraft, t)
                };
            }
        }

        private struct CloudSnapshot
        {
            public bool cloudsEnabled;
            public bool localClouds;
            public float densityMultiplier;
            public float shapeFactor;
            public float shapeScale;
            public float erosionFactor;
            public float erosionScale;
            public bool microErosion;
            public float microErosionFactor;
            public float microErosionScale;
            public float bottomAltitude;
            public float altitudeRange;
            public Vector3 shapeOffset;
            public float earthCurvature;
            public float globalSpeed;
            public float globalOrientation;
            public float shapeSpeedMultiplier;
            public float erosionSpeedMultiplier;
            public float altitudeDistortion;
            public float verticalShapeWindSpeed;
            public float verticalErosionWindSpeed;
            public float ambientLightProbeDimmer;
            public float sunLightDimmer;
            public float erosionOcclusion;
            public Color scatteringTint;
            public float powderEffectIntensity;
            public float multiScattering;
            public bool shadows;
            public VolumetricClouds.CloudShadowResolution shadowResolution;
            public float shadowDistance;
            public float shadowOpacity;
            public float shadowOpacityFallback;
            public float temporalAccumulationFactor;
            public float perceptualBlending;
            public int numPrimarySteps;
            public int numLightSteps;
            public VolumetricClouds.CloudFadeInMode fadeInMode;
            public float fadeInStart;
            public float fadeInDistance;
            public AnimationCurve densityCurve;
            public AnimationCurve erosionCurve;
            public AnimationCurve ambientOcclusionCurve;

            public static CloudSnapshot FromVolume(VolumetricClouds clouds)
            {
                return new CloudSnapshot
                {
                    cloudsEnabled = clouds.state.value,
                    localClouds = clouds.localClouds.value,
                    densityMultiplier = clouds.densityMultiplier.value,
                    shapeFactor = clouds.shapeFactor.value,
                    shapeScale = clouds.shapeScale.value,
                    erosionFactor = clouds.erosionFactor.value,
                    erosionScale = clouds.erosionScale.value,
                    microErosion = clouds.microErosion.value,
                    microErosionFactor = clouds.microErosionFactor.value,
                    microErosionScale = clouds.microErosionScale.value,
                    bottomAltitude = clouds.bottomAltitude.value,
                    altitudeRange = clouds.altitudeRange.value,
                    shapeOffset = clouds.shapeOffset.value,
                    earthCurvature = clouds.earthCurvature.value,
                    globalSpeed = clouds.globalSpeed.value,
                    globalOrientation = clouds.globalOrientation.value,
                    shapeSpeedMultiplier = clouds.shapeSpeedMultiplier.value,
                    erosionSpeedMultiplier = clouds.erosionSpeedMultiplier.value,
                    altitudeDistortion = clouds.altitudeDistortion.value,
                    verticalShapeWindSpeed = clouds.verticalShapeWindSpeed.value,
                    verticalErosionWindSpeed = clouds.verticalErosionWindSpeed.value,
                    ambientLightProbeDimmer = clouds.ambientLightProbeDimmer.value,
                    sunLightDimmer = clouds.sunLightDimmer.value,
                    erosionOcclusion = clouds.erosionOcclusion.value,
                    scatteringTint = clouds.scatteringTint.value,
                    powderEffectIntensity = clouds.powderEffectIntensity.value,
                    multiScattering = clouds.multiScattering.value,
                    shadows = clouds.shadows.value,
                    shadowResolution = clouds.shadowResolution.value,
                    shadowDistance = clouds.shadowDistance.value,
                    shadowOpacity = clouds.shadowOpacity.value,
                    shadowOpacityFallback = clouds.shadowOpacityFallback.value,
                    temporalAccumulationFactor = clouds.temporalAccumulationFactor.value,
                    perceptualBlending = clouds.perceptualBlending.value,
                    numPrimarySteps = clouds.numPrimarySteps.value,
                    numLightSteps = clouds.numLightSteps.value,
                    fadeInMode = clouds.fadeInMode.value,
                    fadeInStart = clouds.fadeInStart.value,
                    fadeInDistance = clouds.fadeInDistance.value,
                    densityCurve = CloneCurve(clouds.densityCurve.value),
                    erosionCurve = CloneCurve(clouds.erosionCurve.value),
                    ambientOcclusionCurve = CloneCurve(clouds.ambientOcclusionCurve.value)
                };
            }

            public static CloudSnapshot FromPreset(WeatherPreset preset)
            {
                WeatherPreset source = preset;
                if (preset.CloudPreset != VolumetricClouds.CloudPresets.Custom)
                {
                    source = ClonePreset(preset);
                    source.ApplyCloudLook(preset.CloudPreset);
                }

                return new CloudSnapshot
                {
                    cloudsEnabled = source.CloudsEnabled,
                    localClouds = source.LocalClouds,
                    densityMultiplier = source.DensityMultiplier,
                    shapeFactor = source.ShapeFactor,
                    shapeScale = source.ShapeScale,
                    erosionFactor = source.ErosionFactor,
                    erosionScale = source.ErosionScale,
                    microErosion = source.MicroErosion,
                    microErosionFactor = source.MicroErosionFactor,
                    microErosionScale = source.MicroErosionScale,
                    bottomAltitude = source.BottomAltitude,
                    altitudeRange = source.AltitudeRange,
                    shapeOffset = source.ShapeOffset,
                    earthCurvature = source.EarthCurvature,
                    globalSpeed = source.GlobalSpeed,
                    globalOrientation = source.GlobalOrientation,
                    shapeSpeedMultiplier = source.ShapeSpeedMultiplier,
                    erosionSpeedMultiplier = source.ErosionSpeedMultiplier,
                    altitudeDistortion = source.AltitudeDistortion,
                    verticalShapeWindSpeed = source.VerticalShapeWindSpeed,
                    verticalErosionWindSpeed = source.VerticalErosionWindSpeed,
                    ambientLightProbeDimmer = source.AmbientLightProbeDimmer,
                    sunLightDimmer = source.SunLightDimmer,
                    erosionOcclusion = source.ErosionOcclusion,
                    scatteringTint = source.ScatteringTint,
                    powderEffectIntensity = source.PowderEffectIntensity,
                    multiScattering = source.MultiScattering,
                    shadows = source.Shadows,
                    shadowResolution = source.ShadowResolution,
                    shadowDistance = source.ShadowDistance,
                    shadowOpacity = source.ShadowOpacity,
                    shadowOpacityFallback = source.ShadowOpacityFallback,
                    temporalAccumulationFactor = source.TemporalAccumulationFactor,
                    perceptualBlending = source.PerceptualBlending,
                    numPrimarySteps = source.NumPrimarySteps,
                    numLightSteps = source.NumLightSteps,
                    fadeInMode = source.FadeInMode,
                    fadeInStart = source.FadeInStart,
                    fadeInDistance = source.FadeInDistance,
                    densityCurve = CloneCurve(source.DensityCurve),
                    erosionCurve = CloneCurve(source.ErosionCurve),
                    ambientOcclusionCurve = CloneCurve(source.AmbientOcclusionCurve)
                };
            }

            private static WeatherPreset ClonePreset(WeatherPreset p)
            {
                return new WeatherPreset
                {
                    Name = p.Name,
                    RainCount = p.RainCount,
                    CloudsEnabled = p.CloudsEnabled,
                    LocalClouds = p.LocalClouds,
                    CloudPreset = p.CloudPreset,
                    DensityMultiplier = p.DensityMultiplier,
                    DensityCurve = CloneCurve(p.DensityCurve),
                    ShapeFactor = p.ShapeFactor,
                    ShapeScale = p.ShapeScale,
                    ErosionFactor = p.ErosionFactor,
                    ErosionScale = p.ErosionScale,
                    ErosionCurve = CloneCurve(p.ErosionCurve),
                    AmbientOcclusionCurve = CloneCurve(p.AmbientOcclusionCurve),
                    MicroErosion = p.MicroErosion,
                    MicroErosionFactor = p.MicroErosionFactor,
                    MicroErosionScale = p.MicroErosionScale,
                    BottomAltitude = p.BottomAltitude,
                    AltitudeRange = p.AltitudeRange,
                    ShapeOffset = p.ShapeOffset,
                    EarthCurvature = p.EarthCurvature,
                    GlobalSpeed = p.GlobalSpeed,
                    GlobalOrientation = p.GlobalOrientation,
                    ShapeSpeedMultiplier = p.ShapeSpeedMultiplier,
                    ErosionSpeedMultiplier = p.ErosionSpeedMultiplier,
                    AltitudeDistortion = p.AltitudeDistortion,
                    VerticalShapeWindSpeed = p.VerticalShapeWindSpeed,
                    VerticalErosionWindSpeed = p.VerticalErosionWindSpeed,
                    AmbientLightProbeDimmer = p.AmbientLightProbeDimmer,
                    SunLightDimmer = p.SunLightDimmer,
                    ErosionOcclusion = p.ErosionOcclusion,
                    ScatteringTint = p.ScatteringTint,
                    PowderEffectIntensity = p.PowderEffectIntensity,
                    MultiScattering = p.MultiScattering,
                    Shadows = p.Shadows,
                    ShadowResolution = p.ShadowResolution,
                    ShadowDistance = p.ShadowDistance,
                    ShadowOpacity = p.ShadowOpacity,
                    ShadowOpacityFallback = p.ShadowOpacityFallback,
                    TemporalAccumulationFactor = p.TemporalAccumulationFactor,
                    PerceptualBlending = p.PerceptualBlending,
                    NumPrimarySteps = p.NumPrimarySteps,
                    NumLightSteps = p.NumLightSteps,
                    FadeInMode = p.FadeInMode,
                    FadeInStart = p.FadeInStart,
                    FadeInDistance = p.FadeInDistance
                };
            }

            public static void Lerp(in CloudSnapshot a, in CloudSnapshot b, float t, Keyframe[] keys,
                VolumetricClouds clouds)
            {
                clouds.state.value = t < 0.5f ? a.cloudsEnabled : b.cloudsEnabled;
                clouds.localClouds.value = t < 0.5f ? a.localClouds : b.localClouds;
                clouds.densityMultiplier.value = Mathf.Lerp(a.densityMultiplier, b.densityMultiplier, t);
                clouds.shapeFactor.value = Mathf.Lerp(a.shapeFactor, b.shapeFactor, t);
                clouds.shapeScale.value = Mathf.Lerp(a.shapeScale, b.shapeScale, t);
                clouds.erosionFactor.value = Mathf.Lerp(a.erosionFactor, b.erosionFactor, t);
                clouds.erosionScale.value = Mathf.Lerp(a.erosionScale, b.erosionScale, t);
                clouds.microErosion.value = t < 0.5f ? a.microErosion : b.microErosion;
                clouds.microErosionFactor.value = Mathf.Lerp(a.microErosionFactor, b.microErosionFactor, t);
                clouds.microErosionScale.value = Mathf.Lerp(a.microErosionScale, b.microErosionScale, t);
                clouds.bottomAltitude.value = Mathf.Lerp(a.bottomAltitude, b.bottomAltitude, t);
                clouds.altitudeRange.value = Mathf.Lerp(a.altitudeRange, b.altitudeRange, t);
                clouds.shapeOffset.value = Vector3.Lerp(a.shapeOffset, b.shapeOffset, t);
                clouds.earthCurvature.value = Mathf.Lerp(a.earthCurvature, b.earthCurvature, t);
                clouds.globalSpeed.value = Mathf.Lerp(a.globalSpeed, b.globalSpeed, t);
                clouds.globalOrientation.value = Mathf.LerpAngle(a.globalOrientation, b.globalOrientation, t);
                clouds.shapeSpeedMultiplier.value = Mathf.Lerp(a.shapeSpeedMultiplier, b.shapeSpeedMultiplier, t);
                clouds.erosionSpeedMultiplier.value = Mathf.Lerp(a.erosionSpeedMultiplier, b.erosionSpeedMultiplier, t);
                clouds.altitudeDistortion.value = Mathf.Lerp(a.altitudeDistortion, b.altitudeDistortion, t);
                clouds.verticalShapeWindSpeed.value = Mathf.Lerp(a.verticalShapeWindSpeed, b.verticalShapeWindSpeed, t);
                clouds.verticalErosionWindSpeed.value =
                    Mathf.Lerp(a.verticalErosionWindSpeed, b.verticalErosionWindSpeed, t);
                clouds.ambientLightProbeDimmer.value =
                    Mathf.Lerp(a.ambientLightProbeDimmer, b.ambientLightProbeDimmer, t);
                clouds.sunLightDimmer.value = Mathf.Lerp(a.sunLightDimmer, b.sunLightDimmer, t);
                clouds.erosionOcclusion.value = Mathf.Lerp(a.erosionOcclusion, b.erosionOcclusion, t);
                clouds.scatteringTint.value = Color.Lerp(a.scatteringTint, b.scatteringTint, t);
                clouds.powderEffectIntensity.value = Mathf.Lerp(a.powderEffectIntensity, b.powderEffectIntensity, t);
                clouds.multiScattering.value = Mathf.Lerp(a.multiScattering, b.multiScattering, t);
                clouds.shadows.value = t < 0.5f ? a.shadows : b.shadows;
                clouds.shadowResolution.value = t < 0.5f ? a.shadowResolution : b.shadowResolution;
                clouds.shadowDistance.value = Mathf.Lerp(a.shadowDistance, b.shadowDistance, t);
                clouds.shadowOpacity.value = Mathf.Lerp(a.shadowOpacity, b.shadowOpacity, t);
                clouds.shadowOpacityFallback.value = Mathf.Lerp(a.shadowOpacityFallback, b.shadowOpacityFallback, t);
                clouds.temporalAccumulationFactor.value =
                    Mathf.Lerp(a.temporalAccumulationFactor, b.temporalAccumulationFactor, t);
                clouds.perceptualBlending.value = Mathf.Lerp(a.perceptualBlending, b.perceptualBlending, t);
                clouds.numPrimarySteps.value = Mathf.RoundToInt(Mathf.Lerp(a.numPrimarySteps, b.numPrimarySteps, t));
                clouds.numLightSteps.value = Mathf.RoundToInt(Mathf.Lerp(a.numLightSteps, b.numLightSteps, t));
                clouds.fadeInMode.value = t < 0.5f ? a.fadeInMode : b.fadeInMode;
                clouds.fadeInStart.value = Mathf.Lerp(a.fadeInStart, b.fadeInStart, t);
                clouds.fadeInDistance.value = Mathf.Lerp(a.fadeInDistance, b.fadeInDistance, t);
                clouds.densityCurve.value = LerpCurve(a.densityCurve, b.densityCurve, t, keys);
                clouds.erosionCurve.value = LerpCurve(a.erosionCurve, b.erosionCurve, t, keys);
                clouds.ambientOcclusionCurve.value =
                    LerpCurve(a.ambientOcclusionCurve, b.ambientOcclusionCurve, t, keys);
            }

            private static AnimationCurve CloneCurve(AnimationCurve curve)
            {
                if (curve == null)
                    return new AnimationCurve();

                return new AnimationCurve(curve.keys)
                {
                    preWrapMode = curve.preWrapMode,
                    postWrapMode = curve.postWrapMode
                };
            }

            private static AnimationCurve LerpCurve(AnimationCurve a, AnimationCurve b, float t, Keyframe[] keys)
            {
                int n = keys.Length;
                for (int i = 0; i < n; i++)
                {
                    float x = i / (float)(n - 1);
                    float av = a != null ? a.Evaluate(x) : 0f;
                    float bv = b != null ? b.Evaluate(x) : 0f;
                    keys[i] = new Keyframe(x, Mathf.Lerp(av, bv, t));
                }

                return new AnimationCurve(keys);
            }
        }
    }
}
