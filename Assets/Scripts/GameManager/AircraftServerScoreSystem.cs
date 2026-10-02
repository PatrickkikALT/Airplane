using System.Collections.Generic;
using Airplane.Multiplayer;
using Unity.Netcode;
using UnityEngine;

public struct RoundStanding
{
    public string Name;
    public int Points;
}

public class AircraftServerScoreSystem : NetworkBehaviour
{
    public static AircraftServerScoreSystem Instance;

    private readonly Dictionary<string, RoundStanding> _standings = new Dictionary<string, RoundStanding>();
    private bool _roundActive;

    private void Awake()
    {
        if (!Instance)
            Instance = this;
    }

    public void BeginRound()
    {
        if (!IsServer)
            return;

        _standings.Clear();
        _roundActive = true;

        IReadOnlyList<NetworkedAircraft> aircraft = NetworkedAircraft.All;
        for (int i = 0; i < aircraft.Count; i++)
        {
            NetworkedAircraft pilot = aircraft[i];
            if (!pilot)
                continue;

            string key = KeyOf(pilot);
            _standings[key] = new RoundStanding
            {
                Name = pilot.DisplayName,
                Points = 0
            };
        }
    }

    public void EndRound()
    {
        _roundActive = false;
    }

    public List<RoundStanding> CopyStandings()
    {
        List<RoundStanding> copy = new List<RoundStanding>(_standings.Count);
        foreach (RoundStanding standing in _standings.Values)
            copy.Add(standing);

        copy.Sort((a, b) => b.Points.CompareTo(a.Points));
        return copy;
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void ReportKillRpc(NetworkObjectReference shooterRef)
    {
        if (!_roundActive)
            return;
        if (!shooterRef.TryGet(out NetworkObject shooterObject))
            return;
        if (!shooterObject.TryGetComponent(out NetworkedAircraft shooter))
            return;

        string key = KeyOf(shooter);
        RoundStanding standing = _standings.TryGetValue(key, out RoundStanding existing)
            ? existing
            : new RoundStanding { Name = shooter.DisplayName, Points = 0 };

        standing.Name = shooter.DisplayName;
        standing.Points++;
        _standings[key] = standing;

        if (!shooter.IsBot)
            ReportScoreRpc(shooter.OwnerClientId, standing.Points);
    }

    [Rpc(SendTo.ClientsAndHost, InvokePermission = RpcInvokePermission.Server)]
    private void ReportScoreRpc(ulong ownerClientId, int points)
    {
        if (NetworkManager.LocalClientId != ownerClientId)
            return;

        RoundScoreHud.Instance?.SetScore(points);
    }

    private static string KeyOf(NetworkedAircraft aircraft)
    {
        if (aircraft.IsBot)
            return "b:" + aircraft.DisplayName;
        return "p:" + aircraft.OwnerClientId;
    }
}
