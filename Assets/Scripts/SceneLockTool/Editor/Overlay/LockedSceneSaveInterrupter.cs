using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Utils.Core.SceneLockTool
{
    [InitializeOnLoad]
    public static class LockedSceneSaveInterrupter
    {
        private static bool IgnoreForSession
        {
            get => SessionState.GetBool("SceneLockTool_IgnoreForSession", false);
            set => SessionState.SetBool("SceneLockTool_IgnoreForSession", value);
        }

        static LockedSceneSaveInterrupter()
        {
            EditorSceneManager.sceneSaved += OnSceneSaved;
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }
        
        private static void OnSceneSaved(Scene scene)
        {
            if (IgnoreForSession) return;
            if (SceneLockTool.IsSceneLocked(scene.name) &&
                SceneLockTool.GetSceneLockOwner(scene.name) != SceneLockTool.GetDeviceID())
            {
                bool dialog = EditorUtility.DisplayDialog("Scene is locked",
                    $"Scene '{scene.name}' is locked by {SceneLockTool.GetSceneLockOwnerUsername(scene.name)} and should not be pushed.",
                    "OK",
                    "Ignore warning for this session.");
                IgnoreForSession = !dialog;
            }
        }
        private static void OnSceneOpened(Scene scene, OpenSceneMode mode)
        {
            if (SceneLockTool.IsSceneLocked(scene.name) &&
                SceneLockTool.GetSceneLockOwner(scene.name) != SceneLockTool.GetDeviceID())
            {
                EditorUtility.DisplayDialog("Scene is locked",
                    $"Scene '{scene.name}' is currently locked by {SceneLockTool.GetSceneLockOwnerUsername(scene.name)}. Please do not push changes to this scene.",
                    "OK");
            }
        }
    }
}