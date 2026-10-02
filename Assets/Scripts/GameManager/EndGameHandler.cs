using Airplane.Multiplayer;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class EndGameHandler : NetworkBehaviour
{
    [SerializeField] private GameObject playerTag;
    [SerializeField] private Transform resultTagParent;
    private AircraftServerScoreSystem _aircraftServerScoreSystem;

    private void Awake()
    {
        _aircraftServerScoreSystem = GetComponent<AircraftServerScoreSystem>();
    }

    public void HandleResults()
    {
        HandlePlayerTag();
        StartCoroutine(ShowResults());
    }
    private void HandlePlayerTag()
    {
        Dictionary<NetworkedAircraft, int> playerResults = _aircraftServerScoreSystem.ReturnPlayerPoints();

        foreach (var points in playerResults)
        {
            NetworkedAircraft aircraft = points.Key;
            int pointValue = points.Value;
            ShowPointsClientRpc(aircraft.DisplayName, pointValue);
            
        }
    }

    [ClientRpc]
    private void ShowPointsClientRpc(string aircraftName, int pointValue)
    {
        Transform playerTagClone = Instantiate(playerTag, resultTagParent).transform;
        TMP_Text nameText = playerTagClone.GetChild(0).GetComponent<TMP_Text>();
        TMP_Text pointText = playerTagClone.GetChild(1).GetComponent<TMP_Text>();
        nameText.text = aircraftName;
        pointText.text = pointValue.ToString();
    }

    private IEnumerator ShowResults()
    {
        resultTagParent.gameObject.SetActive(true);
        yield return new WaitForSeconds(5);
        resultTagParent.gameObject.SetActive(false);
        EndGame();
    }

    private void EndGame()
    {
        GameManager.Instance.EndGame();
    }
}
