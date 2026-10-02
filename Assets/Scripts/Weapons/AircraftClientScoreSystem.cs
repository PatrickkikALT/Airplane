using Airplane.Multiplayer;
using Airplane.Weapons;
using Unity.Netcode;
using UnityEngine;

[RequireComponent(typeof(AircraftVitality))]
public class AircraftClientScoreSystem : NetworkBehaviour
{
    private AircraftVitality _aircraftVitality;

    private void Awake()
    {
        _aircraftVitality = GetComponent<AircraftVitality>();
    }

    public override void OnNetworkSpawn()
    {
        _aircraftVitality.OnDeathEvent += HandleDeath;
    }

    public override void OnNetworkDespawn()
    {
        if (_aircraftVitality)
            _aircraftVitality.OnDeathEvent -= HandleDeath;
    }

    private void HandleDeath(GunHit gunHit)
    {
        if (!IsSpawned || !IsOwner)
            return;
        if (!gunHit.Shooter || gunHit.Shooter == gunHit.Victim)
            return;

        NetworkedAircraft shooter = gunHit.Shooter.GetComponent<NetworkedAircraft>();
        if (!shooter || !shooter.IsSpawned)
            return;

        AircraftServerScoreSystem.Instance?.ReportKillRpc(shooter.NetworkObject);
    }
}
