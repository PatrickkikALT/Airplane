using UnityEngine;

public sealed class TreeBillboard : MonoBehaviour
{
    [SerializeField] private float startSizeMultiple = 6f;
    [SerializeField] private float fullSizeMultiple = 14f;

    private Quaternion _authoredLocal;

    private void Awake()
    {
        _authoredLocal = transform.localRotation;
    }

    private void LateUpdate()
    {
        Camera cam = Camera.main;
        if (!cam)
            return;

        Vector3 toCamera = cam.transform.position - transform.position;
        toCamera.y = 0f;
        float distance = toCamera.magnitude;
        if (distance < 0.001f)
            return;

        float height = Mathf.Max(1f, transform.lossyScale.y);
        float weight = Mathf.InverseLerp(startSizeMultiple * height, fullSizeMultiple * height, distance);
        Quaternion authored = transform.parent ? transform.parent.rotation * _authoredLocal : _authoredLocal;
        Quaternion facing = Quaternion.LookRotation(toCamera / distance, Vector3.up);
        transform.rotation = Quaternion.Slerp(authored, facing, weight);
    }
}
