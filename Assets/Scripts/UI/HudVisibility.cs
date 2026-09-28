using UnityEngine;
using UnityEngine.InputSystem;

namespace Airplane.UI
{
    public static class HudVisibility
    {
        public static bool Visible { get; set; } = true;

        public static void Place(RectTransform overlay, RectTransform target, Vector2 screen, Canvas canvas)
        {
            if (!overlay || !target)
                return;

            Camera cam = canvas && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(overlay, screen, cam, out Vector3 world))
                target.position = world;
        }
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    [AddComponentMenu("Airplane/UI/HUD Toggle")]
    public sealed class HudToggle : MonoBehaviour
    {
        private static HudToggle _instance;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        private void Update()
        {
            if (CheatFlags.BlockPlayerInput)
                return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.hKey.wasPressedThisFrame)
                return;

            HudVisibility.Visible = !HudVisibility.Visible;
        }
    }
}
