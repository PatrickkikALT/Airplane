using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class AircraftServerScoreSystem : NetworkBehaviour
{
    public static AircraftServerScoreSystem Instance;
    [SerializeField] private Dictionary<ulong, int> playerPoints = new Dictionary<ulong, int>();


    private void Awake()
    {
        if (!Instance)
        {
            Instance = this;
        }

    }

    public void StartGame()
    {

        if (!NetworkManager.Singleton.IsHost) return;

        foreach (ulong clientId in NetworkManager.Singleton.ConnectedClientsIds)
        {
            playerPoints.Add(clientId, 0);
        }
    }

    private void Update()
    {

        if (Input.GetKeyDown(KeyCode.I))
        {
            StartGame();
        }
        if (Input.GetKeyDown(KeyCode.T))
        {
            HandlePointServerRpc(NetworkManager.Singleton.LocalClientId);
        }
    }

    [ServerRpc]
    public void HandlePointServerRpc(ulong shooterPlayer)
    {
        playerPoints[shooterPlayer]++;
        Debug.LogError(shooterPlayer);
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(shooterPlayer, out NetworkClient client))
        {
            NetworkObject playerObject = client.OwnedObjects[2];

            if (playerObject.TryGetComponent(out AircraftClientScoreSystem scoreSystem))
            {
                scoreSystem.HandlePointClientRpc(shooterPlayer, playerPoints[shooterPlayer]);
            }
        }

    }

}
