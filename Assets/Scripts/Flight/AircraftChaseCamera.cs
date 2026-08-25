using UnityEngine;
using UnityEngine.InputSystem;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Chase / orbit camera. The aircraft stays centered and the rig does not roll with the
    /// airframe. Heading eases behind the nose; right-drag orbits, scroll zooms, middle-click resets.
    /// Body axes are +X forward, so the nose is <see cref="Transform.right"/>.
    /// </summary>
    [AddComponentMenu("Airplane/Aircraft Chase Camera")]
    public sealed class AircraftChaseCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;

        [Tooltip("x = distance behind (negative), y = height above. Used as the default chase pose.")]
        [SerializeField] private Vector3 localOffset = new Vector3(-14f, 3.6f, 0f);

        [Tooltip("Seconds for the camera heading to catch the aircraft yaw. Larger = less rotation.")]
        [SerializeField] private float headingFollowTime = 1.1f;

        [Tooltip("Hard cap on auto heading rate, degrees/s. 0 = unlimited.")]
        [SerializeField] private float maxHeadingRateDeg = 35f;

        [Tooltip("Look this far above the aircraft origin so the model sits in the middle of the frame.")]
        [SerializeField] private float lookAtHeight = 0.45f;

        [Header("Orbit")]
        [Tooltip("If true, only orbit while the right mouse button is held. If false, the mouse always orbits.")]
        [SerializeField] private bool requireRightMouse = true;

        [SerializeField] private float yawSensitivity = 0.16f;
        [SerializeField] private float pitchSensitivity = 0.14f;
        [SerializeField] private bool invertPitch;

        [SerializeField] private float minPitch = -10f;
        [SerializeField] private float maxPitch = 80f;

        [SerializeField] private float zoomSensitivity = 0.012f;
        [SerializeField] private float minDistance = 6f;
        [SerializeField] private float maxDistance = 48f;

        private Vector3 _followFwd;
        private bool _hasHeading;
        private float _orbitYaw;
        private float _orbitPitch;
        private float _distance;
        private float _defaultPitch;
        private float _defaultDistance;

        public void SetTarget(Transform t)
        {
            target = t;
            _hasHeading = false;
            ResetOrbit();
        }

        private void Awake()
        {
            CacheDefaultPose();
            ResetOrbit();
        }

        private void OnValidate()
        {
            CacheDefaultPose();
        }

        private void LateUpdate()
        {
            if (target == null)
                return;

            ApplyMouseOrbit();
            UpdateFollowHeading();

            if (_followFwd.sqrMagnitude < 1e-6f)
                _followFwd = Vector3.forward;

            Vector3 focus = target.position + Vector3.up * lookAtHeight;
            float headingYaw = Mathf.Atan2(_followFwd.x, _followFwd.z) * Mathf.Rad2Deg;
            Quaternion rig = Quaternion.Euler(_orbitPitch, headingYaw + _orbitYaw, 0f);
            Vector3 desired = focus + rig * (Vector3.back * _distance);
            Vector3 toFocus = focus - desired;
            if (toFocus.sqrMagnitude < 1e-8f)
                return;

            transform.SetPositionAndRotation(desired, Quaternion.LookRotation(toFocus, Vector3.up));
        }

        private void ApplyMouseOrbit()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            if (mouse.middleButton.wasPressedThisFrame)
            {
                ResetOrbit();
                return;
            }

            bool orbit = !requireRightMouse || mouse.rightButton.isPressed;
            if (orbit)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _orbitYaw += delta.x * yawSensitivity;
                float pitchDelta = delta.y * pitchSensitivity;
                _orbitPitch += invertPitch ? pitchDelta : -pitchDelta;
                _orbitPitch = Mathf.Clamp(_orbitPitch, minPitch, maxPitch);
            }

            float scroll = mouse.scroll.ReadValue().y;
            if (scroll * scroll > 0.01f)
            {
                _distance *= 1f - scroll * zoomSensitivity;
                _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
            }
        }

        private void UpdateFollowHeading()
        {
            Vector3 nose = HorizontalForward(target);
            if (!_hasHeading)
            {
                _followFwd = nose;
                _hasHeading = true;
                return;
            }

            float dt = Time.deltaTime;
            float t = headingFollowTime < 0.02f ? 1f : 1f - Mathf.Exp(-dt / headingFollowTime);
            Vector3 desiredFwd = Vector3.Slerp(_followFwd, nose, t).normalized;
            if (maxHeadingRateDeg > 0.1f)
            {
                float maxRad = maxHeadingRateDeg * Mathf.Deg2Rad * dt;
                _followFwd = Vector3.RotateTowards(_followFwd, desiredFwd, maxRad, 0f);
            }
            else
                _followFwd = desiredFwd;
        }

        private void CacheDefaultPose()
        {
            _defaultDistance = Mathf.Abs(localOffset.x);
            if (_defaultDistance < 0.5f)
                _defaultDistance = 14f;
            _defaultPitch = Mathf.Atan2(localOffset.y, _defaultDistance) * Mathf.Rad2Deg;
        }

        private void ResetOrbit()
        {
            CacheDefaultPose();
            _orbitYaw = 0f;
            _orbitPitch = _defaultPitch;
            _distance = _defaultDistance;
        }

        private static Vector3 HorizontalForward(Transform t)
        {
            Vector3 nose = Vector3.ProjectOnPlane(t.right, Vector3.up);
            if (nose.sqrMagnitude < 0.04f)
            {
                Vector3 fallback = Vector3.ProjectOnPlane(t.forward, Vector3.up);
                if (fallback.sqrMagnitude < 0.04f)
                    return Vector3.forward;
                return fallback.normalized;
            }

            return nose.normalized;
        }
    }
}
