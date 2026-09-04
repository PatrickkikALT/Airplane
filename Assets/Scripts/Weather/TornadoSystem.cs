using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

namespace Airplane.Weather
{
    /// <summary>
    /// Drives one or more tornadoes: a GPU vortex simulation for the debris and a
    /// stack of noise shells for the funnel itself. Mirrors <see cref="WeatherSystem" />.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Weather/Tornado System")]
    public sealed class TornadoSystem : MonoBehaviour
    {
        public const int ThreadGroupSize = 256;
        public const int MaxTornadoes = 150;
        private const int MaxShells = 12;

        private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
        private static readonly int TornadoesId = Shader.PropertyToID("_Tornadoes");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int SimTimeId = Shader.PropertyToID("_SimTime");
        private static readonly int ParticleCountId = Shader.PropertyToID("_ParticleCount");
        private static readonly int TornadoCountId = Shader.PropertyToID("_TornadoCount");
        private static readonly int GravityId = Shader.PropertyToID("_Gravity");
        private static readonly int WindId = Shader.PropertyToID("_Wind");
        private static readonly int TurbulenceId = Shader.PropertyToID("_Turbulence");
        private static readonly int SpinSpeedId = Shader.PropertyToID("_SpinSpeed");
        private static readonly int UpdraftId = Shader.PropertyToID("_Updraft");
        private static readonly int InflowSpeedId = Shader.PropertyToID("_InflowSpeed");
        private static readonly int NeckScaleId = Shader.PropertyToID("_NeckScale");
        private static readonly int TaperId = Shader.PropertyToID("_Taper");
        private static readonly int GroundFlareId = Shader.PropertyToID("_GroundFlare");
        private static readonly int WobbleAmplitudeId = Shader.PropertyToID("_WobbleAmplitude");
        private static readonly int WobbleFrequencyId = Shader.PropertyToID("_WobbleFrequency");

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int TopColorId = Shader.PropertyToID("_TopColor");
        private static readonly int BaseRadiusId = Shader.PropertyToID("_BaseRadius");
        private static readonly int HeightId = Shader.PropertyToID("_Height");
        private static readonly int SeedId = Shader.PropertyToID("_Seed");
        private static readonly int ShellIndexId = Shader.PropertyToID("_ShellIndex");
        private static readonly int ShellCountId = Shader.PropertyToID("_ShellCount");
        private static readonly int ShellSpacingId = Shader.PropertyToID("_ShellSpacing");
        private static readonly int OpacityId = Shader.PropertyToID("_Opacity");
        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int VelocityStretchId = Shader.PropertyToID("_VelocityStretch");
        private static readonly int GroundYId = Shader.PropertyToID("_GroundY");
        private static readonly int TintHeightId = Shader.PropertyToID("_TintHeight");
        private static readonly int FarFadeId = Shader.PropertyToID("_FarFade");

        [SerializeField] private ComputeShader simulationCompute;
        [SerializeField] private Material funnelMaterial;
        [SerializeField] private Material debrisMaterial;
        [SerializeField] private Mesh debrisMesh;
        [SerializeField] private Camera cameraOverride;

        [Header("Counts")]
        [SerializeField] [Range(0, MaxTornadoes)] private int tornadoCount = 1;
        [SerializeField] [Min(1)] private int debrisCount = 40000;

        [Header("Funnel Shape")]
        [SerializeField] [Min(1f)] private float baseRadius = 45f;
        [SerializeField] [Min(1f)] private float height = 900f;
        [SerializeField] [Range(0.02f, 1f)] private float neckScale = 0.22f;
        [SerializeField] [Range(0.1f, 4f)] private float taper = 0.65f;
        [SerializeField] [Range(0f, 3f)] private float groundFlare = 0.55f;
        [SerializeField] [Range(0f, 4f)] private float wobbleAmplitude = 0.55f;
        [SerializeField] [Min(0f)] private float wobbleFrequency = 3.1f;
        [SerializeField] [Range(0f, 0.6f)] private float radiusVariance = 0.3f;

        [Header("Funnel Shells")]
        [SerializeField] [Range(1, MaxShells)] private int shellCount = 5;
        [SerializeField] [Min(0f)] private float shellSpacing = 0.35f;
        [SerializeField] [Range(8, 96)] private int funnelSlices = 40;
        [SerializeField] [Range(4, 96)] private int funnelRings = 32;

