using Airplane.Multiplayer;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using Unity.Netcode;

public class JoinHandler : NetworkBehaviour
{

    [SerializeField] private NetworkManager networkManager;

    [Header("UI Handling")]
    [SerializeField] private TMP_Text playerText;
    [SerializeField] private GameObject startButton;
    private int _playerCount;
    private void Start()
    {
        networkManager.OnClientConnectedCallback += AddPlayerCount;
        networkManager.OnClientConnectedCallback += CheckIfGameStart;
        networkManager.OnClientDisconnectCallback += RemovePlayerCount;

    }

    

    private void AddPlayerCount(ulong playerID)
    {
        if (!NetworkManager.Singleton.IsHost) return;
        _playerCount++;
        print("A new player has joined the party " + _playerCount);
    }

    private void RemovePlayerCount(ulong playerID)
    {
        if (!NetworkManager.Singleton.IsHost)
        {
            _playerCount--;

        }
    }
    private void CheckIfGameStart(ulong playerID)
    {

        print("Yes the client has joined.");

        if (!NetworkManager.Singleton.IsHost) return;
        if (_playerCount >= 1)
        {
            startButton.SetActive(true);
            ShowPlayerTextClientRpc($"There are {_playerCount} players in this server.");       
        }

        else
        {
            startButton.SetActive(false);
            ShowPlayerTextClientRpc("Waiting for players to join...");
        }


    }

    [ClientRpc]
    private void ShowPlayerTextClientRpc(string text)
    {
        playerText.text = text;
    }



}
