using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Airplane.Environment
{
    public class TreeBillboardSwitch : MonoBehaviour
    {
        static readonly List<TreeBillboardSwitch> All = new List<TreeBillboardSwitch>(2048);
        static bool hooked;

        [SerializeField] Renderer billboard;
        [SerializeField] float meshDistance = 220f;
        [SerializeField] float billboardDistance = 7000f;

        MeshRenderer mesh;
        Vector3 localCenter;
        bool meshOn;
        bool billboardOn = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook()
        {
            if (hooked)
                return;
            hooked = true;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
        }

        void OnEnable()
        {
            Hook();
            mesh = GetComponent<MeshRenderer>();
            if (mesh != null)
            {
                localCenter = transform.InverseTransformPoint(mesh.bounds.center);
                mesh.forceMeshLod = -1;
                mesh.enabled = false;
            }
            meshOn = false;
            if (billboard != null)
                billboard.enabled = true;
            billboardOn = true;
            if (!All.Contains(this))
                All.Add(this);
        }

        void OnDisable()
        {
            All.Remove(this);
        }

        static void OnBeginCamera(ScriptableRenderContext context, Camera cam)
        {
            if (cam == null || cam.cameraType != CameraType.Game)
                return;

            Vector3 pos = cam.transform.position;
            float meshSq = 220f * 220f;
            float billSq = 7000f * 7000f;
            if (All.Count > 0)
            {
                TreeBillboardSwitch first = All[0];
                meshSq = first.meshDistance * first.meshDistance;
                billSq = first.billboardDistance * first.billboardDistance;
            }

            for (int i = All.Count - 1; i >= 0; i--)
            {
                TreeBillboardSwitch tree = All[i];
                if (tree == null)
                {
                    All.RemoveAt(i);
                    continue;
                }
                tree.Apply(pos, meshSq, billSq);
            }
        }

        void Apply(Vector3 cameraPos, float meshDistanceSq, float billboardDistanceSq)
        {
            if (mesh == null)
                mesh = GetComponent<MeshRenderer>();
            if (mesh == null || billboard == null)
                return;

            float distSq = (transform.TransformPoint(localCenter) - cameraPos).sqrMagnitude;
            bool showMesh = distSq <= meshDistanceSq;
            bool showBillboard = !showMesh && distSq <= billboardDistanceSq;
            if (showMesh != meshOn)
            {
                if (showMesh)
                    mesh.forceMeshLod = 0;
                mesh.enabled = showMesh;
                meshOn = showMesh;
            }
            if (showBillboard != billboardOn)
            {
                billboard.enabled = showBillboard;
                billboardOn = showBillboard;
            }
        }
    }
}
