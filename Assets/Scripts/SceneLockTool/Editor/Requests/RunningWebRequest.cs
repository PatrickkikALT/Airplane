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

		public void Run()
		{
			if (isCleaned)
				return;

			if (IsExpired())
			{
				IsDone = true;
				onComplete?.Invoke();
				Cleanup();
				callback?.Invoke(WebRequestResult.Unknown, "unknown");
				return;
			}

			if (!TryGetCompleted(out bool done, out UnityWebRequest.Result requestResult, out string body))
			{
				IsDone = true;
				onComplete?.Invoke();
				Cleanup();
				callback?.Invoke(WebRequestResult.Unknown, "unknown");
				return;
			}

			if (!done)
				return;

			if (requestResult is UnityWebRequest.Result.ConnectionError or UnityWebRequest.Result.ProtocolError)
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
				body ??= string.Empty;
				body = body.Replace("\n", "");
				body = body.Replace("\r", "");
				Result = body;
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
				UnityWebRequest request = ConnectedWebRequest;
				ConnectedWebRequest = null;
				if (request == null)
					return;

				try
				{
					request.Abort();
					request.Dispose();
				}
				catch (Exception)
				{
				}
			}
		}

		private bool TryGetCompleted(out bool done, out UnityWebRequest.Result requestResult, out string body)
		{
			done = false;
			requestResult = UnityWebRequest.Result.InProgress;
			body = string.Empty;

			UnityWebRequest request = ConnectedWebRequest;
			if (request == null)
				return false;

			try
			{
				done = request.isDone;
				if (!done)
					return true;

				requestResult = request.result;
				body = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
				return true;
			}
			catch (NullReferenceException)
			{
				return false;
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