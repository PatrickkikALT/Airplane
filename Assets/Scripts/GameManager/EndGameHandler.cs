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
        Dictionary<ulong, int> playerResults = _aircraftServerScoreSystem.ReturnPlayerPoints();

        foreach (var points in playerResults)
        {
            int pointValue = points.Value;
            ShowPointsClientRpc(pointValue);
            
        }
    }

    [ClientRpc]
    private void ShowPointsClientRpc(int pointValue)
    {
        Transform playerTagClone = Instantiate(playerTag, resultTagParent).transform;
        TMP_Text pointText = playerTagClone.GetChild(1).GetComponent<TMP_Text>();
        pointText.text = pointValue.ToString();
    }

    private IEnumerator ShowResults()
    {
        resultTagParent.gameObject.SetActive(true);
        yield return new WaitForSeconds(5);
        resultTagParent.gameObject.SetActive(false);
    }
}
