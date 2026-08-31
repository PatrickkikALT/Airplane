using Airplane.FlightSimulation;
using Airplane.Multiplayer;

namespace Airplane.UI
{
    /// <summary>
    /// Session-local cheat switches written by <see cref="AdminConsole"/>. Not replicated: a remote
    /// peer neither sees nor inherits them.
    /// </summary>
    public static class CheatFlags
    {
        public static bool HomingBullets;
        public static bool GodMode;
        public static bool InfiniteAmmo;

        /// <summary>Homing turn rate, degrees per second. 150 is a firm pull without snapping.</summary>
        public static float HomingTurnRateDeg = 150f;

        /// <summary>True while the admin console owns the keyboard, so flight and fire stay still.</summary>
        public static bool BlockPlayerInput { get; set; }

        /// <summary>True when cheats should apply to this airframe (the aircraft this peer flies).</summary>
        public static bool AppliesTo(PlaneRigidbody body)
        {
            NetworkedAircraft local = NetworkedAircraft.Local;
            return local && body && local.Body == body;
        }
    }
}
