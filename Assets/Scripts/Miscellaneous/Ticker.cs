using System.Collections;
using UnityEngine;

public class Ticker : MonoBehaviour
{

    public static Ticker Instance;
    public delegate void OnTick();
    public event OnTick onTick;

    [SerializeField] private float interval;

    private void Awake()
    {
        if (Instance == null) Instance = this;
    }

    private void Start()
    {
        StartCoroutine(Tick());
    }

    private IEnumerator Tick()
    {
        while (true)
        {
            yield return new WaitForSeconds(interval);
            onTick?.Invoke();
        }
    }


}