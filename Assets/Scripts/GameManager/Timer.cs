using System.Collections;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class Timer : NetworkBehaviour
{

    public static Timer Instance;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private float timer;

    
    private void Awake()
    {
        if (!NetworkManager.Singleton.IsHost) return;
        Instance = this;
    }
    public void TurnTimerOn()
    {

        if (!NetworkManager.Singleton.IsHost) return;
        StartCoroutine(HandleTimer());
    }

    private IEnumerator HandleTimer()
    {
        while (timer > 0)
        {
            int minutes = Mathf.FloorToInt(timer / 60);
            int seconds = Mathf.FloorToInt(timer % 60);

            SetTimeToTextClientRpc(minutes, seconds);

            yield return new WaitForSeconds(1);
            timer--;

        }
    }

    [ClientRpc]
    private void SetTimeToTextClientRpc(int minutes, int seconds)
    {
        timerText.text = minutes + ":" + seconds.ToString("00");

    }
}
