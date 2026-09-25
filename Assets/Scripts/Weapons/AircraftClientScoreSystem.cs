using Airplane.Multiplayer;
using Airplane.Weapons;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements.Experimental;

[RequireComponent(typeof(AircraftVitality))]
public class AircraftClientScoreSystem : NetworkBehaviour
{

    [SerializeField] private TMP_Text _pointText;
    private AircraftVitality _aircraftVitality;

    

    private void Awake()
    {
        _pointText = PlayerCanvasScript.Instance.GetComponentInChildren<TMP_Text>();
        _aircraftVitality = GetComponent<AircraftVitality>();
        _aircraftVitality.OnDeathEvent += HandleDeath;
    }

    [ClientRpc]
    public void HandlePointClientRpc(ulong shooterId, int point)
    {
        if (NetworkManager.Singleton.LocalClientId == shooterId)
        {
            _pointText.text = point.ToString();
        }
        
    }

    private void HandleDeath(GunHit gunHit)
    {
        if (gunHit.Shooter.TryGetComponent(out NetworkObject networkObject)) 
        {
            AircraftServerScoreSystem.Instance.HandlePointServerRpc(networkObject.OwnerClientId);
        }
    }
}
