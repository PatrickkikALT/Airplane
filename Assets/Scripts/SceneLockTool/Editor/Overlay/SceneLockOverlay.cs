using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Utils.Core.SceneLockTool
{
    [InitializeOnLoad]
    public static class SceneLockOverlay
    {
        private static Texture2D lockIcon;
        private static Color lockedNotOwnerColor = new Color(0.8f, 0.1f, 0f, 0.2f);

        static SceneLockOverlay()
        {
            lockIcon = EditorGUIUtility.IconContent("Locked").image as Texture2D;
            EditorApplication.projectWindowItemOnGUI += OnProjectWindowGUI;
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyWindowGUI;
        }

        private static void OnProjectWindowGUI(string guid, Rect rect)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);

            if (!path.EndsWith(".unity"))
                return;

            string scene = Path.GetFileNameWithoutExtension(path);

            if (!SceneLockTool.IsSceneLocked(scene))
                return;

            bool isGrid = rect.height > 20f;
            float size = 16f;
            Rect iconRect = isGrid 
                ? new Rect(rect.xMax - size, rect.yMax - size - 12f, size, size) 
                : new Rect(rect.xMin - size + 4, rect.yMax - size, size, size);

            bool isOwner = SceneLockTool.GetSceneLockOwner(scene) == SceneLockTool.GetDeviceID();
            GUI.DrawTexture(iconRect, lockIcon);
            string tooltip = isOwner
                ? "Scene is locked by you"
                : $"Scene is locked by {SceneLockTool.GetSceneLockOwnerUsername(scene)}";
            GUI.Label(iconRect, new GUIContent("", tooltip));
        }

        private static void OnHierarchyWindowGUI(EntityId entityId, Rect rect)
        {
            GameObject obj = EditorUtility.EntityIdToObject(entityId) as GameObject;

            // Scene headers have a valid EntityId but a null object.
            if (!obj)
            {
                Scene scene = SceneManager.GetActiveScene();
                if (!scene.IsValid()) return;

                bool isLocked = SceneLockTool.IsSceneLocked(scene.name);

                if (isLocked)
                    EditorGUI.DrawRect(rect, lockedNotOwnerColor);
                GUIContent icon = EditorGUIUtility.IconContent(isLocked ? "Locked@2x" : "Unlocked@2x");

                Rect iconRect = new Rect(rect.xMax - 24f, rect.y - 2f, 20f, 20f);

                string tooltip = "";
                if (isLocked)
                {
                    bool isOwner = SceneLockTool.GetSceneLockOwner(scene.name) == SceneLockTool.GetDeviceID();
                    tooltip = isOwner
                        ? "Scene is locked by you"
                        : $"Scene is locked by {SceneLockTool.GetSceneLockOwnerUsername(scene.name)}";
                }

                GUI.Label(iconRect, new GUIContent("", icon.image, tooltip));
            }
        }
        
        public static void ForceRepaint()
        {
            EditorApplication.RepaintProjectWindow();
            EditorApplication.RepaintHierarchyWindow();
        }
    }
}