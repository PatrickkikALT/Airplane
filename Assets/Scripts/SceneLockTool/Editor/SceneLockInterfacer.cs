using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;

namespace Utils.Core.SceneLockTool
{
    public sealed class SceneLockInterfacer
    {
        
        // PHP POST keys used for requests.
        private const string GET_REPLY_KEY = "get_reply_key";
        private const string GET_USER_KNOWN_KEY = "get_user_known"; private const string SET_NEW_USER_KEY = "set_new_user";
        private const string NEW_USERNAME_KEY = "new_username";
        private const string GET_PROJECT_KNOWN_KEY = "get_project_known";
        private const string SET_NEW_PROJECT_KEY = "set_new_project";
        private const string GET_PROJECT_ID_KEY = "get_project_id";
        private const string GET_PROJECT_SCENES_KEY = "get_project_scenes";
        private const string SET_PROJECT_SCENES_KEY = "set_project_scenes";
        private const string PROJECT_SCENE_NAMES_KEY = "project_scene_names";
        private const string GET_SCENE_LOCK_DATA_KEY = "get_scene_lock_data";
        private const string SET_SCENE_LOCK_KEY = "set_scene_lock";
        private const string RELEASE_SCENE_LOCK_KEY = "release_scene_lock";
        private const string GET_USERNAME_KEY = "get_username";
        private const string PRODUCT_NAME_KEY = "product_name";
        private const string USER_DEVICE_KEY = "user_device";

        private readonly Dictionary<string, RunningWebRequest> runningRequests = new Dictionary<string, RunningWebRequest>();
        
        public string Url { get; set; }
        public string ProductName { get; set; }
        public string DeviceId { get; set; }
        public string ProjectId { get; set; }
        
        public delegate void StatusLockHandler(bool value);
        public static event StatusLockHandler OnStatusLockChanged;
        
        public SceneLockInterfacer(string url = "", string productName = null, string deviceId = null)
        {
            Url = string.IsNullOrWhiteSpace(url) ? string.IsNullOrWhiteSpace(EditorPrefs.GetString("SceneLockTool_URL")) ? "https://127.0.0.1" : EditorPrefs.GetString("SceneLockTool_URL") : url;
            ProductName = string.IsNullOrWhiteSpace(productName) ? Application.productName : productName;
            DeviceId = string.IsNullOrWhiteSpace(deviceId) ? GenerateDeviceId() : deviceId;
            GetProjectId((result, response) =>
            {
                if (result == WebRequestResult.Success)
                    ProjectId = TryParseProjectId(response, out string projectId) ? projectId : string.Empty;
                else
                    ProjectId = string.Empty;
            });
        }

        /// <summary>
        /// Sends a web request to check if the user associated with the current device is known.
        /// </summary>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The callback receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request. </returns>
        public RunningWebRequest GetUserKnown(Action<WebRequestResult, string> callback)
        {
            return CreateNewWebRequest(nameof(GetUserKnown), "Checking if user exists...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(GET_USER_KNOWN_KEY, DeviceId)
            }, callback);
        }

