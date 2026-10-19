using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

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

    [Header("Islands")]
    [SerializeField] private List<GameObject> islands = new List<GameObject>();

    [Header("Number Ranges")]
    [SerializeField] private List<NumberRanges> numberRanges = new List<NumberRanges>();
    [SerializeField] private float rangedNumber;

    private bool _generating;
    private bool _complete;

    public bool IsGenerating => _generating;
    public bool IsComplete => _complete;
    public float Progress { get; private set; }
    public int IslandTarget { get; private set; }
    public int IslandSpawned { get; private set; }

    private void Start()
    {
        if (MapLoadGate.IsHeld)
            return;
        StartSpawningIslands();
    }

    public void StartSpawningIslands()
    {
        StartCoroutine(Generate());
    }

    public IEnumerator Generate()
    {
        if (_complete)
            yield break;

        if (_generating)
        {
            while (!_complete)
                yield return null;
            yield break;
        }

        _generating = true;
        AssignLocation();

        int islandAmount = 0;
        if (islands != null && islands.Count > 0)
            islandAmount = Random.Range(minIslandAmount, maxIslandAmount + 1);

        IslandTarget = islandAmount;
        IslandSpawned = 0;
        Progress = islandAmount == 0 ? 1f : 0f;

        for (int i = 0; i < islandAmount; i++)
        {
            Vector3 position = ReturnCheckedSpawnPosition();
            Instantiate(islands[Random.Range(0, islands.Count)], position, Quaternion.identity);
            IslandSpawned = i + 1;
            Progress = IslandSpawned / (float)islandAmount;
            yield return null;
        }

        Progress = 1f;
        _complete = true;
        _generating = false;
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
        float randomX = Random.Range(minSpawnPosition.x, maxSpawnPosition.x);
        float randomY = Random.Range(minSpawnPosition.y, maxSpawnPosition.y);
        float randomZ = Random.Range(minSpawnPosition.z, maxSpawnPosition.z);

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

}
