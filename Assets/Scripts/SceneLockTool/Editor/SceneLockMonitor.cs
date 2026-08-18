using UnityEditor;
using UnityEngine;
using static EditorWindowExtensions;

namespace Utils.Core.SceneLockTool
{
    [InitializeOnLoad]
    public class SceneLockMonitor
    {
        private static double nextCheckTime;
        /// <summary>
        /// How often to check for scene lock changes.
        /// </summary>
        private static float checkInterval = 10f;

        static SceneLockMonitor()
        {
            EditorApplication.update += Update;
        }

        private static void Update()
        {
            if (!SceneLockTool.AutoUpdate) return;
            if (EditorApplication.timeSinceStartup > nextCheckTime)
            {
                nextCheckTime = EditorApplication.timeSinceStartup + checkInterval;
                if (IsWindowOpen(out SceneLockTool window))
                    window.RefreshActiveSceneLock();
                else
                    SceneLockTool.Refresh();
            }
        }
    }
}