        [Header("Forces")]
        [SerializeField] [Min(0f)] private float spinSpeed = 85f;
        [SerializeField] [Min(0f)] private float updraft = 40f;
        [SerializeField] [Min(0f)] private float inflowSpeed = 1.4f;
        [SerializeField] [Min(0f)] private float turbulence = 5f;
        [SerializeField] private Vector3 gravity = new(0f, -9.81f, 0f);
        [SerializeField] private Vector3 wind = new(8f, 0f, 3f);
        [SerializeField] [Range(0f, 1f)] private float driftScale = 0.35f;

        [Header("Placement")]
        [SerializeField] private float groundY;
        [SerializeField] private LayerMask groundMask = ~0;
        [SerializeField] [Min(1f)] private float groundRayHeight = 3000f;
        [SerializeField] [Min(0f)] private float spawnRadiusMin = 600f;
        [SerializeField] [Min(0f)] private float spawnRadiusMax = 2500f;
        [SerializeField] [Min(0f)] private float despawnRadius = 6000f;

        [Header("Look")]
        [SerializeField] private Color funnelColor = new(0.28f, 0.25f, 0.22f, 1f);
        [SerializeField] private Color funnelTopColor = new(0.45f, 0.46f, 0.5f, 1f);
        [SerializeField] private Color debrisColor = new(0.32f, 0.28f, 0.23f, 0.75f);
        [SerializeField] private Color debrisTopColor = new(0.5f, 0.5f, 0.54f, 0.5f);
        [SerializeField] [Min(0.001f)] private float debrisSize = 0.9f;
        [SerializeField] [Min(0f)] private float debrisStretch = 2.5f;
        [SerializeField] [Range(0f, 1f)] private float intensity = 1f;

        private readonly GraphicsBuffer.IndirectDrawIndexedArgs[] _argsData =
            new GraphicsBuffer.IndirectDrawIndexedArgs[1];

        private readonly TornadoData[] _tornadoData = new TornadoData[MaxTornadoes];

        public int TornadoCount => _activeTornadoes;
        public int DebrisCount => _drawDebris;
        public Vector3 Wind => wind;
        public float Radius => baseRadius;
        public float Height => height;
        public float SpinSpeed => spinSpeed;
        public float Updraft => updraft;

        private GraphicsBuffer _args;
        private ComputeBuffer _particles;
        private ComputeBuffer _tornadoes;
        private MaterialPropertyBlock[] _funnelProps;
        private MaterialPropertyBlock _debrisProps;
        private Mesh _funnelMesh;
        private Mesh _runtimeQuad;
        private Material _runtimeFunnelMaterial;
        private Material _runtimeDebrisMaterial;
        private int _activeTornadoes;
        private int _drawDebris;
        private int _gpuDebris;
        private int _kernel;

        private void OnEnable()
        {
            AllocateGpu();
        }

        private void OnDisable()
        {
            ReleaseGpu();
        }

        private void OnDestroy()
        {
            ReleaseGpu();
        }

        private void Update()
        {
            if (!isActiveAndEnabled)
                return;

            if (_particles == null || _args == null)
                AllocateGpu();

            Camera cam = ResolveCamera();
            if (!cam || _activeTornadoes <= 0 || intensity <= 0.001f)
                return;

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            UpdateAnchors(cam, dt);

            SimulateDebris(dt);
            DrawDebris(cam);
            DrawFunnels();
        }

        /// <summary>Walks each funnel downwind and recycles the ones that have left the play area.</summary>
        private void UpdateAnchors(Camera cam, float dt)
        {
            Vector3 camPos = cam.transform.position;
            Vector3 drift = new Vector3(wind.x, 0f, wind.z) * (driftScale * dt);
            float despawnSqr = despawnRadius * despawnRadius;

            for (int i = 0; i < _activeTornadoes; i++)
            {
                TornadoData t = _tornadoData[i];
                t.basePosition += drift;

                Vector2 offset = new(t.basePosition.x - camPos.x, t.basePosition.z - camPos.z);
                if (offset.sqrMagnitude > despawnSqr)
                {
                    t = SpawnTornado(camPos);
                }
                else
                {
                    t.basePosition.y = ResolveGround(t.basePosition);
                    t.height = height;
                    t.radius = baseRadius * Mathf.Lerp(1f - radiusVariance, 1f + radiusVariance,
                        Mathf.Repeat(t.seed * 0.618034f, 1f));
                }

                _tornadoData[i] = t;
            }

            _tornadoes?.SetData(_tornadoData);
        }

