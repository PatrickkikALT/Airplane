using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Airplane.UI
{
    /// <summary>
    /// Reads KEY=value lines from a <c>.env</c> next to the project (Editor) or the player
    /// executable (builds). The file is not an asset and is not packed into the build.
    /// </summary>
    public static class EnvFile
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal);
        private static bool _loaded;

        /// <summary>Absolute path that was parsed, or null if none existed.</summary>
        public static string LoadedPath { get; private set; }

        public static string Get(string key)
        {
            EnsureLoaded();
            if (string.IsNullOrEmpty(key))
                return "";

            return Values.TryGetValue(key, out string value) ? value : "";
        }

        private static void EnsureLoaded()
        {
            if (_loaded)
                return;

            _loaded = true;
            string path = ResolvePath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            LoadedPath = path;
            try
            {
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                    ParseLine(lines[i]);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("Failed to read .env: " + ex.Message);
            }
        }

        private static string ResolvePath()
        {
            // Editor: <project>/Assets → <project>/.env
            // Player: <game>_Data → folder containing the executable.
            string data = Application.dataPath;
            if (!string.IsNullOrEmpty(data))
            {
                string root = Directory.GetParent(data)?.FullName;
                if (!string.IsNullOrEmpty(root))
                {
                    string candidate = System.IO.Path.Combine(root, ".env");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }

            string cwd = System.IO.Path.Combine(Directory.GetCurrentDirectory(), ".env");
            return File.Exists(cwd) ? cwd : null;
        }

        private static void ParseLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return;

            string trimmed = line.Trim();
            if (trimmed[0] == '#' || trimmed.StartsWith("//", StringComparison.Ordinal))
                return;

            int eq = trimmed.IndexOf('=');
            if (eq <= 0)
                return;

            string key = trimmed.Substring(0, eq).Trim();
            if (key.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                key = key.Substring(7).Trim();

            if (key.Length == 0)
                return;

            string value = Unquote(trimmed.Substring(eq + 1).Trim());
            Values[key] = value;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2)
            {
                char first = value[0];
                if ((first == '"' || first == '\'') && value[value.Length - 1] == first)
                    return value.Substring(1, value.Length - 2);
            }

            return value;
        }
    }
}
