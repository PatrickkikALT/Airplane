using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public class NumberRanges
{
    public float minX;
    public float maxX;
    public float minY;
    public float maxY;
    public float minZ;
    public float maxZ;

    public NumberRanges(float minX, float maxX, float minY, float maxY, float minZ, float maxZ)
    {
        this.minX = minX;
        this.maxX = maxX;
        this.minY = minY;
        this.maxY = maxY;
        this.minZ = minZ;
        this.maxZ = maxZ;
    }
}

public class MapGenerator : MonoBehaviour
{
    [Header("Positions")]
    [SerializeField] private Vector3 minSpawnPosition;
    [SerializeField] private Vector3 maxSpawnPosition;
    [SerializeField] private Transform spawnArea;
    private Bounds _spawnAreaCollider;

    [Header("Amounts")]
    [SerializeField] private int minIslandAmount;
    [SerializeField] private int maxIslandAmount;
    [SerializeField] private float spawnFrameBudgetMs = 12f;

    [Header("Islands")]
    [SerializeField] private List<GameObject> islands = new List<GameObject>();

    [Header("Number Ranges")]
    [SerializeField] private List<NumberRanges> numberRanges = new List<NumberRanges>();
    [SerializeField] private float rangedNumber;

    private bool _generating;
    private bool _started;
    private bool _complete;
    private bool _hasSeed;
    private int _seed;
    private System.Random _rng;
    private bool _seedHandlerRegistered;
    private bool _requestHandlerRegistered;

    private static MapGenerator _sessionOwner;

    private const string MapSeedMessage = "Airplane.MapSeed";
    private const string MapSeedRequestMessage = "Airplane.MapSeedRequest";

    public bool IsGenerating => _generating;
    public bool IsComplete => _complete;
    public bool HasSeed => _hasSeed;
    public float Progress { get; private set; }
    public int IslandTarget { get; private set; }
    public int IslandSpawned { get; private set; }

