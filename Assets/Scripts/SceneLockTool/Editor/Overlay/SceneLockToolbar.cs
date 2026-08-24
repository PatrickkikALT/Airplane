using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Utils.Core.SceneLockTool;

namespace Utils.Core.SceneLockTool
{
    [EditorToolbarElement("SceneLock/Status", typeof(SceneView))]
    public class SceneLockIcon : EditorToolbarToggle
    {
        private Color lockedNotOwnerColor = new Color(0.8f, 0.2f, 0.2f, 0.4f);

        public SceneLockIcon()
        {
            onIcon = EditorGUIUtility.IconContent("Locked").image as Texture2D;
            offIcon = EditorGUIUtility.IconContent("Unlocked").image as Texture2D;

            EditorApplication.delayCall += () => { schedule.Execute(RefreshStatus).ExecuteLater(1); };
            EditorSceneManager.activeSceneChangedInEditMode += OnSceneChanged;
            AssemblyReloadEvents.afterAssemblyReload += RefreshStatus;
            SceneLockInterfacer.OnStatusLockChanged += RefreshStatus;
            RegisterCallback<DetachFromPanelEvent>(Detached);
        }

        public void Detached(DetachFromPanelEvent evt)
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnSceneChanged;
            AssemblyReloadEvents.afterAssemblyReload -= RefreshStatus;
            SceneLockInterfacer.OnStatusLockChanged -= RefreshStatus;
            UnregisterCallback<DetachFromPanelEvent>(Detached);
        }

        private void OnSceneChanged(Scene oldScene, Scene newScene)
        {
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            bool newValue = SceneLockTool.IsSceneLocked(SceneManager.GetActiveScene().name);
            value = newValue;
            SetLockColor(newValue);
        }

        private void RefreshStatus(bool newValue)
        {
            value = newValue;
            SetLockColor(newValue);
        }

        private void SetLockColor(bool isLocked)
        {
            if (isLocked)
            {
                if (!SceneLockTool.IsSceneLockedByClient(SceneManager.GetActiveScene().name))
                {
                    style.backgroundColor = lockedNotOwnerColor;
                    return;
                }
            }

            style.backgroundColor = StyleKeyword.Null;
        }

        [Obsolete]
        protected override void ExecuteDefaultAction(EventBase evt)
        {
            if (evt.eventTypeId == MouseDownEvent.TypeId())
            {
                SceneLockTool.ToggleScene(SceneManager.GetActiveScene().name,
                    (WebRequestResult result, string response) => { HandleResult(evt, result, response); });
            }
        }

        private static void HandleResult(EventBase evt, WebRequestResult result, string response)
        {
            switch (result)
            {
                case WebRequestResult.Success:
                    break;
                case WebRequestResult.Failed:
                    if (response.Contains("is locked"))
                        EditorUtility.DisplayDialog("Scene Lock",
                            $"Scene is locked by {SceneLockTool.GetSceneLockOwnerUsername(SceneManager.GetActiveScene().name)} ",
                            "OK");
                    else
                        Debug.Log(response);
                    break;
                case WebRequestResult.Null:
                case WebRequestResult.NoInternet:
                case WebRequestResult.Unknown:
                default:
                    break;
            }
        }
    }

    [Overlay(typeof(SceneView), "Scene Lock")]
    public class SceneLockToolbar : ToolbarOverlay
    {
        public SceneLockToolbar() : base("SceneLock/Status")
        {
        }
    }
}