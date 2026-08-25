using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Airplane.Multiplayer
{
    /// <summary>
    /// Minimal direct-IP session front end: host, join or run a dedicated server, then show who is
    /// connected. Deliberately IMGUI so it matches the existing debug HUD and needs no scene canvas.
    /// Swap in Relay later by replacing <see cref="ApplyConnectionData"/>.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Networking/Network Session UI")]
    public sealed class NetworkSessionUi : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        [Tooltip("Address the server binds to. 0.0.0.0 accepts connections on every interface.")]
        [SerializeField] private string serverBindAddress = "0.0.0.0";

        [Header("Auto Start")]
        [Tooltip("Start a host immediately on Play. Handy while iterating with ParrelSync clones.")]
        [SerializeField] private bool autoStartHost;

        [Tooltip("Start as a client immediately on Play. Ignored if Auto Start Host is set.")]
        [SerializeField] private bool autoStartClient;

        [Header("Layout")]
        [SerializeField] private Vector2 panelPosition = new Vector2(16f, 240f);
        [SerializeField] private float panelWidth = 320f;

        private readonly StringBuilder _rosterBuilder = new StringBuilder(256);
        private string _status = "Offline";
        private bool _subscribed;

        private NetworkManager Manager => NetworkManager.Singleton;

        // Subscribed from Start rather than OnEnable: this component usually shares a GameObject with
        // the NetworkManager, and NetworkManager.Singleton is not assigned until its own Awake.
        private void Start()
        {
            if (Manager != null)
            {
                Manager.OnClientConnectedCallback += HandleClientConnected;
                Manager.OnClientDisconnectCallback += HandleClientDisconnected;
                _subscribed = true;
            }

            if (autoStartHost)
                StartHost();
            else if (autoStartClient)
                StartClient();
        }

        private void OnDestroy()
        {
            if (!_subscribed || Manager == null)
                return;
            Manager.OnClientConnectedCallback -= HandleClientConnected;
            Manager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _subscribed = false;
        }

        public void StartHost()
        {
            if (!ApplyConnectionData(true))
                return;
            _status = Manager.StartHost() ? $"Hosting on {port}" : "Failed to start host";
        }

        public void StartServer()
        {
            if (!ApplyConnectionData(true))
                return;
            _status = Manager.StartServer() ? $"Server on {port}" : "Failed to start server";
        }

        public void StartClient()
        {
            if (!ApplyConnectionData(false))
                return;
            _status = Manager.StartClient() ? $"Connecting to {address}:{port}" : "Failed to start client";
        }

        public void Disconnect()
        {
            if (Manager == null || !Manager.IsListening)
                return;
            Manager.Shutdown();
            _status = "Offline";
        }

        private bool ApplyConnectionData(bool listening)
        {
            if (Manager == null)
            {
                _status = "No NetworkManager in the scene";
                return false;
            }

            UnityTransport transport = Manager.GetComponent<UnityTransport>();
            if (transport == null)
            {
                _status = "NetworkManager has no UnityTransport";
                return false;
            }

            string bind = listening && !string.IsNullOrWhiteSpace(serverBindAddress)
                ? serverBindAddress
                : null;

            if (bind != null)
                transport.SetConnectionData(address, port, bind);
            else
                transport.SetConnectionData(address, port);

            return true;
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (Manager != null && clientId == Manager.LocalClientId)
                _status = Manager.IsHost ? $"Hosting on {port}" : $"Connected to {address}:{port}";
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (Manager == null || clientId != Manager.LocalClientId)
                return;

            string reason = Manager.DisconnectReason;
            _status = string.IsNullOrEmpty(reason) ? "Disconnected" : $"Disconnected: {reason}";
        }

        private void OnGUI()
        {
            float x = panelPosition.x;
            float y = panelPosition.y;
            bool listening = Manager != null && Manager.IsListening;
            float height = listening ? 150f : 168f;

            GUI.Box(new Rect(x, y, panelWidth, height), "Multiplayer");
            float row = y + 24f;
            float inner = panelWidth - 20f;

            if (!listening)
            {
                GUI.Label(new Rect(x + 10f, row, 60f, 20f), "Address");
                address = GUI.TextField(new Rect(x + 74f, row, inner - 130f, 20f), address);
                GUI.Label(new Rect(x + inner - 50f, row, 30f, 20f), "Port");
                string portText = GUI.TextField(new Rect(x + inner - 18f, row, 44f, 20f), port.ToString());
                if (ushort.TryParse(portText, out ushort parsed))
                    port = parsed;
                row += 26f;

                if (GUI.Button(new Rect(x + 10f, row, inner / 3f - 4f, 24f), "Host"))
                    StartHost();
                if (GUI.Button(new Rect(x + 10f + inner / 3f, row, inner / 3f - 4f, 24f), "Join"))
                    StartClient();
                if (GUI.Button(new Rect(x + 10f + 2f * inner / 3f, row, inner / 3f - 4f, 24f), "Server"))
                    StartServer();
                row += 30f;
            }
            else
            {
                string role = Manager.IsHost ? "Host" : Manager.IsServer ? "Server" : "Client";
                GUI.Label(new Rect(x + 10f, row, inner, 20f), $"{role}  ·  client id {Manager.LocalClientId}");
                row += 24f;

                if (GUI.Button(new Rect(x + 10f, row, inner, 24f), "Disconnect"))
                    Disconnect();
                row += 30f;
            }

            GUI.Label(new Rect(x + 10f, row, inner, 20f), _status);
            row += 22f;
            GUI.Label(new Rect(x + 10f, row, inner, 60f), BuildRoster());
        }

        private string BuildRoster()
        {
            if (Manager == null || !Manager.IsListening)
                return "";

            _rosterBuilder.Length = 0;

            if (Manager.IsServer)
            {
                _rosterBuilder.Append("Pilots: ").Append(Manager.ConnectedClientsIds.Count).Append('\n');
                foreach (ulong id in Manager.ConnectedClientsIds)
                    _rosterBuilder.Append(id == Manager.LocalClientId ? "· you\n" : $"· client {id}\n");
            }
            else
            {
                NetworkedAircraft local = NetworkedAircraft.Local;
                _rosterBuilder.Append(local ? $"Flying as {local.DisplayName}" : "Waiting for aircraft…");
            }

            return _rosterBuilder.ToString();
        }
    }
}