    private void OnEnable()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager)
            return;

        manager.OnClientConnectedCallback += OnClientConnected;
        manager.OnServerStarted += OnServerStarted;
        TryRegisterHandlers();
    }

    private void OnDisable()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager)
            return;

        manager.OnClientConnectedCallback -= OnClientConnected;
        manager.OnServerStarted -= OnServerStarted;
        UnregisterHandlers();

        if (_sessionOwner == this)
            _sessionOwner = null;
    }

    private void Start()
    {
        if (MapLoadGate.IsHeld)
            return;
        StartSpawningIslands();
    }

    public void StartSpawningIslands()
    {
        StartCoroutine(GenerateForSession());
    }

    public IEnumerator GenerateForSession()
    {
        if (_sessionOwner && _sessionOwner != this)
        {
            while (_sessionOwner && !_sessionOwner._complete)
                yield return null;
            yield break;
        }

        if (_complete)
            yield break;

        if (_started)
        {
            while (!_complete)
                yield return null;
            yield break;
        }

        _started = true;
        _sessionOwner = this;
        yield return StartCoroutine(EnsureSeed());
        yield return StartCoroutine(Generate());
    }

    private IEnumerator EnsureSeed()
    {
        TryRegisterHandlers();
        NetworkManager manager = NetworkManager.Singleton;

        if (manager && manager.IsListening && manager.IsServer)
        {
            if (!_hasSeed)
                SetSeed(new System.Random().Next());
            BroadcastSeed();
            yield break;
        }

        if (manager && manager.IsListening && manager.IsClient)
        {
            float timer = 0f;
            while (!_hasSeed)
            {
                TryRegisterHandlers();
                timer -= Time.unscaledDeltaTime;
                if (timer <= 0f)
                {
                    RequestSeed();
                    timer = 0.5f;
                }

                yield return null;
            }

            yield break;
        }

        if (!_hasSeed)
            SetSeed(new System.Random().Next());
    }

    private IEnumerator Generate()
    {
        if (_complete)
            yield break;

        _generating = true;
        AssignLocation();
        numberRanges.Clear();
        _rng = new System.Random(_seed);

        int islandAmount = 0;
        if (islands != null && islands.Count > 0)
            islandAmount = _rng.Next(minIslandAmount, maxIslandAmount + 1);

        IslandTarget = islandAmount;
        IslandSpawned = 0;
        Progress = islandAmount == 0 ? 1f : 0f;

        float budget = Mathf.Max(1f, spawnFrameBudgetMs) * 0.001f;
        float frameStart = Time.realtimeSinceStartup;

        for (int i = 0; i < islandAmount; i++)
        {
            Vector3 position = ReturnCheckedSpawnPosition();
            Instantiate(islands[_rng.Next(0, islands.Count)], position, Quaternion.identity);
            IslandSpawned = i + 1;
            Progress = IslandSpawned / (float)islandAmount;

            if (Time.realtimeSinceStartup - frameStart >= budget)
            {
                yield return null;
                frameStart = Time.realtimeSinceStartup;
            }
        }

        Progress = 1f;
        _complete = true;
        _generating = false;
        _rng = null;
    }

    private Vector3 ReturnCheckedSpawnPosition()
    {
        bool isChecked = false;

        Vector3 spawnPosition = Vector3.zero;
        while (!isChecked)
        {
            spawnPosition = ReturnSpawnPosition();
            if (!CheckNumber(spawnPosition))
            {
                isChecked = true;
            }
        }

        return spawnPosition;
    }

    private Vector3 ReturnSpawnPosition()
    {
        float randomX = NextFloat(minSpawnPosition.x, maxSpawnPosition.x);
        float randomY = NextFloat(minSpawnPosition.y, maxSpawnPosition.y);
        float randomZ = NextFloat(minSpawnPosition.z, maxSpawnPosition.z);

        Vector3 spawnPosition = new Vector3(randomX, randomY, randomZ);
        return spawnPosition;
    }

    private bool CheckNumber(Vector3 spawnPosition)
    {

        float minX = spawnPosition.x - rangedNumber;
        float maxX = spawnPosition.x + rangedNumber;
        float minY = spawnPosition.y - rangedNumber;
        float maxY = spawnPosition.y + rangedNumber;
        float minZ = spawnPosition.z - rangedNumber;
        float maxZ = spawnPosition.z + rangedNumber;

        NumberRanges numberRange = new NumberRanges(minX, maxX, minY, maxY, minZ, maxZ);

        bool isInRange = false;
        for (int i = 0; i < numberRanges.Count; i++)
        {
            isInRange = (spawnPosition.x >= numberRanges[i].minX && spawnPosition.x < numberRanges[i].maxX &&
                spawnPosition.y >= numberRanges[i].minY && spawnPosition.y < numberRanges[i].maxY &&
                spawnPosition.z >= numberRanges[i].minZ && spawnPosition.z < numberRanges[i].maxZ) ? true : false;

            if (isInRange)
            {
                return true;
            }
        }


        numberRanges.Add(numberRange);
        return false;
    }

    private void AssignLocation()
    {

        if (!spawnArea) return;
        _spawnAreaCollider = spawnArea.GetComponent<Collider>().bounds;
        minSpawnPosition.x = _spawnAreaCollider.min.x;
        maxSpawnPosition.x = _spawnAreaCollider.max.x;
        minSpawnPosition.y = _spawnAreaCollider.min.y;
        maxSpawnPosition.y = _spawnAreaCollider.max.y;
        minSpawnPosition.z = _spawnAreaCollider.min.z;
        maxSpawnPosition.z = _spawnAreaCollider.max.z;
    }

    private float NextFloat(float min, float max)
    {
        return (float)(min + _rng.NextDouble() * (max - min));
    }

    private void SetSeed(int seed)
    {
        if (_hasSeed)
            return;

        _seed = seed;
        _hasSeed = true;
    }

    private void OnServerStarted()
    {
        TryRegisterHandlers();
        if (_hasSeed)
            BroadcastSeed();
    }

    private void OnClientConnected(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager || !manager.IsServer || !_hasSeed || clientId == manager.LocalClientId)
            return;

        SendSeed(clientId);
    }

    private void TryRegisterHandlers()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager || manager.CustomMessagingManager == null)
            return;

        if (!_seedHandlerRegistered)
        {
            manager.CustomMessagingManager.RegisterNamedMessageHandler(MapSeedMessage, OnSeedReceived);
            _seedHandlerRegistered = true;
        }

        if (manager.IsServer && !_requestHandlerRegistered)
        {
            manager.CustomMessagingManager.RegisterNamedMessageHandler(MapSeedRequestMessage, OnSeedRequested);
            _requestHandlerRegistered = true;
        }
    }

    private void UnregisterHandlers()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager || manager.CustomMessagingManager == null)
            return;

        if (_seedHandlerRegistered)
        {
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(MapSeedMessage);
            _seedHandlerRegistered = false;
        }

        if (_requestHandlerRegistered)
        {
            manager.CustomMessagingManager.UnregisterNamedMessageHandler(MapSeedRequestMessage);
            _requestHandlerRegistered = false;
        }
    }

    private void OnSeedReceived(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out int seed);
        SetSeed(seed);
    }

    private void OnSeedRequested(ulong sender, FastBufferReader reader)
    {
        if (!_hasSeed)
            return;

        SendSeed(sender);
    }

    private void BroadcastSeed()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager || !manager.IsServer || !_hasSeed)
            return;

        foreach (ulong clientId in manager.ConnectedClientsIds)
        {
            if (clientId == manager.LocalClientId)
                continue;
            SendSeed(clientId);
        }
    }

    private void RequestSeed()
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager || !manager.IsListening || manager.CustomMessagingManager == null)
            return;

        FastBufferWriter writer = new FastBufferWriter(1, Allocator.Temp);
        writer.WriteValueSafe((byte)1);
        manager.CustomMessagingManager.SendNamedMessage(
            MapSeedRequestMessage,
            NetworkManager.ServerClientId,
            writer,
            NetworkDelivery.Reliable);
        writer.Dispose();
    }

    private void SendSeed(ulong clientId)
    {
        NetworkManager manager = NetworkManager.Singleton;
        if (!manager || manager.CustomMessagingManager == null)
            return;

        FastBufferWriter writer = new FastBufferWriter(sizeof(int), Allocator.Temp);
        writer.WriteValueSafe(_seed);
        manager.CustomMessagingManager.SendNamedMessage(
            MapSeedMessage,
            clientId,
            writer,
            NetworkDelivery.Reliable);
        writer.Dispose();
    }

}
