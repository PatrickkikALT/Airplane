using Airplane.Multiplayer;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class AircraftServerScoreSystem : NetworkBehaviour
{
    public static AircraftServerScoreSystem Instance;
    private Dictionary<NetworkedAircraft, int> playerPoints = new Dictionary<NetworkedAircraft, int>();


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

        foreach (NetworkedAircraft networkedAircraft in NetworkedAircraft.All)
        {
            playerPoints.Add(networkedAircraft, 0);
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
            HandlePointServerRpc(NetworkedAircraft.Local.OwnerClientId, NetworkedAircraft.Local.DisplayName);
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void HandlePointServerRpc(ulong shooterPlayerID, string shooterPlayerName)
    {
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(shooterPlayerID, out NetworkClient client))
        {
            NetworkObject playerObject = client.OwnedObjects[0];

            foreach (NetworkObject networkObject in client.OwnedObjects)
            {
                if (networkObject.TryGetComponent(out PlayerInput playerInput))
                {
                    playerObject = networkObject;
                    break;
                } 
            }

            if (playerObject.TryGetComponent(out NetworkedAircraft aircraft))
            {
                playerPoints[aircraft]++;
            }

            if (playerObject.TryGetComponent(out AircraftClientScoreSystem scoreSystem))
            {
                scoreSystem.HandlePointClientRpc(shooterPlayerID, playerPoints[aircraft]);
            }
        }

    }

    public Dictionary<NetworkedAircraft, int> ReturnPlayerPoints()
    {
        return playerPoints;
    }

}
