using UnityEngine;

namespace Airplane.Multiplayer
{
    /// <summary>
    /// The name this machine flies under. Kept out of <see cref="NetworkSessionUi"/> so the aircraft
    /// can read it the moment it spawns without caring whether a UI exists, and persisted so a pilot
    /// does not have to retype it every session.
    /// </summary>
    public static class LocalPlayerIdentity
    {
        private const string PrefsKey = "Airplane.PilotName";
        private const int MaxLength = 24;

        private static string _pilotName;

        /// <summary>
        /// Chosen callsign, never empty. Falls back to a machine-derived name so an untouched install
        /// still shows something more useful than "Pilot 3" on the nametag.
        /// </summary>
        public static string PilotName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_pilotName))
                {
                    _pilotName = PlayerPrefs.GetString(PrefsKey, "");
                    if (string.IsNullOrWhiteSpace(_pilotName))
                        _pilotName = DefaultName();
                }

                return _pilotName;
            }
            set
            {
                string sanitized = Sanitize(value);
                if (sanitized == _pilotName)
                    return;

                _pilotName = sanitized;
                PlayerPrefs.SetString(PrefsKey, sanitized);
                PlayerPrefs.Save();
            }
        }

        private static string DefaultName()
        {
            try
            {
                string user = System.Environment.UserName;
                if (!string.IsNullOrWhiteSpace(user))
                    return Sanitize(user);
            }
            catch (System.Exception)
            {
                // Some platforms refuse to hand over a user name. A generic callsign is fine.
            }

            return "Pilot";
        }

        private static string Sanitize(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Pilot";

            string trimmed = value.Trim();
            return trimmed.Length > MaxLength ? trimmed.Substring(0, MaxLength) : trimmed;
        }
    }
}
