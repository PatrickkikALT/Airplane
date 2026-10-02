using Airplane.FlightSimulation;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace Airplane.Weather
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-100)]
    [AddComponentMenu("Airplane/Weather/Lightning System")]
    public sealed class LightningSystem : MonoBehaviour
    {
        public const int MaxBranches = 6;
        public const string EnvironmentFlashPrefKey = "EnvironmentFlash";
        private const int MaxPulses = 4;
        private const int ThunderSources = 8;
        private const int RibbonSegments = 48;
        private const float NoCloudCeiling = 1e7f;

        private static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int StartId = Shader.PropertyToID("_Start");
        private static readonly int EndId = Shader.PropertyToID("_End");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int JitterId = Shader.PropertyToID("_Jitter");
        private static readonly int WidthId = Shader.PropertyToID("_Width");
        private static readonly int BranchTId = Shader.PropertyToID("_BranchT");
        private static readonly int BranchSeedId = Shader.PropertyToID("_BranchSeed");
        private static readonly int BranchLengthId = Shader.PropertyToID("_BranchLength");
        private static readonly int BranchSpreadId = Shader.PropertyToID("_BranchSpread");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int EmissionId = Shader.PropertyToID("_Emission");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int LightningFlashId = Shader.PropertyToID("_LightningFlash");
        private const float LumenScale = 4000f;

        [SerializeField] private Material boltMaterialOverride;
        [SerializeField] private Camera cameraOverride;

        [Header("Rate")]
        [SerializeField] [Min(0f)] private float strikesPerMinute;
        [SerializeField] [Range(1, 9999)] private int maxLiveStrikes = 4;
        [SerializeField] [Range(0f, 1f)] private float groundStrikeChance = 0.55f;

        [Header("Channel")]
        [SerializeField] [Min(0.1f)] private float boltWidth = 5f;
        [SerializeField] [Range(0f, 0.5f)] private float jitter = 0.075f;
        [SerializeField] [Range(0f, 1f)] private float branchiness = 0.6f;
        [SerializeField] [Range(0f, 3f)] private float branchSpread = 0.85f;
        [SerializeField] private Color coreColor = new(1f, 1f, 1f, 1f);
        [SerializeField] private Color glowColor = new(0.55f, 0.7f, 1f, 1f);
        [SerializeField] [Min(0f)] private float emission = 8f;

        [Header("Placement")]
        [SerializeField] [Min(0f)] private float minDistance = 500f;
        [SerializeField] [Min(0f)] private float maxDistance = 6000f;
        [SerializeField] private float cloudAltitudeFallback = 1500f;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] [Min(1f)] private float groundRayLength = 6000f;

        [Header("Flash")]
        [SerializeField] [Min(0f)] private float flashIntensity = 1f;
        [SerializeField] [Min(0f)] private float lightIntensity = 900f;
        [SerializeField] [Min(1f)] private float lightRange = 4000f;
        [SerializeField] [Min(0f)] private float environmentFlash = 1f;
        [SerializeField] [Min(0.01f)] private float strikeLifetime = 1.1f;
        [SerializeField] [Min(0.001f)] private float drawTime = 0.04f;

        [Header("Thunder")]
        [SerializeField] private AudioClip[] closeThunderClips;
        [SerializeField] private AudioClip[] distantThunderClips;
        [SerializeField] [Range(0f, 1f)] private float thunderVolume = 1f;
        [SerializeField] [Min(0f)] private float closeThunderDistance = 1200f;

        public float StrikeRate => strikesPerMinute;
        public float FlashIntensity => flashIntensity;
        public float Branchiness => branchiness;
        public float ThunderVolume => thunderVolume;
        public Color CoreColor => coreColor;
        public Color GlowColor => glowColor;

        private Strike[] _strikes;
        private AudioSource[] _thunder;
        private Material _boltMaterial;
        private Material _runtimeMaterial;
        private Mesh _ribbon;
        private float _spawnAccumulator;
        private int _thunderCursor;
        private float _intensity = 1f;
        private bool _environmentApplied;
        private Color _ambientSkyAdd;
        private Color _ambientEquatorAdd;
        private Color _ambientGroundAdd;
        private Color _fogAdd;

        private void OnEnable()
        {
            EnsurePools();
        }

        private void OnDisable()
        {
            Teardown();
        }

        private void Update()
        {
            RevertEnvironmentFlash();

            Camera cam = ResolveCamera();
            if (!cam)
                return;

            EnsurePools();

            float dt = Time.deltaTime;
            if (strikesPerMinute > 0f && _intensity > 0.001f)
            {
                _spawnAccumulator += strikesPerMinute / 60f * _intensity * dt;
                while (_spawnAccumulator >= 1f)
                {
                    _spawnAccumulator -= 1f;
                    TrySpawn(cam);
                }
            }
            else
            {
                _spawnAccumulator = 0f;
            }

            for (int i = 0; i < _strikes.Length; i++)
                UpdateStrike(_strikes[i], cam, dt);
        }

        private void LateUpdate()
        {
            ApplyEnvironmentFlash(ResolveCamera());
        }

        private void UpdateStrike(Strike strike, Camera cam, float dt)
        {
            if (!strike.Active)
                return;

            strike.Age += dt;
            if (strike.Age >= strike.Lifetime)
            {
                strike.Active = false;
                if (strike.Flash)
                    strike.Flash.enabled = false;
                return;
            }

            float envelope = strike.Evaluate();
            float progress = Mathf.Clamp01(strike.Age / Mathf.Max(drawTime, 0.001f));
            UpdateFlash(strike, cam, envelope);
            DrawStrike(strike, envelope * flashIntensity * _intensity, progress);
        }

        private void UpdateFlash(Strike strike, Camera cam, float envelope)
        {
            Light flash = strike.Flash;
            if (!flash)
                return;

            if (envelope <= 0.002f)
            {
                flash.enabled = false;
                return;
            }

            Vector3 position = Vector3.Lerp(strike.Start, strike.End, 0.62f);
            flash.transform.position = position;
            float reach = cam ? Vector3.Distance(position, cam.transform.position) : lightRange;
            flash.range = Mathf.Max(lightRange, reach * 1.8f);
            flash.color = Color.Lerp(glowColor, coreColor, 0.8f);
            flash.intensity = lightIntensity * LumenScale * flashIntensity * _intensity * envelope;
            flash.enabled = true;
        }

        private void DrawStrike(Strike strike, float intensity, float progress)
        {
            if (intensity <= 0.002f || !ResolveMaterial())
                return;

            Mesh mesh = ResolveRibbon();
            Bounds bounds = strike.Bounds;

            for (int i = 0; i <= strike.BranchCount; i++)
            {
                bool trunk = i == 0;
                MaterialPropertyBlock props = strike.Props[i];
                props.Clear();
                props.SetColor(CoreColorId, coreColor);
                props.SetColor(GlowColorId, glowColor);
                props.SetVector(StartId, strike.Start);
                props.SetVector(EndId, strike.End);
                props.SetFloat(SeedId, strike.Seed);
                props.SetFloat(JitterId, jitter);
                props.SetFloat(WidthId, boltWidth * (trunk ? 1f : 0.45f));
                props.SetFloat(BranchTId, trunk ? -1f : strike.BranchT[i - 1]);
                props.SetFloat(BranchSeedId, trunk ? 0f : strike.BranchSeed[i - 1]);
                props.SetFloat(BranchLengthId, trunk ? 0f : strike.BranchLength[i - 1]);
                props.SetFloat(BranchSpreadId, branchSpread);
                props.SetFloat(IntensityId, intensity * (trunk ? 1f : 0.6f));
                props.SetFloat(EmissionId, emission);
                props.SetFloat(ProgressId, progress);

                RenderParams rp = new(_boltMaterial)
                {
                    worldBounds = bounds,
                    layer = gameObject.layer,
                    renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask,
                    shadowCastingMode = ShadowCastingMode.Off,
                    receiveShadows = false,
                    lightProbeUsage = LightProbeUsage.Off,
                    reflectionProbeUsage = ReflectionProbeUsage.Off,
                    matProps = props
                };

                Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.identity);
            }
        }

        private void TrySpawn(Camera cam)
        {
            Strike strike = null;
            int live = 0;
            for (int i = 0; i < _strikes.Length; i++)
            {
                if (_strikes[i].Active)
                    live++;
                else if (strike == null)
                    strike = _strikes[i];
            }

            if (strike == null || live >= maxLiveStrikes)
                return;

            Vector3 camPos = cam.transform.position;
            float angle = Random.value * Mathf.PI * 2f;
            float distance = Random.Range(Mathf.Min(minDistance, maxDistance), Mathf.Max(minDistance, maxDistance));
            Vector3 ground = camPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;

            float cloudY = ResolveCloudBase();
            ground.y = ResolveGround(ground, cloudY);

            bool toGround = Random.value < groundStrikeChance;
            strike.Start = new Vector3(ground.x, cloudY, ground.z);
            if (toGround)
            {
                strike.End = ground;
            }
            else
            {
                float spread = Random.Range(300f, 1400f);
                float crawl = Random.value * Mathf.PI * 2f;
                strike.End = strike.Start
                             + new Vector3(Mathf.Cos(crawl), 0f, Mathf.Sin(crawl)) * spread
                             + Vector3.up * Random.Range(-120f, 260f);
            }

            strike.Seed = Random.value * 999.173f;
            strike.Age = 0f;
            strike.Lifetime = strikeLifetime * Random.Range(0.75f, 1.25f);
            strike.Active = true;

            int maxBranches = Mathf.RoundToInt(branchiness * MaxBranches);
            strike.BranchCount = maxBranches > 0 ? Random.Range(maxBranches / 2, maxBranches + 1) : 0;
            for (int i = 0; i < strike.BranchCount; i++)
            {
                strike.BranchT[i] = Random.Range(0.15f, 0.85f);
                strike.BranchSeed[i] = Random.value * 999.173f;
                strike.BranchLength[i] = Random.Range(0.15f, 0.45f);
            }

            strike.BuildPulses();
            strike.Bounds = BuildBounds(strike);

            if (strike.Flash)
                strike.Flash.enabled = true;

            PlayThunder(strike, camPos, toGround);
        }

        private static Bounds BuildBounds(Strike strike)
        {
            Bounds bounds = new(strike.Start, Vector3.one);
            bounds.Encapsulate(strike.End);
            bounds.Expand(Vector3.Distance(strike.Start, strike.End) * 0.6f);
            return bounds;
        }

        private void PlayThunder(Strike strike, Vector3 listener, bool toGround)
        {
            Vector3 origin = Vector3.Lerp(strike.Start, strike.End, 0.4f);
            float distance = Vector3.Distance(origin, listener);

            AudioClip clip = PickThunderClip(distance);
            if (!clip || thunderVolume <= 0.001f || _thunder == null)
                return;

            AudioSource source = _thunder[_thunderCursor];
            _thunderCursor = (_thunderCursor + 1) % _thunder.Length;
            if (source.isPlaying)
                return;

            float speedOfSound = AtmosphericModel.SampleAt(origin).SpeedOfSound;
            source.transform.position = origin;
            source.clip = clip;
            source.maxDistance = Mathf.Max(maxDistance * 1.5f, 1f);
            source.volume = thunderVolume * _intensity * (toGround ? 1f : 0.7f);
            source.pitch = Random.Range(0.9f, 1.1f);
            source.PlayDelayed(distance / Mathf.Max(speedOfSound, 1f));
        }

        private AudioClip PickThunderClip(float distance)
        {
            bool close = distance <= closeThunderDistance;
            AudioClip[] preferred = close ? closeThunderClips : distantThunderClips;
            AudioClip[] fallback = close ? distantThunderClips : closeThunderClips;

            if (preferred != null && preferred.Length > 0)
                return preferred[Random.Range(0, preferred.Length)];
            if (fallback != null && fallback.Length > 0)
                return fallback[Random.Range(0, fallback.Length)];
            return null;
        }

        private float ResolveCloudBase()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            VolumetricClouds clouds = stack?.GetComponent<VolumetricClouds>();
            if (clouds == null || !clouds.state.value)
                return cloudAltitudeFallback;

            float sea = AtmosphericModel.Instance ? AtmosphericModel.Instance.SeaLevelY : 0f;
            clouds.GetCombinedAltitudeBounds(out float lowestBottom, out _);
            float ceiling = sea + lowestBottom;
            return ceiling >= NoCloudCeiling ? cloudAltitudeFallback : ceiling;
        }

        private float ResolveGround(Vector3 position, float fromY)
        {
            Vector3 origin = new(position.x, fromY, position.z);
            return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRayLength, groundMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : AtmosphericModel.Instance
                    ? AtmosphericModel.Instance.SeaLevelY
                    : 0f;
        }

        private Camera ResolveCamera()
        {
            if (cameraOverride)
                return cameraOverride;
            if (Camera.main)
                return Camera.main;
            return Camera.current;
        }

        private Material ResolveMaterial()
        {
            if (boltMaterialOverride)
            {
                _boltMaterial = boltMaterialOverride;
                return _boltMaterial;
            }

            if (_runtimeMaterial)
            {
                _boltMaterial = _runtimeMaterial;
                return _boltMaterial;
            }

            Shader shader = Shader.Find("Airplane/Weather/Lightning Bolt URP");
            if (!shader)
                return null;

            _runtimeMaterial = new Material(shader)
            {
                name = "LightningBoltRuntime",
                hideFlags = HideFlags.HideAndDontSave
            };
            _boltMaterial = _runtimeMaterial;
            return _boltMaterial;
        }

        private Mesh ResolveRibbon()
        {
            if (_ribbon)
                return _ribbon;

            int verts = (RibbonSegments + 1) * 2;
            var positions = new Vector3[verts];
            var uvs = new Vector2[verts];
            var triangles = new int[RibbonSegments * 6];

            for (int i = 0; i <= RibbonSegments; i++)
            {
                float t = i / (float)RibbonSegments;
                positions[i * 2] = new Vector3(0f, t, 0f);
                positions[i * 2 + 1] = new Vector3(1f, t, 0f);
                uvs[i * 2] = new Vector2(0f, t);
                uvs[i * 2 + 1] = new Vector2(1f, t);
            }

            int tri = 0;
            for (int i = 0; i < RibbonSegments; i++)
            {
                int b = i * 2;
                triangles[tri++] = b;
                triangles[tri++] = b + 2;
                triangles[tri++] = b + 1;
                triangles[tri++] = b + 1;
                triangles[tri++] = b + 2;
                triangles[tri++] = b + 3;
            }

            _ribbon = new Mesh { name = "LightningRibbon" };
            _ribbon.vertices = positions;
            _ribbon.uv = uvs;
            _ribbon.triangles = triangles;
            _ribbon.bounds = new Bounds(Vector3.zero, Vector3.one * 10f);
            return _ribbon;
        }

        private void EnsurePools()
        {
            if (_strikes == null || _strikes.Length != maxLiveStrikes)
            {
                DestroyStrikes();
                _strikes = new Strike[maxLiveStrikes];
                for (int i = 0; i < _strikes.Length; i++)
                    _strikes[i] = CreateStrike(i);
            }

            if (_thunder != null)
                return;

            _thunder = new AudioSource[ThunderSources];
            for (int i = 0; i < _thunder.Length; i++)
            {
                GameObject go = new($"Thunder{i}");
                go.transform.SetParent(transform, false);
                go.hideFlags = HideFlags.HideAndDontSave;

                AudioSource source = go.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = 1f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 50f;
                source.dopplerLevel = 0f;
                _thunder[i] = source;
            }
        }

        private Strike CreateStrike(int index)
        {
            GameObject go = new($"LightningFlash{index}");
            go.transform.SetParent(transform, false);
            go.hideFlags = HideFlags.HideAndDontSave;

            Light flash = go.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.renderMode = LightRenderMode.ForcePixel;
            flash.shadows = LightShadows.None;
            flash.range = lightRange;
            flash.enabled = false;

            Strike strike = new()
            {
                Flash = flash,
                Props = new MaterialPropertyBlock[MaxBranches + 1],
                BranchT = new float[MaxBranches],
                BranchSeed = new float[MaxBranches],
                BranchLength = new float[MaxBranches],
                PulseStart = new float[MaxPulses],
                PulseDecay = new float[MaxPulses]
            };

            for (int i = 0; i < strike.Props.Length; i++)
                strike.Props[i] = new MaterialPropertyBlock();

            return strike;
        }

        private void DestroyStrikes()
        {
            if (_strikes == null)
                return;

            foreach (Strike strike in _strikes)
            {
                if (strike?.Flash)
                    DestroyNow(strike.Flash.gameObject);
            }

            _strikes = null;
        }

        private void Teardown()
        {
            RevertEnvironmentFlash();
            Shader.SetGlobalColor(LightningFlashId, Color.black);
            DestroyStrikes();

            if (_thunder != null)
            {
                foreach (AudioSource source in _thunder)
                {
                    if (source)
                        DestroyNow(source.gameObject);
                }

                _thunder = null;
            }

            if (_ribbon)
            {
                DestroyNow(_ribbon);
                _ribbon = null;
            }

            if (_runtimeMaterial)
            {
                DestroyNow(_runtimeMaterial);
                _runtimeMaterial = null;
            }

            _boltMaterial = null;
        }

        private static void DestroyNow(Object target)
        {
            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
        }

        public void SetStrikeRate(float perMinute)
        {
            strikesPerMinute = Mathf.Max(0f, perMinute);
        }

        public void SetIntensity(float value)
        {
            _intensity = Mathf.Clamp01(value);
        }

        public void SetFlashIntensity(float value)
        {
            flashIntensity = Mathf.Max(0f, value);
        }

        public void SetBranchiness(float value)
        {
            branchiness = Mathf.Clamp01(value);
        }

        public void SetColors(Color core, Color glow)
        {
            coreColor = core;
            glowColor = glow;
        }

        public void SetThunderVolume(float value)
        {
            thunderVolume = Mathf.Clamp01(value);
        }

        private void ApplyEnvironmentFlash(Camera cam)
        {
            Color flash = EvaluateEnvironmentFlash(cam);
            Shader.SetGlobalColor(LightningFlashId, flash);
            if (flash.maxColorComponent <= 0.001f)
                return;

            _ambientSkyAdd = flash;
            _ambientEquatorAdd = flash * 0.7f;
            _ambientGroundAdd = flash * 0.4f;
            _fogAdd = flash * 0.5f;
            _ambientSkyAdd.a = 0f;
            _ambientEquatorAdd.a = 0f;
            _ambientGroundAdd.a = 0f;
            _fogAdd.a = 0f;

            RenderSettings.ambientSkyColor += _ambientSkyAdd;
            RenderSettings.ambientEquatorColor += _ambientEquatorAdd;
            RenderSettings.ambientGroundColor += _ambientGroundAdd;
            RenderSettings.fogColor += _fogAdd;
            _environmentApplied = true;
        }

        private Color EvaluateEnvironmentFlash(Camera cam)
        {
            float flashScale = Mathf.Max(0f, PlayerPrefs.GetFloat(EnvironmentFlashPrefKey, environmentFlash));
            if (_strikes == null || flashScale <= 0f || !cam)
                return Color.black;

            Color tint = Color.Lerp(glowColor, coreColor, 0.7f);
            tint.a = 0f;
            Color sum = Color.black;
            float weight = 0f;
            Vector3 camPos = cam.transform.position;
            float horizon = Mathf.Max(maxDistance, 1f);

            for (int i = 0; i < _strikes.Length; i++)
            {
                Strike strike = _strikes[i];
                if (!strike.Active)
                    continue;

                float envelope = strike.Evaluate();
                if (envelope <= 0.002f)
                    continue;

                Vector3 origin = Vector3.Lerp(strike.Start, strike.End, 0.62f);
                float normalized = Vector3.Distance(origin, camPos) / horizon;
                float falloff = 1f / (1f + normalized * normalized * 3f);
                float contribution = envelope * flashIntensity * _intensity * falloff * flashScale;
                sum += tint * contribution;
                weight = Mathf.Max(weight, contribution);
            }

            float cap = 1.6f * Mathf.Max(1f, flashScale);
            sum.r = Mathf.Min(sum.r, cap);
            sum.g = Mathf.Min(sum.g, cap);
            sum.b = Mathf.Min(sum.b, cap);
            sum.a = Mathf.Clamp01(weight);
            return sum;
        }

        private void RevertEnvironmentFlash()
        {
            if (!_environmentApplied)
                return;

            RenderSettings.ambientSkyColor -= _ambientSkyAdd;
            RenderSettings.ambientEquatorColor -= _ambientEquatorAdd;
            RenderSettings.ambientGroundColor -= _ambientGroundAdd;
            RenderSettings.fogColor -= _fogAdd;
            _ambientSkyAdd = Color.black;
            _ambientEquatorAdd = Color.black;
            _ambientGroundAdd = Color.black;
            _fogAdd = Color.black;
            _environmentApplied = false;
        }

        private sealed class Strike
        {
            public bool Active;
            public Vector3 Start;
            public Vector3 End;
            public Bounds Bounds;
            public float Seed;
            public float Age;
            public float Lifetime;
            public int BranchCount;
            public float[] BranchT;
            public float[] BranchSeed;
            public float[] BranchLength;
            public float[] PulseStart;
            public float[] PulseDecay;
            public int PulseCount;
            public Light Flash;
            public MaterialPropertyBlock[] Props;

            public void BuildPulses()
            {
                PulseCount = Random.Range(2, MaxPulses + 1);
                PulseStart[0] = 0f;
                PulseDecay[0] = Random.Range(0.05f, 0.11f);
                for (int i = 1; i < PulseCount; i++)
                {
                    PulseStart[i] = Random.Range(0.04f, 0.55f) * Lifetime;
                    PulseDecay[i] = Random.Range(0.025f, 0.08f);
                }
            }

            public float Evaluate()
            {
                float value = 0f;
                for (int i = 0; i < PulseCount; i++)
                {
                    float since = Age - PulseStart[i];
                    if (since < 0f)
                        continue;

                    value = Mathf.Max(value, Mathf.Exp(-since / PulseDecay[i]));
                }

                float tail = 1f - Mathf.SmoothStep(0.45f, 0.72f, Age / Mathf.Max(Lifetime, 1e-4f));
                float shaped = value * tail;
                return shaped < 0.05f ? 0f : shaped;
            }
        }
    }
}
