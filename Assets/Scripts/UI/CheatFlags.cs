using Airplane.FlightSimulation;
using Airplane.Multiplayer;

namespace Airplane.UI
{
    public static class CheatFlags
    {
        public static bool HomingBullets;
        public static bool GodMode;
        public static bool InfiniteAmmo;

        public static float HomingTurnRateDeg = 150f;

        public static bool BlockPlayerInput { get; set; }

        public static bool AppliesTo(PlaneRigidbody body)
        {
            NetworkedAircraft local = NetworkedAircraft.Local;
            return local && body && local.Body == body;
        }
    }
}
