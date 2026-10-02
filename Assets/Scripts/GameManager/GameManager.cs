using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.Weapons;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEditor.SearchService;
using UnityEngine;
using UnityEngine.Android;

public enum Direction
{
    Left = 0, Right = 1, Forward = 2, Back = 3,
}
public class GameManager : NetworkBehaviour
{

    public static GameManager Instance;

    private IReadOnlyList<NetworkedAircraft> _readOnlyAircrafts = new List<NetworkedAircraft>();
    private List<NetworkedAircraft> _aircrafts = new List<NetworkedAircraft>();

    [SerializeField] private FadeHandler fadeHandler;
    [SerializeField] private Transform fightArea;
    private Bounds _bounds;

    private bool _hasGameStarted;

    private AircraftServerScoreSystem _aircraftServerScoreSystem;
    private EndGameHandler _endGameHandler;
    private JoinHandler _joinHandler;

    [SerializeField] private GameObject startButton;

    private void Awake()
    {
        _bounds = fightArea.GetComponent<Collider>().bounds;

        if (!Instance)
        {
            Instance = this;
        }

        _aircraftServerScoreSystem = GetComponent<AircraftServerScoreSystem>();
        _endGameHandler = GetComponent<EndGameHandler>();
        _joinHandler = GetComponent<JoinHandler>();

    }

    public void StartGame()
    {
        Debug.Log("StartGame");
        CollectPlayers();
        //stops here, figure out why this is not running on the host
        StartSpawningPlayersClientRpc();
        StartGameJoinHandler();
    }

    private void StartGameJoinHandler()
    {
        _joinHandler?.StartGame();
    }

    private void CollectPlayers()
    {
        Debug.Log("CollectPlayers");
        if (!NetworkManager.Singleton.IsHost) return;
        Debug.Log("Player is the host, collecting aircrafts");
        _readOnlyAircrafts = NetworkedAircraft.All;

        for (int i = 0; i < _readOnlyAircrafts.Count; i++)
        {
            _aircrafts.Add(_readOnlyAircrafts[i]);
        }

    }
    [ClientRpc]
    private void StartSpawningPlayersClientRpc()
    {
        Debug.Log("StartSpawningPlayersClientRpc");
        StartCoroutine(SpawnPlayers());
    }
    private IEnumerator SpawnPlayers()
    {
        Debug.Log("SpawnPlayers");
        yield return StartCoroutine(fadeHandler.FadeIn());
        TeleportPlayers();
        yield return StartCoroutine(fadeHandler.FadeOut());
        BeginSession();
    }

    private IEnumerator FadingEnding()
    {
        yield return StartCoroutine(fadeHandler.FadeIn());
        yield return StartCoroutine(fadeHandler.FadeOut());
        EndSession();
    }

    private void BeginSession()
    {
        _hasGameStarted = true;
        _aircraftServerScoreSystem.StartGame();
        Timer.Instance.TurnTimerOn();
    }

    private void TeleportPlayers()
    {
        int randomSide = Random.Range(0, 4);
        Direction dir = (Direction)randomSide;
        Vector3 spawnPosition = ReturnSpawnPosition(dir);
        Quaternion spawnRotation = ReturnSpawnRotation(dir);

        PlaneRigidbody rb = NetworkedAircraft.Local.Body;
        if (rb)
        {
            rb.Teleport(spawnPosition, spawnRotation, Vector3.zero, Vector3.zero);
        }
    }

    private Vector3 ReturnSpawnPosition(Direction randomPosition)
    {

        Vector3 spawnPosition = Vector3.zero;
        switch (randomPosition)
        {
            case Direction.Left:
                spawnPosition = new Vector3(Random.Range(_bounds.min.x, _bounds.max.x), _bounds.center.y, _bounds.min.z);
                break;
            case Direction.Right:
                spawnPosition = new Vector3(Random.Range(_bounds.min.x, _bounds.max.x), _bounds.center.y, _bounds.max.z);
                break;
            case Direction.Forward:
                spawnPosition = new Vector3(_bounds.min.x, _bounds.center.y, Random.Range(_bounds.min.z, _bounds.max.z));
                break;
            case Direction.Back:
                spawnPosition = new Vector3(_bounds.max.x, _bounds.center.y, Random.Range(_bounds.min.z, _bounds.max.z));
                break;
            default:
                spawnPosition = Vector3.zero;
                break;
        }

        return spawnPosition;
    }

    private Quaternion ReturnSpawnRotation(Direction randomSide)
    {

        Vector3 direction = Vector3.zero;
        switch (randomSide)
        {
            case Direction.Left:
                direction = Vector3.left;
                break;
            case Direction.Right:
                direction = Vector3.right;
                break;
            case Direction.Forward:
                direction = Vector3.forward;
                break;
            case Direction.Back:
                direction = Vector3.back;
                break;

        }

        Quaternion rotation = Quaternion.LookRotation(direction);

        return rotation;
    }

    public void HandleResults()
    {
        _endGameHandler?.HandleResults();
    }

    public void EndGame()
    {
        StartCoroutine(FadingEnding());
    }

    private void EndSession()
    {
        _joinHandler?.EndGame();
        startButton?.SetActive(true);
    }

    private void EndGameJoinHandler()
    {
        _joinHandler?.EndGame();
    }


    




}
