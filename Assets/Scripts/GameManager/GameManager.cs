using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public enum Direction
{
    Left = 0, Right = 1, Forward = 2, Back = 3,
}

public class GameManager : NetworkBehaviour
{
    public static GameManager Instance;

    [SerializeField] private FadeHandler fadeHandler;
    [SerializeField] private Transform fightArea;
    [SerializeField] private float spawnAltitude = 970f;
    [SerializeField] private GameObject startButton;

    private Bounds _bounds;
    private bool _roundRunning;
    private bool _endingRound;

    private AircraftServerScoreSystem _aircraftServerScoreSystem;
    private EndGameHandler _endGameHandler;
    private JoinHandler _joinHandler;

    private void Awake()
    {
        if (fightArea)
            _bounds = fightArea.GetComponent<Collider>().bounds;

        if (!Instance)
            Instance = this;

        _aircraftServerScoreSystem = GetComponent<AircraftServerScoreSystem>();
        _endGameHandler = GetComponent<EndGameHandler>();
        _joinHandler = GetComponent<JoinHandler>();
    }

    public void StartGame()
    {
        if (!IsSpawned || !IsServer || _roundRunning)
            return;
        if (!fightArea)
        {
            Debug.LogError("GameManager needs a Fight Area with a collider.", this);
            return;
        }

        _bounds = fightArea.GetComponent<Collider>().bounds;
        _roundRunning = true;
        _endingRound = false;
        _joinHandler?.NotifyRoundStarted();
        if (startButton)
            startButton.SetActive(false);

        StartRoundRpc(Random.Range(0, 4));
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void StartRoundRpc(int edge)
    {
        StartCoroutine(SpawnPlayers((Direction)edge));
    }

    private IEnumerator SpawnPlayers(Direction edge)
    {
        if (fightArea)
            _bounds = fightArea.GetComponent<Collider>().bounds;

        if (fadeHandler)
            yield return StartCoroutine(fadeHandler.FadeIn());

        TeleportPlayers(edge);

        if (fadeHandler)
            yield return StartCoroutine(fadeHandler.FadeOut());

        BeginSession();
    }

    private void BeginSession()
    {
        RoundScoreHud.Instance?.SetScore(0);
        if (!IsServer)
            return;

        _aircraftServerScoreSystem.BeginRound();
        Timer.Instance.BeginRound();
    }

    private void TeleportPlayers(Direction edge)
    {
        Vector3 spawnPosition = ReturnSpawnPosition(edge);
        Vector3 inward = _bounds.center - spawnPosition;
        inward.y = 0f;
        if (inward.sqrMagnitude < 0.01f)
            inward = Vector3.forward;

        PlaneRigidbody body = NetworkedAircraft.Local ? NetworkedAircraft.Local.Body : null;
        if (body)
            body.Teleport(spawnPosition, Quaternion.LookRotation(inward), Vector3.zero, Vector3.zero);
    }

    private Vector3 ReturnSpawnPosition(Direction edge)
    {
        float insetX = Mathf.Min(400f, _bounds.extents.x * 0.15f);
        float insetZ = Mathf.Min(400f, _bounds.extents.z * 0.15f);
        float x = Random.Range(_bounds.min.x + insetX, _bounds.max.x - insetX);
        float z = Random.Range(_bounds.min.z + insetZ, _bounds.max.z - insetZ);

        switch (edge)
        {
            case Direction.Left:
                z = _bounds.min.z + insetZ;
                break;
            case Direction.Right:
                z = _bounds.max.z - insetZ;
                break;
            case Direction.Forward:
                x = _bounds.min.x + insetX;
                break;
            case Direction.Back:
                x = _bounds.max.x - insetX;
                break;
        }

        return new Vector3(x, spawnAltitude, z);
    }

    public void FinishRound()
    {
        if (!IsServer || !_roundRunning || _endingRound)
            return;

        _endingRound = true;
        Timer.Instance?.StopRound();
        _aircraftServerScoreSystem.EndRound();
        _endGameHandler.Present(_aircraftServerScoreSystem.CopyStandings());
    }

    public void CompleteRound()
    {
        if (!IsServer || !_roundRunning)
            return;

        EndRoundRpc();
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void EndRoundRpc()
    {
        StartCoroutine(FadeOutOfRound());
    }

    private IEnumerator FadeOutOfRound()
    {
        _endGameHandler?.Hide();
        if (fadeHandler)
            yield return StartCoroutine(fadeHandler.FadeIn());
        if (fadeHandler)
            yield return StartCoroutine(fadeHandler.FadeOut());

        if (!IsServer)
            yield break;

        _roundRunning = false;
        _endingRound = false;
        _joinHandler?.NotifyRoundEnded();
    }
}