        private TornadoData SpawnTornado(Vector3 camPos)
        {
            float angle = Random.value * Mathf.PI * 2f;
            float distance = Random.Range(Mathf.Min(spawnRadiusMin, spawnRadiusMax),
                Mathf.Max(spawnRadiusMin, spawnRadiusMax));
            Vector3 position = camPos + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * distance;
            position.y = ResolveGround(position);

            float seed = Random.value * 999.173f;
            return new TornadoData
            {
                basePosition = position,
                radius = baseRadius * Mathf.Lerp(1f - radiusVariance, 1f + radiusVariance,
                    Mathf.Repeat(seed * 0.618034f, 1f)),
                height = height,
                spin = Random.value < 0.5f ? -1f : 1f,
                seed = seed
            };
        }

        private float ResolveGround(Vector3 position)
        {
            Vector3 origin = new(position.x, groundY + groundRayHeight, position.z);
            return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundRayHeight * 2f, groundMask,
                QueryTriggerInteraction.Ignore)
                ? hit.point.y
                : groundY;
        }

        private void SimulateDebris(float dt)
        {
            if (!simulationCompute || _particles == null || _drawDebris <= 0)
                return;

            simulationCompute.SetFloat(DeltaTimeId, dt);
            simulationCompute.SetFloat(SimTimeId, Time.time);
            simulationCompute.SetInt(ParticleCountId, _drawDebris);
            simulationCompute.SetInt(TornadoCountId, _activeTornadoes);
            simulationCompute.SetVector(GravityId, gravity);
            simulationCompute.SetVector(WindId, wind);
            simulationCompute.SetFloat(TurbulenceId, turbulence);
            simulationCompute.SetFloat(SpinSpeedId, spinSpeed);
            simulationCompute.SetFloat(UpdraftId, updraft);
            simulationCompute.SetFloat(InflowSpeedId, inflowSpeed);
            simulationCompute.SetFloat(NeckScaleId, neckScale);
            simulationCompute.SetFloat(TaperId, taper);
            simulationCompute.SetFloat(GroundFlareId, groundFlare);
            simulationCompute.SetFloat(WobbleAmplitudeId, wobbleAmplitude);
            simulationCompute.SetFloat(WobbleFrequencyId, wobbleFrequency);
            simulationCompute.SetBuffer(_kernel, ParticlesId, _particles);
            simulationCompute.SetBuffer(_kernel, TornadoesId, _tornadoes);

            int groups = (_drawDebris + ThreadGroupSize - 1) / ThreadGroupSize;
            simulationCompute.Dispatch(_kernel, groups, 1, 1);
        }

        private void DrawDebris(Camera cam)
        {
            Material material = ResolveDebrisMaterial();
            if (!material || _particles == null || _drawDebris <= 0)
                return;

            Mesh drawMesh = ResolveDebrisMesh();
            if (!drawMesh)
                return;

            WriteArgs(drawMesh);

            material.enableInstancing = true;
            _debrisProps ??= new MaterialPropertyBlock();
            _debrisProps.Clear();

            Color near = debrisColor;
            Color far = debrisTopColor;
            near.a *= intensity;
            far.a *= intensity;

            _debrisProps.SetBuffer(ParticlesId, _particles);
            _debrisProps.SetColor(BaseColorId, near);
            _debrisProps.SetColor(TopColorId, far);
            _debrisProps.SetFloat(SizeId, debrisSize);
            _debrisProps.SetFloat(VelocityStretchId, debrisStretch);
            _debrisProps.SetFloat(GroundYId, _tornadoData[0].basePosition.y);
            _debrisProps.SetFloat(TintHeightId, Mathf.Max(height * 0.5f, 1f));
            _debrisProps.SetFloat(FarFadeId, Mathf.Max(despawnRadius, 1f));
            material.SetBuffer(ParticlesId, _particles);

            RenderParams rp = new(material)
            {
                worldBounds = DebrisBounds(cam),
                layer = gameObject.layer,
                renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.Camera,
                matProps = _debrisProps
            };

            Graphics.RenderMeshIndirect(rp, drawMesh, _args);
        }

