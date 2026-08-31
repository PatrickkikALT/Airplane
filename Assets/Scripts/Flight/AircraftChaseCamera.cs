using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.Serialization;

namespace Airplane.FlightSimulation
{
    /// <summary>
    /// Chase / orbit camera. The aircraft stays centered and the rig does not roll with the
    /// airframe. Heading eases behind the nose; Look orbits, Zoom dollies, ResetOrbit recenters.
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
        [Tooltip("If true, pointer Look only orbits while OrbitHold is pressed. Analog sticks always orbit.")]
        [FormerlySerializedAs("requireRightMouse")]
        [SerializeField] private bool requireOrbitHold = true;

        [Tooltip("Degrees per mouse-delta unit (typically pixels).")]
        [SerializeField] private float yawSensitivity = 0.16f;
        [SerializeField] private float pitchSensitivity = 0.14f;

        [Tooltip("Degrees per second at full stick deflection.")]
        [SerializeField] private float stickYawSpeed = 90f;
        [SerializeField] private float stickPitchSpeed = 70f;

        [SerializeField] private bool invertPitch;

        [SerializeField] private float minPitch = -10f;
        [SerializeField] private float maxPitch = 80f;

        [Tooltip("Mouse-wheel zoom: distance *= 1 − scroll × this.")]
        [SerializeField] private float zoomSensitivity = 0.012f;

        [Tooltip("Held analog zoom: distance *= 1 − axis × this × dt.")]
        [SerializeField] private float zoomStickSensitivity = 1.4f;

        [SerializeField] private float minDistance = 6f;
        [SerializeField] private float maxDistance = 48f;

        private Vector3 _followFwd;
        private bool _hasHeading;
        private float _orbitYaw;
        private float _orbitPitch;
        private float _distance;
        private float _defaultPitch;
        private float _defaultDistance;

        private Vector2 _pointerLook;
        private Vector2 _stickLook;
        private float _pointerZoom;
        private float _stickZoom;
        private bool _orbitHeld;

        /// <summary>Scene chase camera, if one is enabled. Used so PlayerInput on the aircraft can forward Look.</summary>
        public static AircraftChaseCamera Active { get; private set; }

        public void SetTarget(Transform t)
        {
            target = t;
            _hasHeading = false;
            ResetOrbit();
        }

        public void OnLook(InputAction.CallbackContext context)
        {
            if (IsAnalog(context))
            {
                _stickLook = context.canceled ? Vector2.zero : context.ReadValue<Vector2>();
                return;
            }

            if (context.canceled)
                return;
            _pointerLook += context.ReadValue<Vector2>();
        }

        public void OnOrbitHold(InputAction.CallbackContext context)
        {
            _orbitHeld = !context.canceled && context.ReadValueAsButton();
        }

        public void OnZoom(InputAction.CallbackContext context)
        {
            if (IsAnalog(context))
            {
                _stickZoom = context.canceled ? 0f : context.ReadValue<float>();
                return;
            }

            if (context.canceled)
                return;
            _pointerZoom += context.ReadValue<float>();
        }

        public void OnResetOrbit(InputAction.CallbackContext context)
        {
            if (context.performed)
                ResetOrbit();
        }

        private void OnEnable()
        {
            Active = this;
        }

        private void OnDisable()
        {
            if (Active == this)
                Active = null;
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

            ApplyOrbitInput();
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

        private void ApplyOrbitInput()
        {
            bool analogOrbit = _stickLook.sqrMagnitude > 1e-8f;
            bool orbit = !requireOrbitHold || analogOrbit || _orbitHeld;
            if (orbit)
            {
                if (analogOrbit)
                {
                    float dt = Time.deltaTime;
                    ApplyLook(_stickLook.x * stickYawSpeed * dt, _stickLook.y * stickPitchSpeed * dt);
                }

                if (_pointerLook.sqrMagnitude > 1e-8f)
                    ApplyLook(_pointerLook.x * yawSensitivity, _pointerLook.y * pitchSensitivity);
            }

            _pointerLook = Vector2.zero;

            if (_stickZoom * _stickZoom > 1e-8f)
                _distance *= 1f - _stickZoom * zoomStickSensitivity * Time.deltaTime;
            if (_pointerZoom * _pointerZoom > 1e-8f)
                _distance *= 1f - _pointerZoom * zoomSensitivity;
            _pointerZoom = 0f;

            _distance = Mathf.Clamp(_distance, minDistance, maxDistance);
        }

        private void ApplyLook(float yawDelta, float pitchDelta)
        {
            _orbitYaw += yawDelta;
            _orbitPitch += invertPitch ? pitchDelta : -pitchDelta;
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

        private static bool IsAnalog(InputAction.CallbackContext context)
        {
            InputControl control = context.control;
            if (control == null)
                return false;
            if (control is DeltaControl || control.parent is DeltaControl)
                return false;
            return control.device is Gamepad || control.device is Joystick;
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
