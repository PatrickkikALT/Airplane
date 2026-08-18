using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Utils.Core.SceneLockTool
{
    /// <summary>
    /// The filter criteria for scene status in the Scene Lock Tool.
    /// </summary>
    internal enum SceneStatusFilter
    {
        All,
        Locked,
        LockedByMe,
        Unlocked
    }

    /// <summary>
    /// The filter criteria for scene lock time in the Scene Lock Tool.
    /// </summary>
    internal enum SceneLockTimeFilter
    {
        Any,
        Last24Hours,
        OlderThan24Hours,
    }
    
    public partial class SceneLockTool
    {
        private Vector2 scenesScrollPosition;
        private int registeredSceneCount;
        private int page;
        private string scenesSearch = string.Empty;
        private SceneStatusFilter scenesStatusFilter = SceneStatusFilter.All;
        private SceneLockTimeFilter scenesLockTimeFilter = SceneLockTimeFilter.Any;
        
        /// <summary>
        /// Draws the scene list.
        /// </summary>
        private void DrawScenes()
        {
            DrawScenesFiltersToolbar();
            List<KeyValuePair<string, SceneLockSceneObject>> scenes = BuildFilteredSceneList();

            scenesScrollPosition = EditorGUILayout.BeginScrollView(scenesScrollPosition);
            
            if (scenes.Count == 0)
                EditorGUILayout.HelpBox("No scenes match the current filters.", MessageType.Info);

            DrawSceneSummary(scenes);
            EditorGUILayout.EndScrollView();
        }

        private void DrawSceneSummary(List<KeyValuePair<string, SceneLockSceneObject>> scenes)
        {
            foreach (KeyValuePair<string, SceneLockSceneObject> scenePair in scenes)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    GUILayout.Label(scenePair.Key, EditorStyles.boldLabel);
                    if (scenePair.Value.HasSceneLock)
                        DisplaySceneLockStatus(scenePair);
                    else
                        EditorGUILayout.LabelField(new GUIContent("Status: ", EditorGUIUtility.IconContent("Unlocked").image), new GUIContent("Unlocked"));
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void DisplaySceneLockStatus(KeyValuePair<string, SceneLockSceneObject> scenePair)
        {
            bool ownedByMe = scenePair.Value.SceneLock.OwnerDeviceID == interfacer.DeviceId;
            EditorGUILayout.LabelField(new GUIContent("Status: ", EditorGUIUtility.IconContent("Locked").image),
                ownedByMe
                    ? new GUIContent("Locked by you")
                    : new GUIContent($"Locked by {scenePair.Value.SceneLock.OwnerName}"));
            string lockTimeLabel = scenePair.Value.SceneLock.LockTime;
            if (TryGetSceneLockTime(scenePair.Value, out DateTime lockTime))
                lockTimeLabel = $"{lockTime:G} ({FormatRelativeLockTime(DateTime.Now, lockTime)})";

            EditorGUILayout.LabelField(new GUIContent(" Lock Time", EditorGUIUtility.IconContent("UnityEditor.AnimationWindow").image), new GUIContent(lockTimeLabel));
            if (ownedByMe)
                DrawUnlockButton(scenePair);
        }

        private void DrawUnlockButton(KeyValuePair<string, SceneLockSceneObject> scenePair)
        {
            if (GUILayout.Button("Unlock Scene", GUILayout.Height(26f)))
            {
                interfacer.ReleaseSceneLock(scenePair.Value.SceneLock.SceneLockID, (result, response) =>
                {
                    HandleStatus(result, response, "Scene unlocked.");
                    SceneLockCache.Remove(scenePair.Key);
                    RefreshActiveSceneLock();
                    SceneLockOverlay.ForceRepaint();
                    Repaint();
                });
            }
        }

        private void DrawScenesFiltersToolbar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.Label("Scene Filters", EditorStyles.boldLabel);

                scenesStatusFilter = (SceneStatusFilter)EditorGUILayout.EnumPopup("Status", scenesStatusFilter);
                scenesLockTimeFilter = (SceneLockTimeFilter)EditorGUILayout.EnumPopup("Lock Time", scenesLockTimeFilter);
                scenesSearch = EditorGUILayout.TextField("Search", scenesSearch ?? string.Empty);

                if (GUILayout.Button("Clear Filters", GUILayout.Height(26f)))
                {
                    scenesStatusFilter = SceneStatusFilter.All;
                    scenesLockTimeFilter = SceneLockTimeFilter.Any;
                    scenesSearch = string.Empty;
                }
            }
            EditorGUILayout.EndVertical();
        }

        private List<KeyValuePair<string, SceneLockSceneObject>> BuildFilteredSceneList()
        {
            DateTime now = DateTime.Now;
            IEnumerable<KeyValuePair<string, SceneLockSceneObject>> query = SceneLockCache;

            query = query.Where(IsSceneMatchingStatusFilter);
            query = query.Where(s => IsSceneMatchingLockTimeFilter(s.Value, now));
            query = query.Where(IsSceneMatchingSearch);
            query = query.OrderBy(s => !s.Value.HasSceneLock);
            return query.ToList();
        }

        private bool IsSceneMatchingStatusFilter(KeyValuePair<string, SceneLockSceneObject> sceneEntry)
        {
            bool hasLock = sceneEntry.Value.HasSceneLock;
            bool ownedByMe = hasLock && sceneEntry.Value.SceneLock.OwnerDeviceID == interfacer.DeviceId;

            return scenesStatusFilter switch
            {
                SceneStatusFilter.Locked => hasLock,
                SceneStatusFilter.Unlocked => !hasLock,
                SceneStatusFilter.LockedByMe => ownedByMe,
                _ => true
            };
        }

        private bool IsSceneMatchingSearch(KeyValuePair<string, SceneLockSceneObject> sceneEntry)
        {
            if (string.IsNullOrWhiteSpace(scenesSearch))
                return true;

            string term = scenesSearch.Trim();
            if (sceneEntry.Key.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (sceneEntry.Value.HasSceneLock && sceneEntry.Value.SceneLock.OwnerName.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            return false;
        }

        private bool IsSceneMatchingLockTimeFilter(SceneLockSceneObject scene, DateTime now)
        {
            if (scenesLockTimeFilter == SceneLockTimeFilter.Any)
                return true;

            TryGetSceneLockTime(scene, out DateTime lockTime);

            if (lockTime == default)
                return false;
            return scenesLockTimeFilter switch
            {
                SceneLockTimeFilter.Last24Hours => (now - lockTime) <= TimeSpan.FromHours(24),
                SceneLockTimeFilter.OlderThan24Hours => (now - lockTime) > TimeSpan.FromHours(24),
                _ => true
            };
        }

        private static bool TryGetSceneLockTime(SceneLockSceneObject scene, out DateTime lockTime)
        {
            lockTime = default;
            if (scene is not { HasSceneLock: true } || scene.SceneLock == null || string.IsNullOrWhiteSpace(scene.SceneLock.LockTime))
                return false;

            return DateTime.TryParse(scene.SceneLock.LockTime, out lockTime);
        }

        private static string FormatRelativeLockTime(DateTime now, DateTime lockTime)
        {
            TimeSpan age = now - lockTime;
            if (age.TotalMinutes < 1)
                return "just now";
            if (age.TotalHours < 1)
                return $"{Math.Max(1, (int)Math.Round(age.TotalMinutes))}m ago";
            if (age.TotalDays < 1)
                return $"{Math.Max(1, (int)Math.Round(age.TotalHours))}h ago";

            return $"{Math.Max(1, (int)Math.Round(age.TotalDays))}d ago";
        }
    }
}
