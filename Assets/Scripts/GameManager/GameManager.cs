using Airplane.Multiplayer;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class GameManager : MonoBehaviour
{

    private IReadOnlyList<NetworkedAircraft> _readOnlyAircrafts = new List<NetworkedAircraft>();
    private List<NetworkedAircraft> _aircrafts = new List<NetworkedAircraft>();

    [SerializeField] private FadeHandler fadeHandler;



    public void StartGame()
    {
        CollectPlayers();
        StartCoroutine(SpawnPlayers());
    }

    private void CollectPlayers()
    {
        if (!NetworkManager.Singleton.IsHost) return;

        _readOnlyAircrafts = NetworkedAircraft.All;

        for (int i = 0; i < _readOnlyAircrafts.Count; i++)
        {
            _aircrafts.Add(_readOnlyAircrafts[i]);
        }

    }

    private IEnumerator SpawnPlayers()
    {
        yield return StartCoroutine(fadeHandler.FadeIn());
        TeleportPlayers();
        yield return StartCoroutine(fadeHandler.FadeOut());
    }

    private void TeleportPlayers()
    {

    }




}
