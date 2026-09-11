using Airplane.FlightSimulation;
using UnityEngine;

namespace Airplane.Weapons
{
    public enum GunTriggerChannel
    {
        Primary = 0,
        Secondary = 1
    }

    public enum GunFireMode
    {
        Hitscan = 0,
        Projectile = 1
    }

    public struct GunHit
    {
        public Vector3 Point;
        public Vector3 Normal;
        public Vector3 Impulse;
        public Vector3 IncomingVelocity;
        public float Damage;
        public Collider Collider;
        public PlaneRigidbody Victim;
        public PlaneRigidbody Shooter;
        public AircraftGun Gun;
    }
}
