using UnityEngine;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Point sample of the atmosphere at a given geometric altitude.
    /// Density follows the requested exponential model; temperature uses a tropospheric gradient
    /// so Mach and speed of sound remain physically meaningful.
    /// </summary>
    public readonly struct AtmosphereSample
    {
        /// <summary>Geometric altitude above the model's sea-level datum, metres.</summary>
        public readonly float Altitude;

        /// <summary>Static temperature, Kelvin.</summary>
        public readonly float Temperature;

        /// <summary>Approximate static pressure, Pascals (ρ R T, not a full ISA hydrostatic integrate).</summary>
        public readonly float Pressure;

        /// <summary>Air density ρ(h), kg/m³.</summary>
        public readonly float Density;

        /// <summary>Speed of sound, m/s. a = √(γ R T).</summary>
        public readonly float SpeedOfSound;

        public AtmosphereSample(float altitude, float temperature, float pressure, float density, float speedOfSound)
        {
            Altitude = altitude;
            Temperature = temperature;
            Pressure = pressure;
            Density = density;
            SpeedOfSound = speedOfSound;
        }

        /// <summary>Dynamic pressure q = ½ ρ V² for true airspeed <paramref name="trueAirspeed"/> (m/s).</summary>
        public float DynamicPressure(float trueAirspeed)
        {
            return 0.5f * Density * trueAirspeed * trueAirspeed;
        }
    }

    /// <summary>
    /// Scene-level atmosphere. If none is present, <see cref="SampleAt"/> uses built-in ISA-like defaults
    /// so a prefab can fly in an empty scene.
    ///
    /// Density: ρ(h) = ρ₀ exp(−h / h_scale).
    /// Temperature: T(h) = max(T_tropopause, T₀ − L h)  (linear tropospheric lapse, then isothermal).
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Atmospheric Model")]
    public sealed class AtmosphericModel : MonoBehaviour
    {
        public const float StandardSeaLevelDensity = 1.225f;
        public const float StandardSeaLevelTemperature = 288.15f;
        public const float StandardSeaLevelPressure = 101325f;
        public const float StandardLapseRate = 0.0065f;
        public const float TropopauseTemperature = 216.65f;
        public const float SpecificGasConstantAir = 287.05287f;
        public const float RatioOfSpecificHeats = 1.4f;
        public const float StandardScaleHeight = 8500f;
        public const float StandardGravity = 9.80665f;

        public static AtmosphericModel Instance { get; private set; }

        [Header("Datum")]
        [Tooltip("World-Y treated as sea level (metres). Altitude = position.y − seaLevelY.")]
        [SerializeField] private float seaLevelY;

        [Header("Density ρ(h) = ρ₀ exp(−h / h_scale)")]
        [Tooltip("Sea-level density ρ₀, kg/m³. ISA = 1.225.")]
        [SerializeField] private float seaLevelDensity = StandardSeaLevelDensity;

        [Tooltip("Exponential scale height h_scale, metres. ~8500 m reproduces the troposphere well.")]
        [SerializeField] private float scaleHeight = StandardScaleHeight;

        [Header("Temperature gradient")]
        [Tooltip("Sea-level temperature T₀, Kelvin. ISA = 288.15 K (15 °C).")]
        [SerializeField] private float seaLevelTemperature = StandardSeaLevelTemperature;

        [Tooltip("Tropospheric lapse rate L, K/m. ISA = 0.0065. T = T₀ − L h until tropopause.")]
        [SerializeField] private float lapseRate = StandardLapseRate;

        [Tooltip("Temperature floor (tropopause), Kelvin. ISA = 216.65 K.")]
        [SerializeField] private float tropopauseTemperature = TropopauseTemperature;

        [Header("Wind (world frame, m/s)")]
        [Tooltip("Uniform wind added to every flow sample. +X is east if you treat Unity X as east.")]
        [SerializeField] private Vector3 windWorld;

        [Header("Gravity")]
        [Tooltip("If true, uses this vector instead of Physics.gravity. Magnitude is m/s².")]
        [SerializeField] private bool overrideGravity;

        [SerializeField] private Vector3 gravityOverride = new Vector3(0f, -StandardGravity, 0f);

        public float SeaLevelY => seaLevelY;
        public float SeaLevelDensity => seaLevelDensity;
        public Vector3 WindWorld => windWorld;

        public Vector3 Gravity
        {
            get
            {
                if (overrideGravity)
                    return gravityOverride;
                return Physics.gravity;
            }
        }

        private void OnEnable()
        {
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
                Instance = null;
        }

        /// <summary>Sample the atmosphere at a world-space point (uses Instance or built-in defaults).</summary>
        public static AtmosphereSample SampleAt(Vector3 worldPosition)
        {
            AtmosphericModel model = Instance;
            float sea = model ? model.seaLevelY : 0f;
            float alt = worldPosition.y - sea;
            return model ? model.SampleAltitude(alt) : SampleAltitudeDefault(alt);
        }

        public static Vector3 SampleWind()
        {
            AtmosphericModel model = Instance;
            return model ? model.windWorld : Vector3.zero;
        }

        public static Vector3 SampleGravity()
        {
            AtmosphericModel model = Instance;
            return model ? model.Gravity : Physics.gravity;
        }

        public AtmosphereSample SampleAltitude(float geometricAltitude)
        {
            return Evaluate(
                geometricAltitude,
                seaLevelDensity,
                scaleHeight,
                seaLevelTemperature,
                lapseRate,
                tropopauseTemperature);
        }

        public static AtmosphereSample SampleAltitudeDefault(float geometricAltitude)
        {
            return Evaluate(
                geometricAltitude,
                StandardSeaLevelDensity,
                StandardScaleHeight,
                StandardSeaLevelTemperature,
                StandardLapseRate,
                TropopauseTemperature);
        }

        /// <summary>q = ½ ρ V² at the given world position and true airspeed.</summary>
        public static float DynamicPressure(Vector3 worldPosition, float trueAirspeed)
        {
            return SampleAt(worldPosition).DynamicPressure(trueAirspeed);
        }

        private static AtmosphereSample Evaluate(
            float altitude,
            float rho0,
            float hScale,
            float t0,
            float lapse,
            float tFloor)
        {
            float h = altitude;
            float rho = rho0 * Mathf.Exp(-h / Mathf.Max(1f, hScale));
            if (rho < 0.0005f)
                rho = 0.0005f;

            float temperature = t0 - lapse * h;
            if (temperature < tFloor)
                temperature = tFloor;

            float pressure = rho * SpecificGasConstantAir * temperature;
            float speedOfSound = Mathf.Sqrt(RatioOfSpecificHeats * SpecificGasConstantAir * temperature);

            return new AtmosphereSample(h, temperature, pressure, rho, speedOfSound);
        }
    }
}
