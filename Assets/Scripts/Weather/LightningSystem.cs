using Airplane.FlightSimulation;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace Airplane.Weather
{
    /// <summary>
    /// Spawns lightning strikes around the camera: a procedural bolt drawn by
    /// <c>Airplane/Weather/Lightning Bolt URP</c>, a flash light, and thunder delayed by
    /// the real travel time of sound.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Weather/Lightning System")]
    public sealed class LightningSystem : MonoBehaviour
    {
        public const int MaxBranches = 6;
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
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");

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
                UpdateStrike(_strikes[i], dt);
        }

        private void UpdateStrike(Strike strike, float dt)
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

            if (strike.Flash)
            {
                strike.Flash.enabled = envelope > 0.002f;
                strike.Flash.intensity = lightIntensity * flashIntensity * _intensity * envelope;
                strike.Flash.color = glowColor;
            }

            DrawStrike(strike, envelope * flashIntensity * _intensity, progress);
        }

        private void DrawStrike(Strike strike, float intensity, float progress)
        {
            if (intensity <= 0.002f || !ResolveMaterial())
                return;

            Mesh mesh = ResolveRibbon();
            Bounds bounds = strike.Bounds;

            // index 0 is the trunk, the rest are branches hanging off it
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
                // intra-cloud: crawls sideways through the deck instead of reaching down
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
            {
                strike.Flash.transform.position = Vector3.Lerp(strike.Start, strike.End, 0.25f);
                strike.Flash.range = lightRange;
                strike.Flash.enabled = true;
            }

            PlayThunder(strike, camPos, toGround);
        }

        private static Bounds BuildBounds(Strike strike)
        {
            Bounds bounds = new(strike.Start, Vector3.one);
            bounds.Encapsulate(strike.End);
            // the shader pushes the channel sideways off the straight line
            bounds.Expand(Vector3.Distance(strike.Start, strike.End) * 0.6f);
            return bounds;
        }

        /// <summary>Thunder trails the flash by however long sound needs to cover the distance.</summary>
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
            float ceiling = sea + clouds.bottomAltitude.value;
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

        /// <summary>Flat ribbon: the shader bends it along the channel, so only the UVs matter.</summary>
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

            /// <summary>Real strikes are several return strokes down one channel, not a single fade.</summary>
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

                float tail = 1f - Mathf.SmoothStep(0.65f, 1f, Age / Mathf.Max(Lifetime, 1e-4f));
                return value * tail;
            }
        }
    }
}
