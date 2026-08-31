using UnityEngine;

namespace Airplane.AI
{
    /// <summary>
    /// Callsign pool for bot pilots. Names read like pilots rather than like machines so a nametag
    /// on a bot is indistinguishable at a glance from a nametag on a human; the nametag overlay is
    /// what marks the difference, not the name.
    /// </summary>
    public static class BotCallsigns
    {
        private static readonly string[] Names =
        {
            "Maverick", "Iceman", "Goose", "Viper", "Jester", "Merlin", "Hollywood", "Slider",
            "Sundown", "Wolfman", "Chipper", "Bandit", "Cougar", "Raven", "Hawkeye", "Ripper",
            "Bulldog", "Vandal", "Gunner", "Voodoo", "Reaper", "Shadow", "Nomad", "Havoc",
            "Talon", "Warlock", "Cyclone", "Blitz", "Rogue", "Saber", "Falcon", "Wraith"
        };

        private static int _cursor;

        /// <summary>
        /// Next callsign, walking the pool in order and appending a numeral once it wraps so two
        /// live bots never share a nametag.
        /// </summary>
        public static string Next()
        {
            int index = _cursor++;
            string name = Names[index % Names.Length];
            int lap = index / Names.Length;
            return lap == 0 ? name : $"{name} {lap + 1}";
        }

        public static string Random()
        {
            return Names[UnityEngine.Random.Range(0, Names.Length)];
        }

        public static void Reset()
        {
            _cursor = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay()
        {
            // Domain reload can be disabled, in which case the cursor would keep climbing across
            // play sessions and every bot would come back with a numeral suffix.
            _cursor = 0;
        }
    }
}
