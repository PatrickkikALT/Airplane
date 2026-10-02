using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class Timer : NetworkBehaviour
{
    public static Timer Instance;

    [SerializeField] private TMP_Text timerText;
    [SerializeField] private float timer = 180f;

    private Coroutine _routine;
    private float _remaining;

    private void Awake()
    {
        if (Instance == null)
            Instance = this;

        Publish(0, 0);
    }

    public void BeginRound()
    {
        if (!IsServer)
            return;

        StopRound();
        _remaining = Mathf.Max(1f, timer);
        _routine = StartCoroutine(HandleTimer());
    }

    public void StopRound()
    {
        if (_routine == null)
            return;

        StopCoroutine(_routine);
        _routine = null;
    }

    private IEnumerator HandleTimer()
    {
        while (_remaining > 0f)
        {
            int total = Mathf.CeilToInt(_remaining);
            PublishTimeRpc(total / 60, total % 60);
            yield return new WaitForSeconds(1f);
            _remaining -= 1f;
        }

        PublishTimeRpc(0, 0);
        _routine = null;
        GameManager.Instance.FinishRound();
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void PublishTimeRpc(int minutes, int seconds)
    {
        Publish(minutes, seconds);
    }

    private void Publish(int minutes, int seconds)
    {
        if (timerText)
            timerText.text = minutes + ":" + seconds.ToString("00");
    }
}
