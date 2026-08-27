using Airplane.FlightSimulation;
using UnityEngine;

namespace Airplane.Weapons
{
    /// <summary>
    /// Trigger channel a gun listens to. The weapons controller maps one input action onto each channel
    /// so a loadout can mix a primary battery and a secondary cannon without extra scripts.
    /// </summary>
    public enum GunTriggerChannel
    {
        Primary = 0,
        Secondary = 1
    }

    /// <summary>
    /// How a gun delivers a round. Hitscan is a ray evaluated in the firing tick; Projectile integrates
    /// a ballistic tracer that inherits the muzzle velocity of the airframe.
    /// </summary>
    public enum GunFireMode
    {
        Hitscan = 0,
        Projectile = 1
    }

    /// <summary>
    /// One round connecting with a collider. Same role as <see cref="PlaneCollision"/> for
    /// <c>OnPlaneCollisionEnter</c>: <see cref="AircraftGun"/> sends this through
    /// <c>OnGunHit</c> on the victim.
    /// </summary>
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
