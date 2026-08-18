using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Utils.Core.SceneLockTool
{
    public partial class SceneLockTool
    {
        private static IEnumerable<string> GetProjectSceneNames()
        {
            HashSet<string> allScenesInProject = new HashSet<string>();
            string[] guids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets" });

            foreach (string guid in guids)
            {
                string scenePath = AssetDatabase.GUIDToAssetPath(guid);
                string sceneName = Path.GetFileNameWithoutExtension(scenePath);

                if (!scenePath.Contains("ThirdParty") && !string.IsNullOrWhiteSpace(sceneName))
                    allScenesInProject.Add(sceneName);
            }

            return allScenesInProject;
        }

        private void RefreshEverything()
        {
            statusMessage = string.Empty;
            errorMessage = string.Empty;
            interfacer.GetUserKnown((result, response) =>
            {
                doesUserExist = result == WebRequestResult.Success;
                HandleStatus(result, response, doesUserExist ? "User found." : "User account not found.");

                if (!doesUserExist)
                {
                    Repaint();
                    return;
                }
                EnsureUsernameSet();
                EnsureProjectReadyAndSyncScenes();
            });

            RefreshActiveSceneLock();
        }

        private void EnsureUsernameSet()
        {
            interfacer.GetUsername(interfacer.DeviceId, (result, response) =>
            {
                if (result == WebRequestResult.Success)
                    username = response.Split('_')[1];
            });
        }

        private void EnsureProjectReadyAndSyncScenes()
        {
            interfacer.GetProjectKnown((result, response) =>
            {
                if (result == WebRequestResult.Success)
                {
                    doesProjectExist = true;
                    ResolveProjectIdAndSync();
                    return;
                }

                interfacer.SetNewProject((createResult, createResponse) =>
                {
                    doesProjectExist = createResult == WebRequestResult.Success;
                    HandleStatus(createResult, createResponse,
                        doesProjectExist ? "Project registered." : "Unable to register project.");

                    if (doesProjectExist)
                        ResolveProjectIdAndSync();

                    Repaint();
                });

                HandleStatus(result, response, "Project not found. Creating project entry...");
            });
        }

        private void ResolveProjectIdAndSync()
        {
            interfacer.GetProjectId((result, response) =>
            {
                if (result != WebRequestResult.Success || string.IsNullOrWhiteSpace(interfacer.ProjectId))
                {
                    HandleStatus(result, response, "Unable to resolve project id.");
                    Repaint();
                    return;
                }

                SyncProjectScenes(false);
            });
        }

        private void SyncProjectScenes(bool setStatusMessage = true)
        {
            if (string.IsNullOrWhiteSpace(interfacer.ProjectId))
            {
                errorMessage = "Project ID is missing. Refresh setup first.";
                Repaint();
                return;
            }

            List<string> localScenes = GetProjectSceneNames().OrderBy(n => n).ToList();

            interfacer.GetProjectScenes((result, response) =>
            {
                List<string> remoteScenes = new List<string>();
                if (result == WebRequestResult.Success)
                    SceneLockInterfacer.TryParseSceneNames(response, out remoteScenes);

                bool needsSync = localScenes.Count != remoteScenes.Count || !localScenes.SequenceEqual(remoteScenes);
                if (!needsSync)
                {
                    registeredSceneCount = Math.Max(remoteScenes.Count, localScenes.Count);
                    registeredSceneCount = Math.Max(remoteScenes.Count, localScenes.Count);
                    statusMessage = setStatusMessage
                        ? $"Scenes already synced ({registeredSceneCount})."
                        : string.Empty;
                    errorMessage = string.Empty;
                    Repaint();
                    return;
                }

                interfacer.SetProjectScenes(localScenes, (pushResult, pushResponse) =>
                {
                    HandleStatus(pushResult, pushResponse, "Project scenes synced.");
                    registeredSceneCount = localScenes.Count;
                });
            }, interfacer.ProjectId);
        }

        private void CreateAccount()
        {
            string trimmed = (username ?? string.Empty).Trim();
            switch (trimmed.Length)
            {
                case < 3:
                    errorMessage = "Username must be at least 3 characters.";
                    return;
                case > 20:
                    errorMessage = "Username must be less than 20 characters.";
                    return;
            }
            
            interfacer.SetNewUser(trimmed, (result, response) =>
            {
                doesUserExist = result == WebRequestResult.Success;
                HandleStatus(result, response, doesUserExist ? "Account created." : "Account creation failed.");

                if (doesUserExist)
                {
                    username = trimmed;
                    EnsureProjectReadyAndSyncScenes();
                }

                Repaint();
            });
        }

        public void RefreshActiveSceneLock()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            RefreshSceneLockCache(sceneName, (result, response) =>
            {
                sceneLockObject = null;

                if (result == WebRequestResult.Success && SceneLockInterfacer.TryParseSceneLockData(response, out SceneLockSceneObject lockData))
                {
                    sceneLockObject = lockData;
                }
                Repaint();
                SceneLockOverlay.ForceRepaint();
            });
        }

        public static void Refresh()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            RefreshSceneLockCache(sceneName, (result, response) =>
            {
                SceneLockOverlay.ForceRepaint();
            });
        }

        private void HandleStatus(WebRequestResult result, string response, string successMessage)
        {
            if (result == WebRequestResult.Success)
            {
                statusMessage = successMessage;
                errorMessage = string.Empty;
                return;
            }

            statusMessage = string.Empty;
            errorMessage = FormatError(response);
        }

        private string FormatError(string response)
        {
            string error = "error";
            int first = response.IndexOf(error, StringComparison.Ordinal);
            if (first != -1)
            {
                int second = response.IndexOf(error, first + error.Length, StringComparison.Ordinal);

                if (second != -1)
                {
                    int start = second + error.Length;
                    string result = response[start..].Trim();

                    return result;
                }
            }
            else if (response.Contains("GetUserKnownfailure")) return UserUnknown;
            return response;
        }

        private string UserUnknown => "User unknown, create an account.";


        private void OnActiveSceneChanged(Scene previousScene, Scene newScene)
        {
            if (!newScene.IsValid())
                return;

            statusMessage = $"Switched to scene '{newScene.name}'.";
            errorMessage = string.Empty;
            RefreshActiveSceneLock();
            Repaint();
        }

        public static bool IsSceneLocked(string sceneName)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(sceneName) || interfacer == null)
                return false;

            if (SceneLockCache.TryGetValue(sceneName, out SceneLockSceneObject cached))
                return cached is { HasSceneLock: true };

            RefreshSceneLockCache(sceneName, null);
            return false;
        }

        public static string GetDeviceID() => interfacer?.DeviceId;

        public static string GetSceneLockOwnerUsername(string sceneName)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(sceneName) || interfacer == null)
                return string.Empty;
            if (SceneLockCache.TryGetValue(sceneName, out SceneLockSceneObject cached))
                return cached.SceneLock.OwnerName;

            RefreshSceneLockCache(sceneName, null);
            return string.Empty;
        }

        public static string GetSceneLockOwner(string sceneName)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(sceneName) || interfacer == null)
                return string.Empty;
            if (SceneLockCache.TryGetValue(sceneName, out SceneLockSceneObject cached))
                return cached.SceneLock.OwnerDeviceID;

            RefreshSceneLockCache(sceneName, null);
            return string.Empty;
        }

        public static bool IsSceneLockedByClient(string sceneName)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(sceneName) || interfacer == null)
                return false;
            if (SceneLockCache.TryGetValue(sceneName, out SceneLockSceneObject cached))
            {
                if (cached.SceneLock == null)
                    return false;
                return cached.SceneLock.OwnerDeviceID == interfacer.DeviceId;
            }

            RefreshSceneLockCache(sceneName, null);
            return false;
        }

        private static void RefreshSceneLockCache(string sceneName, Action<WebRequestResult, string> callback)
        {
            if (string.IsNullOrWhiteSpace(sceneName) || interfacer == null)
                return;

            if (!PendingSceneLockRequests.Add(sceneName))
                return;

            interfacer.GetSceneLockData(sceneName, (result, response) =>
            {
                PendingSceneLockRequests.Remove(sceneName);

                if (result == WebRequestResult.Success &&
                    SceneLockInterfacer.TryParseSceneLockData(response, out SceneLockSceneObject parsed))
                {
                    SceneLockCache[sceneName] = parsed;
                    SceneLockInterfacer.InvokeOnStatusLockChanged(parsed.HasSceneLock);
                }
                else
                    SceneLockCache.Remove(sceneName);

                callback?.Invoke(result, response);
                EditorApplication.delayCall += EditorApplication.RepaintProjectWindow;
            });
        }

        public static void ToggleScene(string sceneName, Action<WebRequestResult, string> callback)
        {
            EditorWindowExtensions.IsWindowOpen(out SceneLockTool window);
            interfacer.GetSceneLockData(sceneName, (result, s) =>
            {
                bool parsed = SceneLockInterfacer.TryParseSceneLockData(s, out SceneLockSceneObject lockData);
                if (result == WebRequestResult.Success && parsed)
                {
                    if (lockData.HasSceneLock && lockData.SceneLock.OwnerDeviceID != interfacer.DeviceId)
                    {
                        callback?.Invoke(WebRequestResult.Failed,
                            $"Scene is locked by {lockData.SceneLock.OwnerName}.");
                        return;
                    }
                }

                bool shouldLock = !(lockData?.HasSceneLock ?? false);
                if (shouldLock)
                {
                    interfacer.SetSceneLock(sceneName, ((requestResult, s1) =>
                    {
                        window?.HandleStatus(requestResult, s1, "Scene locked.");
                        if (window)
                            window.RefreshActiveSceneLock();
                        else
                            Refresh();
                        SceneLockOverlay.ForceRepaint();
                        callback?.Invoke(requestResult, s1);
                    }), shouldSendEvent: false);
                }
                else
                {
                    interfacer.ReleaseSceneLock(lockData.SceneLock.SceneLockID, (result1, response) =>
                    {
                        window?.HandleStatus(result1, response, "Scene unlocked.");
                        SceneLockCache.Remove(sceneName);
                        window?.RefreshActiveSceneLock();
                        SceneLockOverlay.ForceRepaint();
                        callback?.Invoke(result1, response);
                    }, shouldSendEvent: false);
                }
            });
        }
        
    }
}