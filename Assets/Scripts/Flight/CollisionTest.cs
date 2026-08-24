using Airplane.FlightSimulation;
using UnityEngine;

public class CollisionTest : MonoBehaviour
{
    private PlaneRigidbody _rigidbody;

    private void Start()
    {
        _rigidbody = GetComponent<PlaneRigidbody>();
    }
    
    /// <summary>
    /// Gets called by <see cref="PlaneRigidbody"/>'s SendMessage on collision.
    /// </summary>
    private void OnPlaneCollisionEnter(PlaneCollision hit)
    {
        if (_rigidbody.TrueAirspeed * FlightSimMath.AirSpeedToKnots * FlightSimMath.KnotsToKmh > 100)
        {
            Destroy(gameObject);
        }
    }
}
