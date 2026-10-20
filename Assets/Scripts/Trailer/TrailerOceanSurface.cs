using UnityEngine;
using UnityEngine.Rendering;

namespace Airplane.Trailer
{
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter))]
    public sealed class TrailerOceanSurface : MonoBehaviour
    {
        [SerializeField] private int resolution = 128;

        private static readonly int WaterLevelYId = Shader.PropertyToID("_WaterLevelY");

        private Mesh _mesh;
        private MeshRenderer _renderer;
        private MaterialPropertyBlock _propertyBlock;
        private int _builtResolution = -1;
        private bool _hasInitialWaterLevel;
        private float _waterLevelY;

        private void OnEnable()
        {
            RecordWaterLevel();
            BuildMesh();
            KeepVisible();
        }

        private void Awake()
        {
            RecordWaterLevel();
            BuildMesh();
        }

        private void LateUpdate()
        {
            if (!Application.isPlaying)
                _waterLevelY = transform.position.y;

            BuildMesh();
            KeepVisible();
        }

        private void KeepVisible()
        {
            ApplyWaterLevelProperty();
            if (!_renderer)
                _renderer = GetComponent<MeshRenderer>();
            if (_renderer)
            {
                _renderer.allowOcclusionWhenDynamic = false;
            }

            if (!_mesh)
                return;

            Vector3 center = Vector3.zero;
            Camera cam = Camera.main;
            if (cam)
                center = transform.InverseTransformPoint(cam.transform.position);
            _mesh.bounds = new Bounds(center, new Vector3(400000f, 80000f, 400000f));
        }

        private void RecordWaterLevel()
        {
            if (!_hasInitialWaterLevel || !Application.isPlaying)
            {
                _waterLevelY = transform.position.y;
                _hasInitialWaterLevel = true;
            }
        }

        private void ApplyWaterLevelProperty()
        {
            if (!_renderer)
                _renderer = GetComponent<MeshRenderer>();
            if (!_renderer)
                return;

            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            _renderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetFloat(WaterLevelYId, _waterLevelY);
            _renderer.SetPropertyBlock(_propertyBlock);
        }

        private void BuildMesh()
        {
            MeshFilter filter = GetComponent<MeshFilter>();
            if (!filter)
                return;

            int n = Mathf.Clamp(Mathf.Max(resolution, 128), 64, 256);
            if (_mesh && _builtResolution == n && filter.sharedMesh == _mesh)
                return;

            _builtResolution = n;
            int side = n + 1;
            var vertices = new Vector3[side * side];
            var uv = new Vector2[side * side];
            var triangles = new int[n * n * 6];
            int v = 0;
            for (int z = 0; z < side; z++)
            {
                float vz = z / (float)n;
                for (int x = 0; x < side; x++)
                {
                    float ux = x / (float)n;
                    vertices[v] = new Vector3((ux - 0.5f) * 10f, 0f, (vz - 0.5f) * 10f);
                    uv[v] = new Vector2(ux, vz);
                    v++;
                }
            }

            int t = 0;
            for (int z = 0; z < n; z++)
            {
                for (int x = 0; x < n; x++)
                {
                    int i = z * side + x;
                    triangles[t++] = i;
                    triangles[t++] = i + side;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + 1;
                    triangles[t++] = i + side;
                    triangles[t++] = i + side + 1;
                }
            }

            if (!_mesh)
            {
                _mesh = new Mesh();
                _mesh.name = "TrailerOcean";
                _mesh.hideFlags = HideFlags.DontSave;
            }
            else
            {
                _mesh.Clear();
            }

            _mesh.indexFormat = vertices.Length > 65000
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;
            _mesh.vertices = vertices;
            _mesh.uv = uv;
            _mesh.triangles = triangles;
            _mesh.RecalculateNormals();
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(200000f, 50000f, 200000f));
            filter.sharedMesh = _mesh;
            ApplyWaterLevelProperty();
        }
    }
}
