using UnityEngine;

namespace Airplane.Multiplayer
{
    public static class LocalPlayerIdentity
    {
        private const string PrefsKey = "Airplane.PilotName";
        private const string AircraftPrefsKey = "Airplane.AircraftIndex";
        private const int MaxLength = 24;

        private static string _pilotName;
        private static int _aircraftIndex = int.MinValue;

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

        public static int AircraftIndex
        {
            get
            {
                if (_aircraftIndex == int.MinValue)
                    _aircraftIndex = Mathf.Max(0, PlayerPrefs.GetInt(AircraftPrefsKey, 0));
                return _aircraftIndex;
            }
            set
            {
                int next = Mathf.Max(0, value);
                if (next == _aircraftIndex)
                    return;

                _aircraftIndex = next;
                PlayerPrefs.SetInt(AircraftPrefsKey, next);
                PlayerPrefs.Save();
            }
        }

        public static string AircraftName
        {
            get
            {
                AircraftCatalog catalog = AircraftSelection.Catalog;
                if (!catalog || catalog.Count == 0)
                    return "";
                return catalog.GetDisplayName(AircraftIndex);
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
