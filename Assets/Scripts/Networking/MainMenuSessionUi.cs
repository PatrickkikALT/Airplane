using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Airplane.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Networking/Main Menu Session UI")]
    public sealed class MainMenuSessionUi : MonoBehaviour
    {
        [Header("Session")]
        [SerializeField] private int maxPlayers = GameLobby.DefaultMaxPlayers;

        [Header("Scene")]
        [Tooltip("Loaded when the host starts the flight. Must be in Build Settings.")]
        [SerializeField] private string gameScene = "Patrick";

        [Header("UI")]
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button startButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Toggle serverToggle;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private LobbyRow[] lobbyRows;

        [Serializable]
        public sealed class LobbyRow
        {
            public Button button;
            public TMP_Text label;
        }

        private readonly StringBuilder _builder = new StringBuilder(256);
        private Coroutine _poll;
        private string _fault;

        private void OnEnable()
        {
            Wire(true);
            GameLobby.Changed += Refresh;
            NetworkConnection.OnConnectFail += Refresh;
            Refresh();
            _poll = StartCoroutine(Poll());
        }

        private void OnDisable()
        {
            if (_poll != null)
                StopCoroutine(_poll);
            _poll = null;
            GameLobby.Changed -= Refresh;
            NetworkConnection.OnConnectFail -= Refresh;
            Wire(false);
        }

        public void StartHost()
        {
            if (GameLobby.Current != null)
            {
                if (GameLobby.Current.IsHost)
                    _ = StartFlight();
                return;
            }

            _ = CreateLobby();
        }

        public void StartServer()
        {
            if (serverToggle)
                serverToggle.SetIsOnWithoutNotify(true);
            StartHost();
        }

        public void StartClient()
        {
            _fault = null;
            if (GameLobby.Current != null)
            {
                _ = GameLobby.Leave();
                return;
            }

            string code = addressInput ? addressInput.text : "";
            if (string.IsNullOrWhiteSpace(code))
            {
                if (GameLobby.OpenSessions.Count == 0)
                {
                    NetworkConnection.Report("No open lobbies");
                    Refresh();
                    return;
                }

                _ = GameLobby.JoinId(GameLobby.OpenSessions[0].Id);
                return;
            }

            _ = GameLobby.JoinCode(code);
        }

        public void JoinListed(int index)
        {
            if (GameLobby.Current != null || GameLobby.Busy)
                return;
            if (index < 0 || index >= GameLobby.OpenSessions.Count)
                return;
            _ = GameLobby.JoinId(GameLobby.OpenSessions[index].Id);
        }

        public void Quit()
        {
            Application.Quit();
        }

        private async System.Threading.Tasks.Task CreateLobby()
        {
            _fault = null;
            bool isPrivate = serverToggle && serverToggle.isOn;
            string name = LocalPlayerIdentity.PilotName;
            await GameLobby.Create(name, maxPlayers, isPrivate);
        }

        private async System.Threading.Tasks.Task StartFlight()
        {
            if (!await GameLobby.Lock())
                return;
            LoadGame();
        }

        private void LoadGame()
        {
            if (string.IsNullOrWhiteSpace(gameScene))
                return;

            if (!Application.CanStreamedLevelBeLoaded(gameScene))
            {
                _fault = "Scene not in Build Settings: " + gameScene;
                Set(statusText, _fault);
                return;
            }

            NetworkManager manager = NetworkManager.Singleton;
            if (!manager || !manager.IsListening)
            {
                _fault = string.IsNullOrEmpty(NetworkConnection.Status) ? "Not connected" : NetworkConnection.Status;
                Set(statusText, _fault);
                return;
            }

            if (manager.IsServer && manager.NetworkConfig.EnableSceneManagement)
            {
                SceneEventProgressStatus status = manager.SceneManager.LoadScene(gameScene, LoadSceneMode.Single);
                if (status == SceneEventProgressStatus.Started)
                    return;

                _fault = "Could not load " + gameScene + " (" + status + ")";
                Set(statusText, _fault);
            }

            if (manager && manager.IsListening && !manager.IsServer)
                return;

            SceneManager.LoadScene(gameScene);
        }

        private IEnumerator Poll()
        {
            yield return null;
            yield return StartCoroutine(Wait(GameLobby.Refresh()));
            WaitForSeconds wait = new WaitForSeconds(4f);
            while (true)
            {
                yield return wait;
                if (GameLobby.Current == null && !GameLobby.Busy)
                    yield return StartCoroutine(Wait(GameLobby.Refresh()));
            }
        }

        private IEnumerator Wait(System.Threading.Tasks.Task task)
        {
            while (task != null && !task.IsCompleted)
                yield return null;
        }

        private void Refresh()
        {
            bool inLobby = GameLobby.Current != null;
            bool busy = GameLobby.Busy;
            bool host = inLobby && GameLobby.Current.IsHost;

            Shown(hostButton, !inLobby);
            Shown(startButton, host);
            Shown(joinButton, !inLobby);
            Shown(leaveButton, inLobby);
            Shown(addressInput, !inLobby);
            Shown(serverToggle, !inLobby);

            if (hostButton) hostButton.interactable = !busy;
            if (startButton) startButton.interactable = !busy;
            if (joinButton) joinButton.interactable = !busy;
            if (leaveButton) leaveButton.interactable = !busy;
            if (addressInput) addressInput.interactable = !busy;
            if (serverToggle) serverToggle.interactable = !busy;

            Set(statusText, BuildStatus());
            FillRows(inLobby || busy);
        }

        private string BuildStatus()
        {
            if (!string.IsNullOrEmpty(_fault) && GameLobby.Current == null && !GameLobby.Busy)
                return _fault;

            ISession session = GameLobby.Current;
            if (session != null)
            {
                _builder.Length = 0;
                _builder.Append(session.Name).Append("   ").Append(session.Code).Append('\n');
                if (session.Players != null)
                {
                    for (int i = 0; i < session.Players.Count; i++)
                    {
                        IReadOnlyPlayer player = session.Players[i];
                        _builder.Append(GameLobby.CallsignOf(player));
                        if (player != null && player.Id == session.Host)
                            _builder.Append("  ·  host");
                        _builder.Append('\n');
                    }
                }

                if (!session.IsHost)
                    _builder.Append("Waiting for the host");
                if (!string.IsNullOrEmpty(_fault))
                    _builder.Append('\n').Append(_fault);
                return _builder.ToString();
            }

            if (GameLobby.Busy)
                return "Contacting lobby…";

            IReadOnlyList<ISessionInfo> open = GameLobby.OpenSessions;
            if (open.Count == 0)
            {
                string status = NetworkConnection.Status;
                return string.IsNullOrEmpty(status) ? "No open lobbies" : status;
            }

            bool listedOnButtons = lobbyRows != null && lobbyRows.Length > 0;
            _builder.Length = 0;
            _builder.Append("Open lobbies  ·  ").Append(open.Count);
            if (listedOnButtons)
                return _builder.ToString();

            int shown = Mathf.Min(open.Count, 4);
            for (int i = 0; i < shown; i++)
            {
                ISessionInfo info = open[i];
                int players = info.MaxPlayers - info.AvailableSlots;
                _builder.Append('\n').Append(info.Name).Append("  ").Append(players).Append('/').Append(info.MaxPlayers);
            }

            if (string.IsNullOrWhiteSpace(addressInput ? addressInput.text : ""))
                _builder.Append("\nJOIN enters the first");
            return _builder.ToString();
        }

        private void FillRows(bool hide)
        {
            if (lobbyRows == null)
                return;

            for (int i = 0; i < lobbyRows.Length; i++)
            {
                LobbyRow row = lobbyRows[i];
                if (row == null || !row.button)
                    continue;

                bool show = !hide && i < GameLobby.OpenSessions.Count;
                if (row.button.gameObject.activeSelf != show)
                    row.button.gameObject.SetActive(show);
                if (!show)
                    continue;

                ISessionInfo info = GameLobby.OpenSessions[i];
                int players = info.MaxPlayers - info.AvailableSlots;
                TMP_Text label = row.label ? row.label : row.button.GetComponentInChildren<TMP_Text>();
                Set(label, info.Name + "   " + players + "/" + info.MaxPlayers);
                row.button.interactable = !GameLobby.Busy;
            }
        }

        private void Wire(bool subscribe)
        {
            if (subscribe)
            {
                if (hostButton) hostButton.onClick.AddListener(StartHost);
                if (startButton) startButton.onClick.AddListener(StartHost);
                if (joinButton) joinButton.onClick.AddListener(StartClient);
                if (leaveButton) leaveButton.onClick.AddListener(StartClient);
                if (lobbyRows != null)
                {
                    for (int i = 0; i < lobbyRows.Length; i++)
                    {
                        int index = i;
                        LobbyRow row = lobbyRows[i];
                        if (row != null && row.button)
                            row.button.onClick.AddListener(() => JoinListed(index));
                    }
                }

                return;
            }

            if (hostButton) hostButton.onClick.RemoveListener(StartHost);
            if (startButton) startButton.onClick.RemoveListener(StartHost);
            if (joinButton) joinButton.onClick.RemoveListener(StartClient);
            if (leaveButton) leaveButton.onClick.RemoveListener(StartClient);
            if (lobbyRows == null)
                return;

            for (int i = 0; i < lobbyRows.Length; i++)
            {
                LobbyRow row = lobbyRows[i];
                if (row != null && row.button)
                    row.button.onClick.RemoveAllListeners();
            }
        }

        private static void Shown(Component control, bool shown)
        {
            if (!control)
                return;
            GameObject target = control.gameObject;
            if (target.activeSelf != shown)
                target.SetActive(shown);
        }

        private static void Set(TMP_Text text, string value)
        {
            if (!text || text.text == value)
                return;
            text.text = value;
        }
    }
}
