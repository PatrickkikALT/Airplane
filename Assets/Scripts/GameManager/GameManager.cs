using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using Airplane.Weapons;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Android;

public enum Direction
{
    Left = 0, Right = 1, Forward = 2, Back = 3,
}
public class GameManager : NetworkBehaviour
{

    private IReadOnlyList<NetworkedAircraft> _readOnlyAircrafts = new List<NetworkedAircraft>();
    private List<NetworkedAircraft> _aircrafts = new List<NetworkedAircraft>();

    [SerializeField] private FadeHandler fadeHandler;
    [SerializeField] private Transform fightArea;
    private Bounds _bounds;

    private bool _hasGameStarted;

    private void Awake()
    {
        _bounds = fightArea.GetComponent<Collider>().bounds;

    }
    public void StartGame()
    {
        CollectPlayers();
        StartSpawningPlayersClientRpc();
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
    [ClientRpc]
    private void StartSpawningPlayersClientRpc()
    {
        StartCoroutine(SpawnPlayers());
    }
    private IEnumerator SpawnPlayers()
    {
        yield return StartCoroutine(fadeHandler.FadeIn());
        TeleportPlayers();
        yield return StartCoroutine(fadeHandler.FadeOut());
        BeginSession();
    }

    private void BeginSession()
    {
        _hasGameStarted = true;
        AircraftServerScoreSystem.Instance.StartGame();
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

    public void EndGame()
    {

    }


    




}
