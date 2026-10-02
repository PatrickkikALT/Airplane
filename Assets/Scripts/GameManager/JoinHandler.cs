using TMPro;
using Unity.Netcode;
using UnityEngine;

public class JoinHandler : NetworkBehaviour
{
    [SerializeField] private TMP_Text playerText;
    [SerializeField] private GameObject startButton;

    private readonly NetworkVariable<int> _connectedCount = new NetworkVariable<int>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _canStart = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> _inRound = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private bool _roundActive;

    private void Start()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager)
            return;

        if (manager.IsListening)
            TrySpawn();
        else
            manager.OnServerStarted += TrySpawn;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton)
            NetworkManager.Singleton.OnServerStarted -= TrySpawn;
    }

    private void TrySpawn()
    {
        if (NetworkManager.Singleton)
            NetworkManager.Singleton.OnServerStarted -= TrySpawn;

        if (IsSpawned || !NetworkManager.Singleton || !NetworkManager.Singleton.IsServer)
            return;

        NetworkObject.Spawn();
    }

    public override void OnNetworkSpawn()
    {
        _connectedCount.OnValueChanged += OnCountChanged;
        _canStart.OnValueChanged += OnFlagChanged;
        _inRound.OnValueChanged += OnFlagChanged;
        ApplyLobby();

        if (!IsServer)
            return;

        NetworkManager.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.OnClientDisconnectCallback += OnClientDisconnected;
        RefreshLobby();
    }

    public override void OnNetworkDespawn()
    {
        _connectedCount.OnValueChanged -= OnCountChanged;
        _canStart.OnValueChanged -= OnFlagChanged;
        _inRound.OnValueChanged -= OnFlagChanged;

        if (NetworkManager == null)
            return;

        NetworkManager.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    public void NotifyRoundStarted()
    {
        if (!IsServer)
            return;

        _roundActive = true;
        RefreshLobby();
    }

    public void NotifyRoundEnded()
    {
        if (!IsServer)
            return;

        _roundActive = false;
        RefreshLobby();
    }

    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer)
            return;

        if (_roundActive && clientId != NetworkManager.LocalClientId)
        {
            NetworkManager.DisconnectClient(clientId);
            return;
        }

        RefreshLobby();
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer)
            return;

        RefreshLobby();
    }

    private void OnCountChanged(int previous, int current)
    {
        ApplyLobby();
    }

    private void OnFlagChanged(bool previous, bool current)
    {
        ApplyLobby();
    }

    private void RefreshLobby()
    {
        int count = NetworkManager.ConnectedClientsIds.Count;
        if (IsServer && count < 1)
            count = 1;

        _connectedCount.Value = count;
        _inRound.Value = _roundActive;
        _canStart.Value = !_roundActive && count >= 1;
        ApplyLobby();
    }

    private void ApplyLobby()
    {
        int playerCount = _connectedCount.Value;
        bool roundActive = _inRound.Value;

        if (playerText)
        {
            if (roundActive)
                playerText.text = playerCount + " in this round";
            else if (playerCount <= 0)
                playerText.text = "Waiting for players...";
            else if (playerCount == 1)
                playerText.text = "1 pilot in the lobby";
            else
                playerText.text = playerCount + " pilots in the lobby";
        }

        bool server = NetworkManager.Singleton && NetworkManager.Singleton.IsServer;
        if (startButton)
            startButton.SetActive(server && _canStart.Value);
    }
}
