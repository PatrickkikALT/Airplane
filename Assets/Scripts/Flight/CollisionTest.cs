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

    /// <summary>
    /// Gets called by <see cref="PlaneRigidbody"/>'s SendMessage on collision.
    /// In a session only the owning peer solves contact, so only the owner ever reaches this; the
    /// server decides whether the wreck is real and hands out the replacement aircraft.
    /// </summary>
    private void OnPlaneCollisionEnter(PlaneCollision hit)
    {
        float impactKmh = _rigidbody.TrueAirspeed * FlightSimMath.AirSpeedToKnots * FlightSimMath.KnotsToKmh;

        if (_networked && _networked.IsSpawned)
        {
            _networked.ReportCrash(hit.Point, impactKmh);
            return;
        }

        if (impactKmh > 50)
        {
            Destroy(gameObject);
        }
    }
}
