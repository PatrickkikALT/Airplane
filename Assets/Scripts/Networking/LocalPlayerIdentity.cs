using UnityEngine;

namespace Airplane.Multiplayer
{
    public static class LocalPlayerIdentity
    {
        private const string PrefsKey = "Airplane.PilotName";
        private const int MaxLength = 24;

        private static string _pilotName;

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