        private Bounds DebrisBounds(Camera cam)
        {
            Bounds bounds = new(_tornadoData[0].basePosition, Vector3.one);
            for (int i = 0; i < _activeTornadoes; i++)
            {
                TornadoData t = _tornadoData[i];
                float spread = t.radius * 8f;
                bounds.Encapsulate(t.basePosition + new Vector3(spread, t.height, spread));
                bounds.Encapsulate(t.basePosition - new Vector3(spread, 0f, spread));
            }

            bounds.Encapsulate(cam.transform.position);
            return bounds;
        }

        private void DrawFunnels()
        {
            Material material = ResolveFunnelMaterial();
            if (!material)
                return;

            Mesh mesh = ResolveFunnelMesh();
            if (!mesh)
                return;

            int shells = Mathf.Clamp(shellCount, 1, MaxShells);
            EnsureFunnelProps(shells);

            Color near = funnelColor;
            near.a *= intensity;

            for (int i = 0; i < _activeTornadoes; i++)
            {
                TornadoData t = _tornadoData[i];
                float spread = t.radius * (1f + shellSpacing) * 4f;
                Bounds bounds = new(
                    t.basePosition + new Vector3(0f, t.height * 0.5f, 0f),
                    new Vector3(spread, t.height * 1.2f, spread));

                for (int s = 0; s < shells; s++)
                {
                    MaterialPropertyBlock props = _funnelProps[i * MaxShells + s];
                    props.Clear();
                    props.SetColor(BaseColorId, near);
                    props.SetColor(TopColorId, funnelTopColor);
                    props.SetFloat(BaseRadiusId, t.radius);
                    props.SetFloat(HeightId, t.height);
                    props.SetFloat(NeckScaleId, neckScale);
                    props.SetFloat(TaperId, taper);
                    props.SetFloat(GroundFlareId, groundFlare);
                    props.SetFloat(WobbleAmplitudeId, wobbleAmplitude);
                    props.SetFloat(WobbleFrequencyId, wobbleFrequency);
                    props.SetFloat(SeedId, t.seed);
                    props.SetFloat(ShellIndexId, s);
                    props.SetFloat(ShellCountId, shells);
                    props.SetFloat(ShellSpacingId, shellSpacing);
                    props.SetFloat(OpacityId, intensity);

                    RenderParams rp = new(material)
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

                    Graphics.RenderMesh(rp, mesh, 0, Matrix4x4.Translate(t.basePosition));
                }
            }
        }

        private void EnsureFunnelProps(int shells)
        {
            _funnelProps ??= new MaterialPropertyBlock[MaxTornadoes * MaxShells];
            for (int i = 0; i < _activeTornadoes; i++)
            for (int s = 0; s < shells; s++)
            {
                int index = i * MaxShells + s;
                _funnelProps[index] ??= new MaterialPropertyBlock();
            }
        }

        // Materials are optional: without one wired up we build it from the shader, the same
        // way AircraftExplosion does, so the component works as soon as it is added.
        private Material ResolveFunnelMaterial()
        {
            if (funnelMaterial)
                return funnelMaterial;

            if (!_runtimeFunnelMaterial)
                _runtimeFunnelMaterial = CreateRuntimeMaterial(
                    "Airplane/Weather/Tornado Funnel URP", "TornadoFunnelRuntime");

            return _runtimeFunnelMaterial;
        }

        private Material ResolveDebrisMaterial()
        {
            if (debrisMaterial)
                return debrisMaterial;

            if (!_runtimeDebrisMaterial)
                _runtimeDebrisMaterial = CreateRuntimeMaterial(
                    "Airplane/Weather/Tornado Debris URP", "TornadoDebrisRuntime");

            return _runtimeDebrisMaterial;
        }

