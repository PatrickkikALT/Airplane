using System.Text;
using Airplane.UI;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Airplane.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Networking/Network Session UI")]
    public sealed class NetworkSessionUi : MonoBehaviour
    {
        [Header("Auto Start")]
        [Tooltip("Create a lobby as soon as Play starts.")]
        [SerializeField] private bool autoStartHost;

        [Tooltip("Join a lobby as soon as Play starts. Ignored if Auto Start Host is set. Address is the join code.")]
        [SerializeField] private bool autoStartClient;

        [Tooltip("Lobby name when Auto Start Host is set. Join code when Auto Start Client is set.")]
        [SerializeField] private string address = "Local";

        [Header("Scene")]
        [Tooltip("Loaded after Disconnect. Leave empty to stay in this scene.")]
        [SerializeField] private string menuScene = "";

        [Header("Panels")]
        [SerializeField] private GameObject root;
        [SerializeField] private GameObject serverControls;

        [Header("Session")]
        [SerializeField] private TMP_Text roleText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text rosterText;
        [SerializeField] private Button disconnectButton;

        [Header("Server")]
        [SerializeField] private Button dummyButton;
        [SerializeField] private TMP_Text botCountText;
        [SerializeField] private Button botsDownButton;
        [SerializeField] private Button botsUpButton;

        private readonly StringBuilder _rosterBuilder = new StringBuilder(256);

        private NetworkManager Manager => NetworkManager.Singleton;

        private static AircraftNetworkSpawner Spawner => AircraftNetworkSpawner.Instance;

        private void OnEnable()
        {
            Wire(true);
        }

        private void OnDisable()
        {
            Wire(false);
        }

        private async void Start()
        {
            if (Manager && Manager.IsListening)
                return;

            if (autoStartHost)
            {
                string name = string.IsNullOrWhiteSpace(address) || address.IndexOf('.') >= 0 ? "Local" : address.Trim();
                await GameLobby.Create(name, GameLobby.DefaultMaxPlayers, false);
            }
            else if (autoStartClient && !string.IsNullOrWhiteSpace(address) && address.IndexOf('.') < 0)
                await GameLobby.JoinCode(address.Trim());
        }

        private void Update()
        {
            Refresh();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || !keyboard.f8Key.wasPressedThisFrame)
                return;
            if (CheatFlags.BlockPlayerInput)
                return;
            if (Manager == null || !Manager.IsListening || !Manager.IsServer)
                return;
            SpawnDummy();
        }

        public void Disconnect()
        {
            bool wasListening = Manager && Manager.IsListening;
            NetworkConnection.Disconnect();
            if (!wasListening || string.IsNullOrWhiteSpace(menuScene))
                return;
            if (!Application.CanStreamedLevelBeLoaded(menuScene))
                return;
            SceneManager.LoadScene(menuScene);
        }

        public void SpawnDummy()
        {
            if (Spawner != null)
                Spawner.SpawnDummy();
        }

        public void DecreaseBots()
        {
            AircraftNetworkSpawner spawner = Spawner;
            if (spawner != null)
                spawner.SetBotCount(spawner.DesiredBotCount - 1);
        }

        public void IncreaseBots()
        {
            AircraftNetworkSpawner spawner = Spawner;
            if (spawner != null)
                spawner.SetBotCount(spawner.DesiredBotCount + 1);
        }

        private void Refresh()
        {
            bool listening = Manager && Manager.IsListening;
            bool show = listening && HudVisibility.Visible && !CheatFlags.BlockPlayerInput;
            SetShown(root, show);
            if (!show)
            {
                if (!root || root == gameObject)
                    SetShown(serverControls, false);
                return;
            }

            bool server = Manager.IsServer;
            AircraftNetworkSpawner spawner = Spawner;
            SetShown(serverControls, server && spawner != null);

            Set(statusText, NetworkConnection.Status);
            Set(rosterText, BuildRoster());

            if (roleText)
            {
                string role = Manager.IsHost ? "Host" : Manager.IsServer ? "Server" : "Client";
                if (GameLobby.Current != null && !string.IsNullOrEmpty(GameLobby.Current.Code))
                    role += "  ·  " + GameLobby.Current.Code;
                Set(roleText, role + "  ·  client id " + Manager.LocalClientId);
            }

            if (server && botCountText && spawner != null)
                Set(botCountText, "Bots  " + spawner.LiveBotCount + "/" + spawner.DesiredBotCount);
        }

        private string BuildRoster()
        {
            if (!Manager || !Manager.IsListening)
                return "";

            _rosterBuilder.Length = 0;

            if (Manager.IsServer)
            {
                _rosterBuilder.Append("Pilots: ").Append(Manager.ConnectedClientsIds.Count).Append('\n');
                foreach (ulong id in Manager.ConnectedClientsIds)
                {
                    _rosterBuilder.Append(id == Manager.LocalClientId
                        ? "· " + LocalPlayerIdentity.PilotName + " (you)\n"
                        : "· client " + id + "\n");
                }
            }
            else
            {
                NetworkedAircraft local = NetworkedAircraft.Local;
                _rosterBuilder.Append(local ? "Flying as " + local.DisplayName : "Waiting for aircraft…");
            }

            return _rosterBuilder.ToString();
        }

        private void Wire(bool subscribe)
        {
            if (subscribe)
            {
                if (disconnectButton) disconnectButton.onClick.AddListener(Disconnect);
                if (dummyButton) dummyButton.onClick.AddListener(SpawnDummy);
                if (botsDownButton) botsDownButton.onClick.AddListener(DecreaseBots);
                if (botsUpButton) botsUpButton.onClick.AddListener(IncreaseBots);
                return;
            }

            if (disconnectButton) disconnectButton.onClick.RemoveListener(Disconnect);
            if (dummyButton) dummyButton.onClick.RemoveListener(SpawnDummy);
            if (botsDownButton) botsDownButton.onClick.RemoveListener(DecreaseBots);
            if (botsUpButton) botsUpButton.onClick.RemoveListener(IncreaseBots);
        }

        private void SetShown(GameObject target, bool shown)
        {
            if (!target || target == gameObject)
                return;
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