        /// <summary>
        /// Sends a web request to register a new user associated with the current device.
        /// </summary>
        /// <param name="username">The username of the new user to be registered. Must meet the required validation criteria before being sent.</param>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The callback receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request.</returns>
        public RunningWebRequest SetNewUser(string username, Action<WebRequestResult, string> callback)
        {
            return CreateNewWebRequest(nameof(SetNewUser), "Creating new user...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(SET_NEW_USER_KEY, DeviceId),
                new MultipartFormDataSection(NEW_USERNAME_KEY, username)
            }, callback);
        }

        /// <summary>
        /// Sends a web request to check if the project associated with the current device is known.
        /// </summary>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The call back receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request.</returns>
        public RunningWebRequest GetProjectKnown(Action<WebRequestResult, string> callback)
        {
            return CreateNewWebRequest(nameof(GetProjectKnown), "Checking if project exists...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(GET_PROJECT_KNOWN_KEY, ProductName)
            }, callback);
        }

        /// <summary>
        /// Sends a web request to register a new project associated with the current device.
        /// </summary>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The call back receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request.</returns>
        public RunningWebRequest SetNewProject(Action<WebRequestResult, string> callback)
        {
            return CreateNewWebRequest(nameof(SetNewProject), "Creating new project...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(SET_NEW_PROJECT_KEY, ProductName)
            }, callback);
        }

        /// <summary>
        /// Sends a web request to get the project id associated with the current project.
        /// </summary>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The call back receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request.</returns>
        public RunningWebRequest GetProjectId(Action<WebRequestResult, string> callback)
        {
            return CreateNewWebRequest(nameof(GetProjectId), "Getting project id...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(GET_PROJECT_ID_KEY, ProductName)
            }, (result, response) =>
            {
                if (result == WebRequestResult.Success && TryParseProjectId(response, out string projectId))
                    ProjectId = projectId;

                callback?.Invoke(result, response);
            });
        }

        /// <summary>
        /// Sends a web request to get the project scenes associated with the current project.
        /// </summary>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The call back receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <param name="projectId">The project id to get the scenes for. If not provided, the current project id is used.</param>
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request.</returns>
        public RunningWebRequest GetProjectScenes(Action<WebRequestResult, string> callback, string projectId = null)
        {
            return CreateNewWebRequest(nameof(GetProjectScenes), "Getting project scenes...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(GET_PROJECT_SCENES_KEY, projectId ?? ProjectId)
            }, callback);
        }

        /// <summary>
        /// Sends a web request to set the project scenes associated with the current project.
        /// </summary>
        /// <param name="scenes">An IEnumerable containing all the scenes to register with this project.</param>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The call back receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <param name="projectId">The project id to get the scenes for. If not provided, the current project id is used.</param>
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request.</returns>
        public RunningWebRequest SetProjectScenes(IEnumerable<string> scenes, Action<WebRequestResult, string> callback, string projectId = null)
        {
            string packedScenes = "scenes?" + string.Join("?", scenes.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()));
            return CreateNewWebRequest(nameof(SetProjectScenes), "Submitting project scenes...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(SET_PROJECT_SCENES_KEY, projectId ?? ProjectId),
                new MultipartFormDataSection(PROJECT_SCENE_NAMES_KEY, packedScenes)
            }, callback);
        }

        /// <summary>
        /// Gets the scene lock data for a specific scene. <br/>
        /// In most cases, you will want to parse the string response with <see cref="TryParseSceneLockData"/> to get a <see cref="SceneLockSceneObject"/> containing the data in a more structured way.
        /// </summary>
        /// <param name="sceneName">The scene to get the data for.</param>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The call back receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request.</returns>
        public RunningWebRequest GetSceneLockData(string sceneName, Action<WebRequestResult, string> callback)
        {
            return CreateNewWebRequest(nameof(GetSceneLockData) + sceneName, $"Gathering lock data for {sceneName}...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(GET_SCENE_LOCK_DATA_KEY, sceneName)
            }, callback);
        }

        /// <summary>
        /// Sets the scene lock data for a specific scene.
        /// </summary>
        /// <param name="sceneName">The scene to set the data for.</param>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The call back receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <param name="userDeviceId">The user's device id to use for the data. If not provided, it will use the default deviceId.</param>
        /// <param name="shouldSendEvent">Whether to send an event indicating that the scene lock was set.</param>
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request.</returns>
        public RunningWebRequest SetSceneLock(string sceneName, Action<WebRequestResult, string> callback, string userDeviceId = null, bool shouldSendEvent = true)
        {
            return CreateNewWebRequest(nameof(SetSceneLock) + sceneName, $"Creating scene lock for {sceneName}...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(SET_SCENE_LOCK_KEY, sceneName),
                new MultipartFormDataSection(USER_DEVICE_KEY, userDeviceId ?? DeviceId)
            }, (result, response) =>  
            {
                if (result == WebRequestResult.Success && shouldSendEvent)
                {
                    OnStatusLockChanged?.Invoke(true);
                }
                callback?.Invoke(result, response);
            });
        }

        /// <summary>
        /// Releases the scene lock data for a specific scene.
        /// </summary>
        /// <param name="sceneLockId">Scene to release the lock for.</param>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The call back receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <param name="shouldSendEvent">Whether to send an event indicating that the scene lock was released.</param>       
        /// <returns>A <see cref="RunningWebRequest"/> instance representing the ongoing web request.</returns>
        public RunningWebRequest ReleaseSceneLock(string sceneLockId, Action<WebRequestResult, string> callback, bool shouldSendEvent = true)
        {
            return CreateNewWebRequest(nameof(ReleaseSceneLock) + sceneLockId, $"Releasing scene lock {sceneLockId}...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(RELEASE_SCENE_LOCK_KEY, sceneLockId)
            }, (result, response) =>
            {
                if (result == WebRequestResult.Success && shouldSendEvent)
                {
                    OnStatusLockChanged?.Invoke(false);
                }
                callback?.Invoke(result, response);
            });
        }

        public RunningWebRequest GetUsername(string deviceId, Action<WebRequestResult, string> callback)
        {
            return CreateNewWebRequest(nameof(GetUsername) + deviceId, $"Getting username for {deviceId}...", new List<IMultipartFormSection>
            {
                new MultipartFormDataSection(GET_USERNAME_KEY, deviceId)
            }, callback);
        }

        /// <summary>
        /// Cancels all ongoing web requests managed by this interfacer.
        /// </summary>
        /// <remarks>
        /// This method ensures that all currently running web requests are terminated and their resources are cleaned up.
        /// It also clears the internal dictionary tracking the active requests.
        /// </remarks>
        public void CancelAllRequests()
        {
            foreach (var request in runningRequests.Values)
            {
                request.Cleanup();
            }
            runningRequests.Clear();
        }

        /// <summary>
        /// Creates a new <see cref="RunningWebRequest"/> with the specified parameters.
        /// </summary>
        /// <param name="key">The PHP POST key used for the specific request.</param>
        /// <param name="loadingText">The loading text to display to the user while this request is active.</param>
        /// <param name="form">The data to send with the POST request.</param>
        /// <param name="callback">A callback action that is invoked upon completion of the web request.
        /// The call back receives a <see cref="WebRequestResult"/> indicating the result of the request
        /// and a string response containing additional data if applicable.</param>
        /// <param name="onSuccess">A callback that gets called when the request returns a success code.</param>
        /// <param name="onFail">A callback that gets called when the request returns a failure code.</param>
        /// <param name="onNetwork">A callback that gets called when there is no network or server.</param>
        /// <param name="onNull">A callback that gets called when the result is unknown.</param>
        /// <returns></returns>
        private RunningWebRequest CreateNewWebRequest(
            string key,
            string loadingText,
            List<IMultipartFormSection> form,
            Action<WebRequestResult, string> callback,
            Action onSuccess = null,
            Action onFail = null,
            Action onNetwork = null,
            Action onNull = null)
        {
            if (runningRequests.TryGetValue(key, out RunningWebRequest existing))
            {
                if (!existing.IsDone)
                {
                    return existing;
                }

                runningRequests.Remove(key);
            }

            form.Add(new MultipartFormDataSection(GET_REPLY_KEY, key));
            form.Add(new MultipartFormDataSection(PRODUCT_NAME_KEY, ProductName));

            UnityWebRequest request = UnityWebRequest.Post(Url, form);
            request.SendWebRequest();

            RunningWebRequest runningRequest = new RunningWebRequest(
                request,
                callback,
                loadingText,
                onSuccess,
                onFail,
                onNetwork,
                onNull,
                () => runningRequests.Remove(key));

            runningRequests[key] = runningRequest;
            EditorApplication.update += runningRequest.Run;

            return runningRequest;
        }
        

        /// <summary>
        /// Attempts to parse the project ID from a raw response string.
        /// </summary>
        /// <param name="rawResponse">The raw response string from which the project ID is extracted.</param>
        /// <param name="projectId">When this method returns, contains the parsed project ID if the operation was successful;
        /// otherwise, it contains an empty string.</param>
        /// <returns><c>true</c> if the project ID was successfully parsed; otherwise, <c>false</c>.</returns>
        public static bool TryParseProjectId(string rawResponse, out string projectId)
        {
            projectId = string.Empty;
            if (!TryExtractPayload(rawResponse, out string payload) || !payload.StartsWith("result"))
            {
                return false;
            }

            projectId = payload["result".Length..];
            return !string.IsNullOrWhiteSpace(projectId);
        }

        /// <summary>
        /// Attempts to parse scene names from a raw server response string.
        /// </summary>
        /// <param name="rawResponse">The raw response string obtained from the server, expected to contain scene data.</param>
        /// <param name="sceneNames">An output list that will be populated with parsed scene names if parsing is successful.</param>
        /// <returns>A boolean value indicating whether the parsing operation was successful.</returns>
        public static bool TryParseSceneNames(string rawResponse, out List<string> sceneNames)
        {
            sceneNames = new List<string>();
            if (!TryExtractPayload(rawResponse, out string payload) || !payload.StartsWith("result"))
            {
                return false;
            }

            string[] split = payload.Split('?');
            if (split.Length <= 1)
            {
                return true;
            }

            for (int i = 1; i < split.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(split[i]))
                {
                    sceneNames.Add(split[i].Trim());
                }
            }

            return true;
        }

        /// <summary>
        /// Attempts to parse the raw response string into a <see cref="SceneLockSceneObject"/> instance.
        /// </summary>
        /// <param name="rawResponse">The raw response string containing scene lock data.</param>
        /// <param name="sceneLockObject">The output parameter that will store the parsed <see cref="SceneLockSceneObject"/> instance if parsing is successful.</param>
        /// <returns>True if the raw response is successfully parsed into a <see cref="SceneLockSceneObject"/>; otherwise, false.</returns>
        public static bool TryParseSceneLockData(string rawResponse, out SceneLockSceneObject sceneLockObject)
        {
            sceneLockObject = new SceneLockSceneObject();
            if (!TryExtractPayload(rawResponse, out string payload) || !payload.StartsWith("result"))
                return false;

            string[] parts = payload.Split('?');
            if (parts.Length < 4)
                return false;

            sceneLockObject.projectID = parts[1];
            sceneLockObject.projectName = parts[2];
            sceneLockObject.HasSceneLock = parts[3] == "1";

            if (!sceneLockObject.HasSceneLock)
                return true;

            if (parts.Length < 11)
                return false;

            sceneLockObject.SceneLock = new SceneLock(
                parts[4],
                parts[1],
                parts[6],
                parts[7],
                parts[8],
                parts[10],
                parts[9]);

            return true;
        }

        /// <summary>
        /// Attempts to extract a payload from the provided raw response string. The payload is the portion of the
        /// string that follows the first occurrence of a hyphen ('-'). The method will remove any newline or carriage
        /// return characters prior to extraction.
        /// </summary>
        /// <param name="rawResponse">The raw response string from which the payload will be extracted. The method
        /// expects the payload to follow a hyphen ('-') character within the rawResponse.</param>
        /// <param name="payload">An output parameter that contains the extracted payload if the method succeeds.
        /// If the method fails, the value will be an empty string.</param>
        /// <returns>A boolean value indicating whether the extraction was successful. Returns <c>true</c> if the
        /// payload was successfully extracted; otherwise, <c>false</c>.</returns>
        private static bool TryExtractPayload(string rawResponse, out string payload)
        {
            payload = string.Empty;

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                return false;
            }

            string compact = rawResponse.Replace("\n", string.Empty).Replace("\r", string.Empty);
            int splitIndex = compact.IndexOf('-');
            if (splitIndex < 0 || splitIndex == compact.Length - 1)
            {
                return false;
            }

            payload = compact.Substring(splitIndex + 1);
            return true;
        }

        /// <summary>
        /// Generates a unique device identifier by computing an MD5 hash based on the device's physical network address.
        /// If the device is identified as a ParrelSync clone, an additional "_clone" suffix is appended to the hash input.
        /// </summary>
        /// <returns>A string representing the hashed and formatted device identifier.</returns>
        private static string GenerateDeviceId()
        {
            string physicalAddress = string.Empty;
            NetworkInterface[] networkInterfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (NetworkInterface adapter in networkInterfaces)
            {
                physicalAddress = adapter.GetPhysicalAddress().ToString();
                if (!string.IsNullOrWhiteSpace(physicalAddress))
                    break;
            }
            
            byte[] stringBytes = Encoding.UTF8.GetBytes(physicalAddress);
            StringBuilder builder = new StringBuilder();
            using (MD5 md5 = MD5.Create())
            {
                byte[] hash = md5.ComputeHash(stringBytes);
                foreach (byte b in hash)
                    builder.Append(b.ToString("X2"));
            }

            return builder.ToString();
        }

        public string GetLoadingMessage()
        {
            return runningRequests.Values.Any() ? runningRequests.Values.First().LoadingText : string.Empty;
        }

        public static void InvokeOnStatusLockChanged(bool isLocked)
        {
            OnStatusLockChanged?.Invoke(isLocked);
        }

        public void Dispose()
        {
            CancelAllRequests();
            runningRequests.Clear();
        }
    }
}