        private static Material CreateRuntimeMaterial(string shaderName, string materialName)
        {
            Shader shader = Shader.Find(shaderName);
            if (!shader)
                return null;

            return new Material(shader)
            {
                name = materialName,
                hideFlags = HideFlags.HideAndDontSave,
                enableInstancing = true
            };
        }

        private Camera ResolveCamera()
        {
            if (cameraOverride)
                return cameraOverride;
            if (Camera.main)
                return Camera.main;
            return Camera.current;
        }

        /// <summary>Unit tube: xz on the unit circle, y in [0,1]. The shader gives it its shape.</summary>
        private Mesh ResolveFunnelMesh()
        {
            if (_funnelMesh)
                return _funnelMesh;

            int slices = Mathf.Clamp(funnelSlices, 8, 96);
            int rings = Mathf.Clamp(funnelRings, 4, 96);
            int columns = slices + 1;

            var vertices = new Vector3[columns * (rings + 1)];
            var normals = new Vector3[vertices.Length];
            var uvs = new Vector2[vertices.Length];
            var triangles = new int[slices * rings * 6];

            for (int r = 0; r <= rings; r++)
            {
                float v = r / (float)rings;
                for (int s = 0; s <= slices; s++)
                {
                    float u = s / (float)slices;
                    float angle = u * Mathf.PI * 2f;
                    float cos = Mathf.Cos(angle);
                    float sin = Mathf.Sin(angle);

                    int index = r * columns + s;
                    vertices[index] = new Vector3(cos, v, sin);
                    normals[index] = new Vector3(cos, 0f, sin);
                    uvs[index] = new Vector2(u, v);
                }
            }

            int tri = 0;
            for (int r = 0; r < rings; r++)
            for (int s = 0; s < slices; s++)
            {
                int i0 = r * columns + s;
                int i1 = i0 + 1;
                int i2 = i0 + columns;
                int i3 = i2 + 1;

                triangles[tri++] = i0;
                triangles[tri++] = i2;
                triangles[tri++] = i1;
                triangles[tri++] = i1;
                triangles[tri++] = i2;
                triangles[tri++] = i3;
            }

            _funnelMesh = new Mesh { name = "TornadoFunnel" };
            if (vertices.Length > 65535)
                _funnelMesh.indexFormat = IndexFormat.UInt32;
            _funnelMesh.vertices = vertices;
            _funnelMesh.normals = normals;
            _funnelMesh.uv = uvs;
            _funnelMesh.triangles = triangles;
            // the vertex shader expands this far past the unit tube
            _funnelMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 10f);
            return _funnelMesh;
        }

        private Mesh ResolveDebrisMesh()
        {
            if (debrisMesh)
                return debrisMesh;

            if (!_runtimeQuad)
            {
                _runtimeQuad = new Mesh { name = "TornadoDebrisQuad" };
                _runtimeQuad.vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f),
                    new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f),
                    new Vector3(0.5f, 0.5f, 0f)
                };
                _runtimeQuad.uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(1f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f)
                };
                _runtimeQuad.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                _runtimeQuad.RecalculateBounds();
            }

            return _runtimeQuad;
        }

        private void WriteArgs(Mesh drawMesh)
        {
            _argsData[0].indexCountPerInstance = drawMesh.GetIndexCount(0);
            _argsData[0].instanceCount = (uint)Mathf.Max(_drawDebris, 0);
            _argsData[0].startIndex = drawMesh.GetIndexStart(0);
            _argsData[0].baseVertexIndex = drawMesh.GetBaseVertex(0);
            _argsData[0].startInstance = 0;
            _args.SetData(_argsData);
        }

        private void AllocateGpu()
        {
            int previousDraw = _drawDebris;
            bool hadBuffer = _particles != null;
            ReleaseBuffers();

            if (!simulationCompute)
                return;

            _kernel = simulationCompute.FindKernel("Simulate");
            _gpuDebris = Mathf.Max(1, debrisCount);
            _drawDebris = hadBuffer ? Mathf.Clamp(previousDraw, 0, _gpuDebris) : _gpuDebris;

            Camera cam = ResolveCamera();
            Vector3 camPos = cam != null ? cam.transform.position : transform.position;

            // Reallocating for capacity must not teleport funnels that are already running.
            _activeTornadoes = Mathf.Clamp(hadBuffer ? _activeTornadoes : tornadoCount, 0, MaxTornadoes);
            for (int i = 0; i < MaxTornadoes; i++)
            {
                if (i >= _activeTornadoes)
                    _tornadoData[i] = default;
                else if (_tornadoData[i].height <= 0f)
                    _tornadoData[i] = SpawnTornado(camPos);
            }

            _tornadoes = new ComputeBuffer(MaxTornadoes, TornadoData.Stride, ComputeBufferType.Structured);
            _tornadoes.SetData(_tornadoData);

            // The sim recycles anything that is out of range, so a rough seed is enough.
            var data = new DebrisParticle[_gpuDebris];
            Vector3 anchor = _activeTornadoes > 0 ? _tornadoData[0].basePosition : camPos;
            for (int i = 0; i < _gpuDebris; i++)
            {
                float angle = Random.value * Mathf.PI * 2f;
                float radius = baseRadius * Random.Range(0.2f, 1.6f);
                data[i].position = anchor + new Vector3(
                    Mathf.Cos(angle) * radius,
                    Random.value * height,
                    Mathf.Sin(angle) * radius);
                data[i].velocity = Vector3.up * updraft * Random.Range(0.2f, 0.8f);
                data[i].scale = Random.Range(0.35f, 1.85f);
                data[i].seed = Random.value * 999.173f;
            }

            _particles = new ComputeBuffer(_gpuDebris, DebrisParticle.Stride, ComputeBufferType.Structured);
            _particles.SetData(data);

            _args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            Mesh drawMesh = ResolveDebrisMesh();
            if (drawMesh)
                WriteArgs(drawMesh);
        }

        private void ReleaseBuffers()
        {
            _particles?.Release();
            _particles = null;
            _tornadoes?.Release();
            _tornadoes = null;
            _args?.Release();
            _args = null;
            _gpuDebris = 0;
        }

        private void ReleaseGpu()
        {
            ReleaseBuffers();
            DestroyRuntimeObject(ref _runtimeQuad);
            DestroyRuntimeObject(ref _funnelMesh);
            DestroyRuntimeObject(ref _runtimeFunnelMaterial);
            DestroyRuntimeObject(ref _runtimeDebrisMaterial);
        }

        private static void DestroyRuntimeObject<T>(ref T target) where T : Object
        {
            if (target == null)
                return;

            if (Application.isPlaying)
                Destroy(target);
            else
                DestroyImmediate(target);
            target = null;
        }

        public void SetWind(Vector3 value)
        {
            wind = value;
        }

        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp01(value);
        }

        public void SetShape(float radius, float columnHeight)
        {
            baseRadius = Mathf.Max(radius, 1f);
            height = Mathf.Max(columnHeight, 1f);
        }

        public void SetForces(float spin, float lift)
        {
            spinSpeed = Mathf.Max(spin, 0f);
            updraft = Mathf.Max(lift, 0f);
        }

        public void SetTornadoCount(int count)
        {
            count = Mathf.Clamp(count, 0, MaxTornadoes);
            if (count == _activeTornadoes)
                return;

            Camera cam = ResolveCamera();
            Vector3 camPos = cam != null ? cam.transform.position : transform.position;
            for (int i = _activeTornadoes; i < count; i++)
                _tornadoData[i] = SpawnTornado(camPos);

            _activeTornadoes = count;
            _tornadoes?.SetData(_tornadoData);
        }

        public void EnsureDebrisCapacity(int count)
        {
            int needed = Mathf.Max(count, 1);
            if (_particles != null && _gpuDebris >= needed)
                return;

            debrisCount = needed;
            AllocateGpu();
        }

        public void SetDebrisCount(int count)
        {
            count = Mathf.Max(0, count);
            if (count > 0)
                EnsureDebrisCapacity(count);

            _drawDebris = _particles != null ? Mathf.Min(count, _gpuDebris) : count;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DebrisParticle
        {
            public Vector3 position;
            public Vector3 velocity;
            public float scale;
            public float seed;
            public const int Stride = 32;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct TornadoData
        {
            public Vector3 basePosition;
            public float radius;
            public float height;
            public float spin;
            public float seed;
            public float pad;
            public const int Stride = 32;
        }
    }
}
