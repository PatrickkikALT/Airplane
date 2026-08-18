using UnityEditor;
using System;
using UnityEngine.Networking;
using UnityEngine;

namespace Utils.Core.SceneLockTool
{
	public class RunningWebRequest
	{
		public string LoadingText { get; private set; }
		public UnityWebRequest ConnectedWebRequest { get; private set; }
		public bool IsDone { get; private set; }
		public string Result { get; private set; }

		private Action<WebRequestResult, string> callback;
		private Action onSuccess;
		private Action onFail;
		private Action onNetwork;
		private Action onNull;
		private Action onComplete;
		private float timeOfConception;
		private float timeOutTime = 30f;
		private bool isCleaned;

		public RunningWebRequest(UnityWebRequest connectedWebRequest, Action<WebRequestResult, string> callback, string loadingText = "Loading...", Action onSuccess = null, Action onFail = null, Action onNetwork = null, Action onNull = null, Action onComplete = null)
		{
			LoadingText = loadingText;
			ConnectedWebRequest = connectedWebRequest;
			this.callback = callback;
			IsDone = false;
			timeOfConception = Time.realtimeSinceStartup;
			this.onSuccess = onSuccess;
			this.onFail = onFail;
			this.onNetwork = onNetwork;
			this.onNull = onNull;
			this.onComplete = onComplete;
		}

		~RunningWebRequest()
		{
			Cleanup();
		}

		public void Run()
		{
			if (IsExpired())
			{
				IsDone = true;
				onComplete?.Invoke();
				Cleanup();
				callback?.Invoke(WebRequestResult.Unknown, "unknown");
				return;
			}

			if (!ConnectedWebRequest.isDone)
				return;

			if (ConnectedWebRequest.result is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError)
			{
				Result = "network";
				IsDone = true;
				onComplete?.Invoke();
				Cleanup();
				onNetwork?.Invoke();
				callback?.Invoke(WebRequestResult.NoInternet, "Could not connect to server. Please check your internet connection and try again.");
			}
			else
			{
				string result = ConnectedWebRequest.downloadHandler.text;

				result = result.Replace("\n", "");
				result = result.Replace("\r", "");
				Result = result;
				IsDone = true;
				onComplete?.Invoke();
				Cleanup();

				if (Result.Contains("success"))
				{
					callback?.Invoke(WebRequestResult.Success, Result);
					onSuccess?.Invoke();
				}
				else if (Result.Contains("failure"))
				{
					callback?.Invoke(WebRequestResult.Failed, Result);
					onFail?.Invoke();
				}
				else if (Result.Contains("error"))
				{
					Result = "Encountered error: " + Result;
					callback?.Invoke(WebRequestResult.Failed, Result);
					onFail?.Invoke();
				}
				else
				{
					onNull?.Invoke();
					callback?.Invoke(WebRequestResult.Null, Result);
				}
			}
		}

		public void Cleanup()
		{
			if (!isCleaned)
			{
				isCleaned = true;
				EditorApplication.update -= Run;
				if (ConnectedWebRequest != null)
				{
					ConnectedWebRequest.Abort();
					ConnectedWebRequest.Dispose();
				}
			}
		}

		public string GetResult()
		{
			return Result;
		}

		public bool IsExpired()
		{
			return timeOfConception + timeOutTime < Time.realtimeSinceStartup;
		}

		public float GetRemainingTime()
		{
			return Mathf.Abs((timeOfConception - Time.realtimeSinceStartup));
		}
	}
}