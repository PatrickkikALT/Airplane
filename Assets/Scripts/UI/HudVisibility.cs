using UnityEngine;
using UnityEngine.InputSystem;

namespace Airplane.UI
{
    /// <summary>
    /// Session-local overlay switch for screenshots and cinematics. Does not change per-aircraft HUD
    /// flags, so bots and remote proxies stay hidden when overlays come back on.
    /// </summary>
    public static class HudVisibility
    {
        public static bool Visible { get; set; } = true;
    }

    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-150)]
    [AddComponentMenu("Airplane/UI/HUD Toggle")]
    public sealed class HudToggle : MonoBehaviour
    {
        private static HudToggle _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            GameObject host = new GameObject("HUD Toggle");
            DontDestroyOnLoad(host);
            host.AddComponent<HudToggle>();
        }

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
