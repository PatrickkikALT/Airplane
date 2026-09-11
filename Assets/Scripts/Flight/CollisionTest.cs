using Airplane.FlightSimulation;
using Airplane.Multiplayer;
using UnityEngine;

public class CollisionTest : MonoBehaviour
{
    private PlaneRigidbody _rigidbody;
    private NetworkedAircraft _networked;

    private void Start()
    {
        _rigidbody = GetComponent<PlaneRigidbody>();
        _networked = GetComponent<NetworkedAircraft>();
    }

    private void OnPlaneCollisionEnter(PlaneCollision hit)
    {
        float impactKmh = _rigidbody.TrueAirspeed * FlightSimMath.AirSpeedToKnots * FlightSimMath.KnotsToKmh;

        if (_networked && _networked.IsSpawned)
        {
            _networked.ReportCrash(hit.Point, impactKmh);
            return;
        }
    }
}
