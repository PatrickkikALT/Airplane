using System;
using System.Collections.Generic;
using System.Text;
using Airplane.Multiplayer;
using Airplane.Weapons;
using Airplane.Weather;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Airplane.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    [AddComponentMenu("Airplane/UI/Admin Console")]
    public sealed class AdminConsole : MonoBehaviour
    {
        private const int MaxLogLines = 40;
        private const int MaxHistory = 64;
        private const string InputControlName = "AdminConsoleInput";
        private const string PasswordKey = "ADMIN_PASSWORD";

        private static AdminConsole _instance;

        private readonly List<string> _log = new List<string>(MaxLogLines);
        private readonly List<string> _history = new List<string>(MaxHistory);
        private readonly StringBuilder _helpBuilder = new StringBuilder(512);
        private readonly List<Command> _commands = new List<Command>();

        private bool _open;
        private bool _unlocked;
        private string _password = "";
        private string _input = "";
        private int _historyIndex = -1;
        private bool _focusPending;
        private bool _openedThisFrame;
        private GUIStyle _panelStyle;
        private GUIStyle _logStyle;
        private GUIStyle _inputStyle;
        private GUIStyle _hintStyle;
        private Texture2D _panelTex;

        /// <summary>True while the console is on screen and eating keyboard input.</summary>
        public static bool IsOpen => _instance != null && _instance._open;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (_instance != null)
                return;

            GameObject host = new GameObject("Admin Console");
            DontDestroyOnLoad(host);
            host.AddComponent<AdminConsole>();
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(this);
                return;
            }

            _instance = this;
            _password = EnvFile.Get(PasswordKey);
            RegisterCommands();
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
                CheatFlags.BlockPlayerInput = false;
            }

            if (_panelTex)
                Destroy(_panelTex);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.semicolonKey.wasPressedThisFrame)
            {
                if (_open)
                    Close();
                else
                    Open();
                return;
            }

            if (!_open)
                return;

            if (keyboard.escapeKey.wasPressedThisFrame)
            {
                Close();
                return;
            }

            SyncLocalPlayerInput();
        }

        private void Open()
        {
            _open = true;
            _openedThisFrame = true;
            _focusPending = true;
            _historyIndex = -1;
            _input = "";
            CheatFlags.BlockPlayerInput = true;
            SyncLocalPlayerInput();
            ZeroLocalTriggers();

            if (!_unlocked)
            {
                _log.Clear();
                Print(string.IsNullOrEmpty(_password)
                    ? "no " + PasswordKey + " in .env"
                    : "password required");
            }
        }

        private void Close()
        {
            _open = false;
            _focusPending = false;
            CheatFlags.BlockPlayerInput = false;
            SyncLocalPlayerInput();
        }

        private static void ZeroLocalTriggers()
        {
            NetworkedAircraft local = NetworkedAircraft.Local;
            if (!local)
                return;

            AircraftWeaponsController weapons = local.GetComponent<AircraftWeaponsController>();
            if (weapons && weapons.InputEnabled)
                weapons.ApplyExternalFire(0f, 0f);
        }

        private static void SyncLocalPlayerInput()
        {
            NetworkedAircraft local = NetworkedAircraft.Local;
            if (!local)
                return;

            PlayerInput playerInput = local.GetComponent<PlayerInput>();
            if (!playerInput)
                return;

            bool want = local.IsOwner && !local.IsBot && local.IsAlive && !CheatFlags.BlockPlayerInput;
            playerInput.enabled = want;
        }

        private void OnGUI()
        {
            if (!_open)
                return;

            EnsureStyles();
            GUI.depth = -1000;

            Event ev = Event.current;
            if (ev != null)
                HandleGuiEvent(ev);

            float width = Screen.width;
            float logHeight = Mathf.Min(Screen.height * 0.38f, 16f + _log.Count * 18f + 8f);
            float inputHeight = 28f;
            float hintHeight = 20f;
            float total = logHeight + inputHeight + hintHeight;
            Rect panel = new Rect(0f, Screen.height - total, width, total);

            GUI.Box(panel, GUIContent.none, _panelStyle);

            float y = panel.y + 6f;
            for (int i = 0; i < _log.Count; i++)
            {
                GUI.Label(new Rect(12f, y, width - 24f, 18f), _log[i], _logStyle);
                y += 18f;
            }

            Rect inputRect = new Rect(8f, panel.yMax - hintHeight - inputHeight, width - 16f, 22f);
            GUI.SetNextControlName(InputControlName);

            string typed = _input ?? "";
            if (_openedThisFrame)
            {
                // The toggle key would otherwise land in the field on the same frame.
                if (typed == ";" || typed == ":")
                    typed = "";
            }

            string next = _unlocked
                ? GUI.TextField(inputRect, typed, _inputStyle)
                : GUI.PasswordField(inputRect, typed, '*', _inputStyle);
            if (!_openedThisFrame)
                _input = next;
            else
                _input = "";

            GUI.Label(
                new Rect(10f, panel.yMax - hintHeight, width - 20f, hintHeight),
                _unlocked
                    ? "enter run   up/down history   tab complete   esc / ; close"
                    : "enter submit   esc / ; close",
                _hintStyle);

            if (_focusPending)
            {
                GUI.FocusControl(InputControlName);
                _focusPending = false;
            }

            _openedThisFrame = false;
        }

        private void HandleGuiEvent(Event ev)
        {
            if (ev.type != EventType.KeyDown)
                return;

            if (ev.keyCode == KeyCode.Semicolon || ev.character == ';' || ev.character == ':')
            {
                ev.Use();
                return;
            }

            if (ev.keyCode == KeyCode.Escape)
            {
                ev.Use();
                return;
            }

            if (ev.keyCode == KeyCode.Return || ev.keyCode == KeyCode.KeypadEnter)
            {
                ev.Use();
                Submit();
                return;
            }

            if (!_unlocked)
                return;

            if (ev.keyCode == KeyCode.UpArrow)
            {
                ev.Use();
                HistoryStep(-1);
                return;
            }

            if (ev.keyCode == KeyCode.DownArrow)
            {
                ev.Use();
                HistoryStep(1);
                return;
            }

            if (ev.keyCode == KeyCode.Tab)
            {
                ev.Use();
                Complete();
            }
        }

        private void Submit()
        {
            string line = (_input ?? "").Trim();
            _input = "";
            _historyIndex = -1;
            _focusPending = true;

            if (line.Length == 0)
                return;

            if (!_unlocked)
            {
                TryUnlock(line);
                return;
            }

            Remember(line);
            Print("> " + line);
            Execute(line);
        }

        private void TryUnlock(string attempt)
        {
            if (string.IsNullOrEmpty(_password) || attempt != _password)
            {
                Print("denied");
                return;
            }

            _unlocked = true;
            _log.Clear();
            Print("admin console  ·  ; to close  ·  help for commands");
        }

        private void Relock()
        {
            _unlocked = false;
            _input = "";
            _historyIndex = -1;
            _log.Clear();
            Print("locked");
            Print(string.IsNullOrEmpty(_password)
                ? "no " + PasswordKey + " in .env"
                : "password required");
        }

        private void Remember(string line)
        {
            if (_history.Count > 0 && _history[_history.Count - 1] == line)
                return;

            _history.Add(line);
            if (_history.Count > MaxHistory)
                _history.RemoveAt(0);
        }

        private void HistoryStep(int delta)
        {
            if (_history.Count == 0)
                return;

            if (_historyIndex < 0)
                _historyIndex = _history.Count;

            _historyIndex = Mathf.Clamp(_historyIndex + delta, 0, _history.Count);
            _input = _historyIndex >= _history.Count ? "" : _history[_historyIndex];
            _focusPending = true;
        }

        private void Complete()
        {
            string prefix = (_input ?? "").Trim();
            if (prefix.Length == 0)
            {
                Print("commands: " + CommandNames());
                return;
            }

            string token = prefix;
            int space = prefix.IndexOf(' ');
            if (space >= 0)
                token = prefix.Substring(0, space);

            List<string> matches = new List<string>();
            for (int i = 0; i < _commands.Count; i++)
            {
                if (_commands[i].Name.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    matches.Add(_commands[i].Name);
            }

            if (matches.Count == 1)
            {
                _input = matches[0] + (space >= 0 ? prefix.Substring(space) : " ");
                _focusPending = true;
                return;
            }

            if (matches.Count > 1)
                Print(string.Join("  ", matches));
        }

        private void Execute(string line)
        {
            SplitArgs(line, out string name, out string[] args);
            if (string.IsNullOrEmpty(name))
                return;

            for (int i = 0; i < _commands.Count; i++)
            {
                Command command = _commands[i];
                if (!string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    string result = command.Handler(args);
                    if (!string.IsNullOrEmpty(result))
                        Print(result);
                }
                catch (Exception ex)
                {
                    Print("error: " + ex.Message);
                }

                return;
            }

            Print("unknown command '" + name + "'  ·  try help");
        }

        private static void SplitArgs(string line, out string name, out string[] args)
        {
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                name = "";
                args = Array.Empty<string>();
                return;
            }

            name = parts[0];
            if (parts.Length == 1)
            {
                args = Array.Empty<string>();
                return;
            }

            args = new string[parts.Length - 1];
            Array.Copy(parts, 1, args, 0, args.Length);
        }

        private void Print(string line)
        {
            _log.Add(line);
            while (_log.Count > MaxLogLines)
                _log.RemoveAt(0);
        }

        private string CommandNames()
        {
            _helpBuilder.Length = 0;
            for (int i = 0; i < _commands.Count; i++)
            {
                if (i > 0)
                    _helpBuilder.Append("  ");
                _helpBuilder.Append(_commands[i].Name);
            }

            return _helpBuilder.ToString();
        }

        private void RegisterCommands()
        {
            Add("help", "help [command]", "List commands, or describe one.", CmdHelp);
            Add("lock", "lock", "Lock the console. The password is required again.", CmdLock);
            Add("status", "status", "Show cheat flags currently in effect.", CmdStatus);
            Add("clear", "clear", "Clear the console log.", CmdClear);
            Add("homing", "homing [on|off|rate]", "Steer this aircraft's rounds into the nearest target.", CmdHoming);
            Add("god", "god [on|off]", "Ignore crashes and gun damage on this aircraft.", CmdGod);
            Add("ammo", "ammo [on|off]", "Infinite magazines on this aircraft.", CmdAmmo);
            Add("heal", "heal [name|*]", "Refill hit points. Default is you.", CmdHeal);
            Add("reload", "reload [name|*]", "Refill every gun magazine. Default is you.", CmdReload);
            Add("bots", "bots [count]", "Set the bot squadron size. Replicated, any admin.", CmdBots);
            Add("dummy", "dummy", "Spawn a still target in front of you. Replicated, any admin.", CmdDummy);
            Add("timescale", "timescale [rate]", "Set Time.timeScale for everyone. 1 is normal.", CmdTimescale);
            Add("nametags", "nametags [on|off]", "Toggle aircraft nametags.", CmdNametags);
            Add("scale", "scale [scale] [name|*]", "Change an aircraft's scale. Default is you.", CmdScale);
            Add("destroy", "destroy [name|*]", "Crash an aircraft by nametag, or * for everyone.", CmdDestroy);
            Add("speed", "speed [speed] [name|*]", "Set max thrust. Base is 60000. Default is you.", CmdSpeed);
            Add("weather", "weather [weather]", "Set the shared weather. No argument lists presets.", CmdWeather);
        }

        private void Add(string name, string usage, string help, Func<string[], string> handler)
        {
            _commands.Add(new Command
            {
                Name = name,
                Usage = usage,
                Help = help,
                Handler = handler
            });
        }

        private string CmdWeather(string[] args)
        {
            WeatherManager manager = WeatherManager.Instance;
            if (!manager)
                return "no weather manager";

            string[] possibleWeathers = manager.GetWeathers();
            if (args == null || args.Length == 0)
                return string.Join(", ", possibleWeathers);

            string name = args[0];
            if (!manager.HasWeather(name))
                return "Invalid weather, run weather for possible weathers.";

            string error = AdminSession.Send(AdminCommand.Weather, name, 0f);
            return string.IsNullOrEmpty(error) ? "weather " + name.ToLowerInvariant() : error;
        }

        private string CmdSpeed(string[] args)
        {
            if (args == null || args.Length == 0 || !int.TryParse(args[0], out int speed))
                return "usage: speed [speed] [name|*]";

            string target = args.Length > 1 ? args[1] : "";
            if (!string.IsNullOrEmpty(target) && target != "*" && !AdminSession.AnyMatch(target))
                return "no aircraft named '" + target + "'";

            string error = AdminSession.Send(AdminCommand.Speed, target, speed);
            return string.IsNullOrEmpty(error) ? "speed " + speed : error;
        }

        private string CmdDestroy(string[] args)
        {
            if (args == null || args.Length == 0)
                return "usage: destroy [name|*]";

            string target = args[0];
            if (target != "*" && !AdminSession.AnyMatch(target))
                return "no aircraft named '" + target + "'";

            string error = AdminSession.Send(AdminCommand.Destroy, target, 0f);
            return string.IsNullOrEmpty(error) ? "destroy " + target : error;
        }

        private string CmdScale(string[] args)
        {
            if (args == null || args.Length == 0 || !float.TryParse(args[0], out float scale))
                return "usage: scale [scale] [name|*]";

            scale = Mathf.Clamp(scale, 0f, 50f);
            string target = args.Length > 1 ? args[1] : "";
            if (!string.IsNullOrEmpty(target) && target != "*" && !AdminSession.AnyMatch(target))
                return "no aircraft named '" + target + "'";

            string error = AdminSession.Send(AdminCommand.Scale, target, scale);
            return string.IsNullOrEmpty(error) ? "scale " + scale.ToString("0.###") : error;
        }

        private string CmdHelp(string[] args)
        {
            if (args.Length > 0)
            {
                for (int i = 0; i < _commands.Count; i++)
                {
                    if (!string.Equals(_commands[i].Name, args[0], StringComparison.OrdinalIgnoreCase))
                        continue;
                    return _commands[i].Usage + "  —  " + _commands[i].Help;
                }

                return "no such command '" + args[0] + "'";
            }

            Print("commands:");
            for (int i = 0; i < _commands.Count; i++)
                Print("  " + _commands[i].Usage.PadRight(28) + _commands[i].Help);

            return "";
        }

        private string CmdStatus(string[] args)
        {
            WeatherManager weather = WeatherManager.Instance;
            string weatherName = weather != null ? weather.CurrentWeatherName : "";
            return "homing " + OnOff(CheatFlags.HomingBullets)
                   + "  (" + CheatFlags.HomingTurnRateDeg.ToString("0") + " deg/s)"
                   + "  god " + OnOff(CheatFlags.GodMode)
                   + "  ammo " + OnOff(CheatFlags.InfiniteAmmo)
                   + "  timescale " + Time.timeScale.ToString("0.###")
                   + (string.IsNullOrEmpty(weatherName) ? "" : "  weather " + weatherName);
        }

        private string CmdClear(string[] args)
        {
            _log.Clear();
            return "";
        }

        private string CmdLock(string[] args)
        {
            Relock();
            return "";
        }

        private string CmdHoming(string[] args)
        {
            if (args.Length > 0 && float.TryParse(args[0], out float rate))
            {
                CheatFlags.HomingTurnRateDeg = Mathf.Clamp(rate, 10f, 720f);
                CheatFlags.HomingBullets = true;
                return "homing on  ·  " + CheatFlags.HomingTurnRateDeg.ToString("0") + " deg/s";
            }

            if (!TryParseToggle(args, CheatFlags.HomingBullets, out bool next))
                return "usage: homing [on|off|<deg/s>]";

            CheatFlags.HomingBullets = next;
            return "homing " + OnOff(next)
                   + (next ? "  ·  " + CheatFlags.HomingTurnRateDeg.ToString("0") + " deg/s" : "");
        }

        private string CmdGod(string[] args)
        {
            if (!TryParseToggle(args, CheatFlags.GodMode, out bool next))
                return "usage: god [on|off]";

            CheatFlags.GodMode = next;
            return "god " + OnOff(next);
        }

        private string CmdAmmo(string[] args)
        {
            if (!TryParseToggle(args, CheatFlags.InfiniteAmmo, out bool next))
                return "usage: ammo [on|off]";

            CheatFlags.InfiniteAmmo = next;
            return "infinite ammo " + OnOff(next);
        }

        private static string CmdHeal(string[] args)
        {
            string target = args != null && args.Length > 0 ? args[0] : "";
            if (!string.IsNullOrEmpty(target) && target != "*" && !AdminSession.AnyMatch(target))
                return "no aircraft named '" + target + "'";

            string error = AdminSession.Send(AdminCommand.Heal, target, 0f);
            if (!string.IsNullOrEmpty(error))
                return error;

            if (string.IsNullOrEmpty(target))
            {
                NetworkedAircraft local = NetworkedAircraft.Local;
                AircraftVitality vitality = local ? local.GetComponent<AircraftVitality>() : null;
                if (vitality && (!AdminSession.IsListening || IsServer()))
                    return "healed  ·  " + vitality.HitPoints.ToString("0") + " hp";
            }

            return "healed";
        }

        private static string CmdReload(string[] args)
        {
            string target = args != null && args.Length > 0 ? args[0] : "";
            if (!string.IsNullOrEmpty(target) && target != "*" && !AdminSession.AnyMatch(target))
                return "no aircraft named '" + target + "'";

            string error = AdminSession.Send(AdminCommand.Reload, target, 0f);
            return string.IsNullOrEmpty(error) ? "magazines refilled" : error;
        }

        private static string CmdBots(string[] args)
        {
            AircraftNetworkSpawner spawner = AircraftNetworkSpawner.Instance;
            if (args.Length == 0)
            {
                if (IsServer() && spawner != null)
                    return "bots " + spawner.LiveBotCount + "/" + spawner.DesiredBotCount;
                return "usage: bots [count]";
            }

            if (!int.TryParse(args[0], out int count))
                return "usage: bots [count]";

            string error = AdminSession.Send(AdminCommand.Bots, "", count);
            if (!string.IsNullOrEmpty(error))
                return error;

            if (IsServer() && spawner != null)
                return "bots " + spawner.LiveBotCount + "/" + spawner.DesiredBotCount;

            return "bots " + count + "  ·  sent";
        }

        private static string CmdDummy(string[] args)
        {
            string error = AdminSession.Send(AdminCommand.Dummy, "", 0f);
            return string.IsNullOrEmpty(error) ? "dummy spawned" : error;
        }

        private static string CmdTimescale(string[] args)
        {
            if (args.Length == 0)
                return "timescale " + Time.timeScale.ToString("0.###");

            if (!float.TryParse(args[0], out float scale))
                return "usage: timescale [rate]";

            scale = Mathf.Clamp(scale, 0f, 8f);
            string error = AdminSession.Send(AdminCommand.Timescale, "", scale);
            return string.IsNullOrEmpty(error) ? "timescale " + scale.ToString("0.###") : error;
        }

        private static string CmdNametags(string[] args)
        {
            if (!TryParseToggle(args, AircraftNametagOverlay.Enabled, out bool next))
                return "usage: nametags [on|off]";

            AircraftNametagOverlay.Enabled = next;
            return "nametags " + OnOff(next);
        }

        private static bool IsServer()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager != null && manager.IsListening && manager.IsServer;
        }

        private static bool TryParseToggle(string[] args, bool current, out bool next)
        {
            if (args == null || args.Length == 0)
            {
                next = !current;
                return true;
            }

            string value = args[0].ToLowerInvariant();
            if (value == "on" || value == "1" || value == "true")
            {
                next = true;
                return true;
            }

            if (value == "off" || value == "0" || value == "false")
            {
                next = false;
                return true;
            }

            next = current;
            return false;
        }

        private static string OnOff(bool value)
        {
            return value ? "on" : "off";
        }

        private void EnsureStyles()
        {
            if (_panelStyle != null)
                return;

            _panelTex = new Texture2D(1, 1, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "AdminConsolePanel"
            };
            _panelTex.SetPixel(0, 0, new Color(0.04f, 0.05f, 0.07f, 0.88f));
            _panelTex.Apply();

            _panelStyle = new GUIStyle(GUI.skin.box)
            {
                normal = { background = _panelTex },
                border = new RectOffset(0, 0, 0, 0)
            };

            _logStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 13,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.82f, 0.9f, 0.78f, 1f) },
                clipping = TextClipping.Clip,
                wordWrap = false
            };

            _inputStyle = new GUIStyle(GUI.skin.textField)
            {
                fontSize = 14,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };

            _hintStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                normal = { textColor = new Color(0.65f, 0.7f, 0.62f, 0.9f) }
            };
        }

        private struct Command
        {
            public string Name;
            public string Usage;
            public string Help;
            public Func<string[], string> Handler;
        }
    }
}
