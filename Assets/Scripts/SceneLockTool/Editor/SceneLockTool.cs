using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Utils.Core.SceneLockTool
{
    public partial class SceneLockTool : EditorWindow
    {
        private static SceneLockInterfacer interfacer;

        private static readonly Dictionary<string, SceneLockSceneObject> SceneLockCache =
            new Dictionary<string, SceneLockSceneObject>();

        private static readonly HashSet<string> PendingSceneLockRequests = new HashSet<string>();
        private static bool initialized;
        private static string username = "";
        private static GUIStyle prefixStyle;
        private bool doesUserExist;
        private bool doesProjectExist;
        private SceneLockSceneObject sceneLockObject;
        private Vector2 scrollPosition;
        private string statusMessage = "";
        private string errorMessage = "";

        private GUIContent DebugToggleIcon => new GUIContent("",
                EditorGUIUtility.IconContent(DebugMode ? "d_DebuggerAttached" : "d_DebuggerDisabled").image,
                "Debug Mode");

        /// <summary>
        /// Whether to automatically check for lock changes.
        /// </summary>
        public static bool AutoUpdate
        {
            get => EditorPrefs.GetBool("SceneLockTool_AutoUpdate", true);
            private set => EditorPrefs.SetBool("SceneLockTool_AutoUpdate", value);
        }

        private static bool DebugMode
        {
            get => EditorPrefs.GetBool("SceneLockTool_DebugMode", false);
            set => EditorPrefs.SetBool("SceneLockTool_DebugMode", value);
        }

        [MenuItem("Utils/Scene Lock Tool %&s")]
        public static void ShowWindow()
        {
            SceneLockTool window = GetWindow<SceneLockTool>("Scene Lock Tool");
            window.minSize = new Vector2(300, 300);
            window.Show();
        }

        private static void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;
            interfacer?.Dispose();
            interfacer = new SceneLockInterfacer();
            interfacer.GetUsername(interfacer.DeviceId, (result, response) =>
            {
                if (result == WebRequestResult.Success)
                    username = response.Split('_')[1];
            });
            WarmSceneLockCache();
        }

        private static void SetupStyles()
        {
            if (prefixStyle is null)
            {
                prefixStyle = new GUIStyle(EditorStyles.label);
                prefixStyle.active.textColor = prefixStyle.normal.textColor;
                prefixStyle.focused.textColor = prefixStyle.normal.textColor;
            }
        }

        private static void WarmSceneLockCache()
        {
            if (interfacer == null)
                return;

            foreach (string sceneName in GetProjectSceneNames())
                RefreshSceneLockCache(sceneName, null);
        }

        private void OnEnable()
        {
            EnsureInitialized();
            EditorSceneManager.activeSceneChangedInEditMode += OnActiveSceneChanged;
            RefreshEverything();
        }

        private void OnDisable()
        {
            EditorSceneManager.activeSceneChangedInEditMode -= OnActiveSceneChanged;
            PendingSceneLockRequests.Clear();
        }
        
        public void OnGUI()
        {
            if (interfacer == null)
                EnsureInitialized();
            SetupStyles();
            DrawToolbar();
            switch (page)
            {
                case 0:
                    scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);
                    DrawHeader();
                    DrawConnectionCard();
                    DrawSetupCard();
                    DrawActiveSceneCard();
                    DrawFooterStatus();
                    EditorGUILayout.EndScrollView();
                    break;
                case 1:
                    DrawScenes();
                    break;
            }
        }
        
        private void DrawHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.BeginHorizontal();
                {
                    GUILayout.Label("Scene Lock", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(DebugToggleIcon, GUILayout.Height(EditorGUIUtility.singleLineHeight)))
                        DebugMode = !DebugMode;
                }
                GUILayout.EndHorizontal();
                EditorGUILayout.LabelField("Active Scene", SceneManager.GetActiveScene().name);
                if (DebugMode)
                    AutoUpdate = EditorGUILayout.Toggle(new GUIContent("Auto Update", "Whether or not to automatically check for lock changes."), AutoUpdate);

            }
            EditorGUILayout.EndVertical();
        }


        private void DrawConnectionCard()
        {
            if (DebugMode)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    DisplayConnectionInfo();
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void DisplayConnectionInfo()
        {
            GUILayout.Label("Connection", EditorStyles.boldLabel);

            interfacer.Url = EditorGUILayout.TextField("Endpoint", interfacer.Url);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField("Product", interfacer.ProductName);
                EditorGUILayout.TextField("Device ID", interfacer.DeviceId);
                EditorGUILayout.TextField("Project ID",
                    string.IsNullOrWhiteSpace(interfacer.ProjectId) ? "(unknown)" : interfacer.ProjectId);
            }

            if (GUILayout.Button(new GUIContent(" Refresh", EditorGUIUtility.IconContent("Refresh").image), GUILayout.Height(24f)))
                RefreshEverything();

            if (GUILayout.Button(new GUIContent(" Sync Scenes", EditorGUIUtility.IconContent("SyncSearch").image), GUILayout.Height(24f)))
                SyncProjectScenes();
        }

        private void DrawSetupCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.Label("Setup", EditorStyles.boldLabel);

                DrawStateLine("User", doesUserExist ? username : "Missing", doesUserExist);
                DrawStateLine("Project", doesProjectExist ? $"{interfacer?.ProductName}" : "Missing", doesProjectExist);
                EditorGUILayout.LabelField("Registered Scenes", registeredSceneCount.ToString());

                if (!doesUserExist)
                {
                    EditorGUILayout.Space(4f);
                    username = EditorGUILayout.TextField("Username", username);
                    using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(username) || username.Trim().Length < 3 || username.Trim().Length > 20))
                    {
                        if (GUILayout.Button("Create Account", GUILayout.Height(24f)))
                            CreateAccount();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawActiveSceneCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                GUILayout.Label("Active Scene Lock", EditorStyles.boldLabel);

                DrawActiveLockStatus();

                if (!AutoUpdate || DebugMode)
                    if (GUILayout.Button(new GUIContent(" Refresh Lock State", EditorGUIUtility.IconContent("Refresh").image), GUILayout.Height(26f)))
                        RefreshActiveSceneLock();

            }
            EditorGUILayout.EndVertical();
        }

        private void DrawActiveLockStatus()
        {
            string activeSceneName = SceneManager.GetActiveScene().name;
            bool hasLock = sceneLockObject is { HasSceneLock: true };

            GUIStyle style = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = hasLock ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.2f, 0.7f, 0.2f) },
                hover = { textColor = hasLock ? new Color(0.75f, 0.75f, 0.75f) : new Color(0.2f, 0.7f, 0.2f) }
            };

            DrawSceneLockStatus(hasLock, style, activeSceneName);
        }

        private void DrawSceneLockStatus(bool hasLock, GUIStyle style, string activeSceneName)
        {
            if (hasLock)
            {
                bool ownedByMe = sceneLockObject.SceneLock.OwnerDeviceID == interfacer.DeviceId;
                DrawLockInfo(style, ownedByMe);

                if (ownedByMe)
                    DrawReleaseSceneLock(activeSceneName);
            }
            else
            {
                DrawLockButton(style, activeSceneName);
            }
        }

        private void DrawLockInfo(GUIStyle style, bool ownedByMe)
        {
            EditorGUILayout.LabelField(new GUIContent("Status: ", EditorGUIUtility.IconContent("Locked").image),
                ownedByMe
                    ? new GUIContent("Locked by you")
                    : new GUIContent($"Locked by {sceneLockObject.SceneLock.OwnerName}"), style);
            EditorGUILayout.LabelField(
                new GUIContent(" Lock Time", EditorGUIUtility.IconContent("UnityEditor.AnimationWindow").image),
                new GUIContent(sceneLockObject.SceneLock.LockTime));
        }

        private void DrawLockButton(GUIStyle style, string activeSceneName)
        {
            EditorGUILayout.LabelField(new GUIContent("Status: ", EditorGUIUtility.IconContent("Unlocked").image), new GUIContent("Unlocked"), style);
            using (new EditorGUI.DisabledScope(!doesUserExist || !doesProjectExist))
            {
                if (GUILayout.Button(new GUIContent(" Lock Scene", EditorGUIUtility.IconContent("Locked").image), GUILayout.Height(26f)))
                {
                    interfacer.SetSceneLock(activeSceneName, (result, response) =>
                    {
                        HandleStatus(result, response, "Scene locked.");
                        RefreshActiveSceneLock();
                        SceneLockOverlay.ForceRepaint();
                    });
                }
            }
        }

        private void DrawReleaseSceneLock(string activeSceneName)
        {
            if (GUILayout.Button(
                    new GUIContent(" Unlock Scene", EditorGUIUtility.IconContent("Unlocked").image),
                    GUILayout.Height(26f)))
            {
                interfacer.ReleaseSceneLock(sceneLockObject.SceneLock.SceneLockID, (result, response) =>
                {
                    HandleStatus(result, response, "Scene unlocked.");
                    SceneLockCache.Remove(activeSceneName);
                    RefreshActiveSceneLock();
                    SceneLockOverlay.ForceRepaint();
                });
            }
        }

        private void DrawFooterStatus()
        {
            string loading = interfacer.GetLoadingMessage();

            GUIContent content = new GUIContent();
            if (!string.IsNullOrWhiteSpace(loading))
                content = new GUIContent(" " + loading, EditorGUIUtility.IconContent("console.infoicon.sml").image);

            if (!string.IsNullOrWhiteSpace(statusMessage))
                content = new GUIContent(" " + statusMessage,
                    EditorGUIUtility.IconContent("console.infoicon.sml").image);

            if (!string.IsNullOrWhiteSpace(errorMessage))
                content = new GUIContent(" " + errorMessage,
                    EditorGUIUtility.IconContent("console.erroricon.sml").image);

            if (string.IsNullOrWhiteSpace(content.text)) return;

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                GUILayout.Label(content, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawStateLine(string label, string value, bool ok)
        {
            GUIStyle labelStyle = new GUIStyle(EditorStyles.label)
            {
                normal = { textColor = ok ? new Color(0.2f, 0.7f, 0.2f) : new Color(0.8f, 0.45f, 0.2f) },
                hover = { textColor = ok ? new Color(0.2f, 0.7f, 0.2f) : new Color(0.8f, 0.45f, 0.2f) }
            };

            Rect rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            Rect valueRect = EditorGUI.PrefixLabel(rowRect, new GUIContent(label), prefixStyle);
            EditorGUI.LabelField(valueRect, value, labelStyle);
        }
        
        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUIStyle baseStyle = new GUIStyle(EditorStyles.toolbarButton)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(6, 6, 4, 4)
                };

                GUIStyle selectedStyle = new GUIStyle(baseStyle)
                {
                    fontStyle = FontStyle.Bold
                };

                GUIContent homeContent = new GUIContent(" Home", EditorGUIUtility.IconContent("d_UnityEditor.SceneHierarchyWindow").image);
                GUIContent scenesContent = new GUIContent(" Scenes", EditorGUIUtility.IconContent("d_SceneAsset Icon").image);

                GUILayoutOption btnWidth = GUILayout.Width(position.width / 2);

                if (GUILayout.Toggle(page == 0, homeContent, page == 0 ? selectedStyle : baseStyle, GUILayout.Height(22), btnWidth))
                    page = 0;

                if (GUILayout.Toggle(page == 1, scenesContent, page == 1 ? selectedStyle : baseStyle, GUILayout.Height(22), btnWidth))
                    page = 1;

                GUILayout.FlexibleSpace();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}





