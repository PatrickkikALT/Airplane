using System.Runtime.InteropServices;
using Airplane.FlightSimulation;
using UnityEngine;
using UnityEngine.Rendering;

namespace Airplane.Weather
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Weather/Weather System")]
    public sealed class WeatherSystem : MonoBehaviour
    {
        public const int ThreadGroupSize = 256;

        private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
        private static readonly int DeltaTimeId = Shader.PropertyToID("_DeltaTime");
        private static readonly int SimTimeId = Shader.PropertyToID("_SimTime");
        private static readonly int TurbulenceId = Shader.PropertyToID("_Turbulence");
        private static readonly int ParticleCountId = Shader.PropertyToID("_ParticleCount");
        private static readonly int GravityId = Shader.PropertyToID("_Gravity");
        private static readonly int WindId = Shader.PropertyToID("_Wind");
        private static readonly int CameraPositionId = Shader.PropertyToID("_CameraPosition");
        private static readonly int BoundsSizeId = Shader.PropertyToID("_BoundsSize");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SizeId = Shader.PropertyToID("_Size");
        private static readonly int VelocityStretchId = Shader.PropertyToID("_VelocityStretch");
        private static readonly int FarFadeId = Shader.PropertyToID("_FarFade");
        private static readonly int CloudCeilingId = Shader.PropertyToID("_CloudCeiling");
        private static readonly int CloudFadeId = Shader.PropertyToID("_CloudFade");

        private const float NoCloudCeiling = 1e7f;

        [SerializeField] private ComputeShader simulationCompute;

        [SerializeField] [Min(1)] private int particleCount = 100000;
        [SerializeField] private Mesh mesh;
        [SerializeField] private Material material;

        [Header("Forces")] 
        [SerializeField] private Vector3 wind = new(4f, 0f, 1f);

        [SerializeField] private Vector3 gravity = new(0f, -18f, 0f);
        [SerializeField] [Min(0f)] private float turbulence = 6f;

        [Header("Volume")]
        [SerializeField] private Vector3 volumeBounds = new(50f, 40f, 50f);
        [SerializeField] [Min(1f)] private float cloudFade = 120f;

        [SerializeField] private Camera cameraOverride;

        [Header("Look")] 
        [SerializeField] private Color color = new(0.75f, 0.82f, 0.9f, 0.45f);

        [SerializeField] [Min(0.001f)] private float particleSize = 0.08f;
        [SerializeField] [Min(0f)] private float velocityStretch = 8f;

        private readonly GraphicsBuffer.IndirectDrawIndexedArgs[] _argsData =
            new GraphicsBuffer.IndirectDrawIndexedArgs[1];

        public int ParticleCount => _drawCount;
        public Vector3 Wind => wind;

        private GraphicsBuffer _args;
        private int _gpuCount;
        private int _drawCount;
        private int _kernel;
        private bool _loggedDepthWarning;

        private ComputeBuffer _particles;
        private MaterialPropertyBlock _props;
        private Mesh _runtimeQuad;

        private void Update()
        {
            if (!isActiveAndEnabled)
                return;

            if (_particles == null || _args == null)
                AllocateGpu();

            if (_particles == null || !simulationCompute || !material || _drawCount <= 0)
                return;

            Camera cam = ResolveCamera();
            if (!cam)
                return;

            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            Vector3 camPos = cam.transform.position;
            float ceiling = ResolveCloudCeiling();
            float fadeMeters = Mathf.Max(cloudFade, 1f);
            float camFade = Mathf.Clamp01((ceiling - camPos.y) / fadeMeters);

            simulationCompute.SetFloat(DeltaTimeId, dt);
            simulationCompute.SetFloat(SimTimeId, Time.time);
            simulationCompute.SetFloat(TurbulenceId, turbulence);
            simulationCompute.SetFloat(CloudCeilingId, ceiling);
            simulationCompute.SetInt(ParticleCountId, _drawCount);
            simulationCompute.SetVector(GravityId, gravity);
            simulationCompute.SetVector(WindId, wind);
            simulationCompute.SetVector(CameraPositionId, camPos);
            simulationCompute.SetVector(BoundsSizeId, volumeBounds);
            simulationCompute.SetBuffer(_kernel, ParticlesId, _particles);

            int groups = (_drawCount + ThreadGroupSize - 1) / ThreadGroupSize;
            simulationCompute.Dispatch(_kernel, groups, 1, 1);

            // dry above the cloud deck
            if (camFade <= 0.001f)
                return;

            Mesh drawMesh = ResolveMesh();
            if (!drawMesh)
                return;

            WriteArgs(drawMesh);

            material.enableInstancing = true;
            _props ??= new MaterialPropertyBlock();
            _props.Clear();
            Color drawColor = color;
            drawColor.a *= camFade;

            _props.SetBuffer(ParticlesId, _particles);
            _props.SetColor(BaseColorId, drawColor);
            _props.SetFloat(SizeId, particleSize);
            _props.SetFloat(VelocityStretchId, velocityStretch);
            _props.SetFloat(FarFadeId, Mathf.Max(Mathf.Min(volumeBounds.x, volumeBounds.z) * 0.45f, 1f));
            _props.SetFloat(CloudCeilingId, ceiling);
            _props.SetFloat(CloudFadeId, fadeMeters);
            material.SetBuffer(ParticlesId, _particles);

            RenderParams rp = new(material)
            {
                worldBounds = new Bounds(camPos, volumeBounds),
                layer = gameObject.layer,
                renderingLayerMask = RenderingLayerMask.defaultRenderingLayerMask,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false,
                lightProbeUsage = LightProbeUsage.Off,
                reflectionProbeUsage = ReflectionProbeUsage.Off,
                motionVectorMode = MotionVectorGenerationMode.Camera,
                matProps = _props
            };

            Graphics.RenderMeshIndirect(rp, drawMesh, _args);
        }

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

        private static float ResolveCloudCeiling()
        {
            VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
            if (stack == null)
                return NoCloudCeiling;

            VolumetricClouds clouds = stack.GetComponent<VolumetricClouds>();
            if (clouds == null || !clouds.state.value)
                return NoCloudCeiling;

            float sea = AtmosphericModel.Instance ? AtmosphericModel.Instance.SeaLevelY : 0f;
            return sea + clouds.bottomAltitude.value;
        }

        private Camera ResolveCamera()
        {
            if (cameraOverride)
                return cameraOverride;
            if (Camera.main)
                return Camera.main;
            return Camera.current;
        }

        private Mesh ResolveMesh()
        {
            if (mesh)
                return mesh;

            if (!_runtimeQuad)
            {
                _runtimeQuad = new Mesh { name = "WeatherQuad" };
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
            _argsData[0].instanceCount = (uint)Mathf.Max(_drawCount, 0);
            _argsData[0].startIndex = drawMesh.GetIndexStart(0);
            _argsData[0].baseVertexIndex = drawMesh.GetBaseVertex(0);
            _argsData[0].startInstance = 0;
            _args.SetData(_argsData);
        }

        private void AllocateGpu()
        {
            int previousDraw = _drawCount;
            bool hadBuffer = _particles != null;
            ReleaseBuffers();

            if (!simulationCompute)
                return;

            _kernel = simulationCompute.FindKernel("Simulate");
            _gpuCount = Mathf.Max(1, particleCount);
            _drawCount = hadBuffer ? Mathf.Clamp(previousDraw, 0, _gpuCount) : _gpuCount;

            Camera cam = ResolveCamera();
            Vector3 camPos = cam != null ? cam.transform.position : transform.position;
            Vector3 half = volumeBounds * 0.5f;
            half = new Vector3(Mathf.Max(half.x, 0.01f), Mathf.Max(half.y, 0.01f), Mathf.Max(half.z, 0.01f));

            var data = new WeatherParticle[_gpuCount];
            for (int i = 0; i < _gpuCount; i++)
            {
                data[i].position = camPos + new Vector3(
                    Random.Range(-half.x, half.x),
                    Random.Range(-half.y, half.y),
                    Random.Range(-half.z, half.z));
                data[i].velocity = wind + gravity * Random.Range(0.55f, 1.15f);
                data[i].scale = Random.Range(0.45f, 1.35f);
                data[i].seed = Random.value * 999.173f;
            }

            _particles = new ComputeBuffer(_gpuCount, WeatherParticle.Stride, ComputeBufferType.Structured);
            _particles.SetData(data);

            _args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1,
                GraphicsBuffer.IndirectDrawIndexedArgs.size);
            Mesh drawMesh = ResolveMesh();
            if (drawMesh)
                WriteArgs(drawMesh);
        }

        private void ReleaseBuffers()
        {
            _particles?.Release();
            _particles = null;
            _args?.Release();
            _args = null;
            _gpuCount = 0;
        }

        private void ReleaseGpu()
        {
            ReleaseBuffers();
            if (_runtimeQuad == null)
                return;

            if (Application.isPlaying)
                Destroy(_runtimeQuad);
            else
                DestroyImmediate(_runtimeQuad);
            _runtimeQuad = null;
        }

        public void SetWind(Vector3 value)
        {
            wind = value;
        }

        public void EnsureCapacity(int count)
        {
            int needed = Mathf.Max(count, 1);
            if (_particles != null && _gpuCount >= needed)
                return;

            particleCount = needed;
            AllocateGpu();
        }

        public void SetParticleCount(int count)
        {
            count = Mathf.Max(0, count);
            if (count > 0)
                EnsureCapacity(count);

            _drawCount = _particles != null ? Mathf.Min(count, _gpuCount) : count;
        }

 

        [StructLayout(LayoutKind.Sequential)]
        private struct WeatherParticle
        {
            public Vector3 position;
            public Vector3 velocity;
            public float scale;
            public float seed;
            public const int Stride = 32;
        }
    }
}