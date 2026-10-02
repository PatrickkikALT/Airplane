using System.Collections;
using System.Collections.Generic;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class EndGameHandler : NetworkBehaviour
{
    [SerializeField] private Transform resultTagParent;
    [SerializeField] private TMP_Text resultsTitle;
    [SerializeField] private float resultsHoldSeconds = 6f;

    private Coroutine _hold;

    private void Awake()
    {
        Hide();
    }

    public void Present(List<RoundStanding> standings)
    {
        if (!IsServer)
            return;

        ShowResultsRpc(standings.Count);
        for (int i = 0; i < standings.Count; i++)
            SetResultRowRpc(i, standings[i].Name, standings[i].Points);
        if (_hold != null)
            StopCoroutine(_hold);
        _hold = StartCoroutine(HoldResults());
    }

    public void Hide()
    {
        ClearRows();
        if (resultTagParent)
            resultTagParent.gameObject.SetActive(false);
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void ShowResultsRpc(int count)
    {
        if (resultsTitle)
            resultsTitle.text = "Round over";

        if (!resultTagParent)
            return;

        resultTagParent.gameObject.SetActive(true);
        int shown = Mathf.Min(count, resultTagParent.childCount);
        for (int i = 0; i < resultTagParent.childCount; i++)
            resultTagParent.GetChild(i).gameObject.SetActive(i < shown);
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void SetResultRowRpc(int index, string pilotName, int points)
    {
        if (!resultTagParent || index < 0 || index >= resultTagParent.childCount)
            return;

        Transform row = resultTagParent.GetChild(index);
        row.gameObject.SetActive(true);
        if (row.childCount < 2)
            return;

        TMP_Text nameText = row.GetChild(0).GetComponent<TMP_Text>();
        TMP_Text pointText = row.GetChild(1).GetComponent<TMP_Text>();
        if (nameText)
            nameText.text = pilotName;
        if (pointText)
            pointText.text = points.ToString();
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void HideResultsRpc()
    {
        Hide();
    }

    private IEnumerator HoldResults()
    {
        yield return new WaitForSeconds(resultsHoldSeconds);
        _hold = null;
        HideResultsRpc();
        GameManager.Instance.CompleteRound();
    }

    private void ClearRows()
    {
        if (!resultTagParent)
            return;

        for (int i = 0; i < resultTagParent.childCount; i++)
            resultTagParent.GetChild(i).gameObject.SetActive(false);
    }
}
