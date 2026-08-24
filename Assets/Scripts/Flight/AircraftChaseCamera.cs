using UnityEngine;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Chase camera. Follows the aircraft's transform so it does not
    /// add a second smoothing layer on top of FixedUpdate pose snaps.
    /// </summary>
    [AddComponentMenu("Airplane/Aircraft Chase Camera")]
    public sealed class AircraftChaseCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 localOffset = new Vector3(-14f, 3.6f, 0f);
        [SerializeField] private float lookAhead = 18f;

        public void SetTarget(Transform t)
        {
            target = t;
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            Vector3 desired = target.TransformPoint(localOffset);
            Vector3 lookAt = target.position + target.right * lookAhead + Vector3.up * 0.4f;
            transform.SetPositionAndRotation(desired, Quaternion.LookRotation(lookAt - desired, Vector3.up));
        }
    }
}
