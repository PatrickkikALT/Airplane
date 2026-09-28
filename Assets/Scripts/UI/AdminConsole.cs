using System;
using System.Collections.Generic;
using System.Text;
using Airplane.Multiplayer;
using Airplane.Weapons;
using Airplane.Weather;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace Airplane.UI
{
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-200)]
    [AddComponentMenu("Airplane/UI/Admin Console")]
    public sealed class AdminConsole : MonoBehaviour
    {
        private const int MaxLogLines = 40;
        private const int MaxHistory = 64;
        private const string PasswordKey = "ADMIN_PASSWORD";

        [Header("UI")]
        [SerializeField] private GameObject panel;
        [SerializeField] private TMP_Text logText;
        [SerializeField] private TMP_InputField inputField;
        [SerializeField] private TMP_Text hintText;

        private static AdminConsole _instance;

        private readonly List<string> _log = new List<string>(MaxLogLines);
        private readonly List<string> _history = new List<string>(MaxHistory);
        private readonly StringBuilder _helpBuilder = new StringBuilder(512);
        private readonly StringBuilder _logBuilder = new StringBuilder(1024);
        private readonly List<Command> _commands = new List<Command>();

        private bool _open;
        private bool _unlocked;
        private string _password = "";
        private string _input = "";
        private int _historyIndex = -1;
        private bool _focusPending;
        private int _swallowFrames;
        private int _submitFrame = -1;

        public static bool IsOpen => _instance != null && _instance._open;

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

            if (inputField)
            {
                inputField.lineType = TMP_InputField.LineType.SingleLine;
                Navigation navigation = inputField.navigation;
                navigation.mode = Navigation.Mode.None;
                inputField.navigation = navigation;
                inputField.onValueChanged.AddListener(OnInputChanged);
                inputField.onSubmit.AddListener(OnSubmitField);
            }

            ApplyContentType();
            RefreshHint();
            RefreshLog();
            SetShown(panel, false);
        }

        private void OnDestroy()
        {
            if (inputField)
            {
                inputField.onValueChanged.RemoveListener(OnInputChanged);
                inputField.onSubmit.RemoveListener(OnSubmitField);
            }

            if (_instance == this)
            {
                _instance = null;
                CheatFlags.BlockPlayerInput = false;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.semicolonKey.wasPressedThisFrame)
            {
                _swallowFrames = 3;
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

            if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
            {
                if (_submitFrame == Time.frameCount)
                    return;
                if (inputField)
                    _input = inputField.text ?? "";
                Submit();
                return;
            }

            SyncLocalPlayerInput();

            if (!_unlocked)
                return;

            if (keyboard.upArrowKey.wasPressedThisFrame)
                HistoryStep(-1);
            else if (keyboard.downArrowKey.wasPressedThisFrame)
                HistoryStep(1);
            else if (keyboard.tabKey.wasPressedThisFrame)
                Complete();
        }

        private void LateUpdate()
        {
            if (_swallowFrames > 0)
                _swallowFrames--;

            if (!_focusPending || !_open || !inputField)
                return;

            inputField.ActivateInputField();
            _focusPending = false;
        }

        private void Open()
        {
            _open = true;
            _focusPending = true;
            _historyIndex = -1;
            SetInputText("");
            SetShown(panel, true);
            ApplyContentType();
            RefreshHint();
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
            else
            {
                RefreshLog();
            }
        }

        private void Close()
        {
            _open = false;
            _focusPending = false;
            if (inputField)
                inputField.DeactivateInputField();
            SetShown(panel, false);
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

        private void Submit()
        {
            if (_submitFrame == Time.frameCount)
                return;
            _submitFrame = Time.frameCount;

            string line = (_input ?? "").Trim();
            _historyIndex = -1;
            _focusPending = true;
            SetInputText("");

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

        private void OnSubmitField(string value)
        {
            if (_submitFrame == Time.frameCount)
                return;
            _input = value ?? "";
            Submit();
        }

        private void OnInputChanged(string value)
        {
            if (value != null && (value.IndexOf('\t') >= 0
                || (_swallowFrames > 0 && (value.IndexOf(';') >= 0 || value.IndexOf(':') >= 0))))
            {
                value = value.Replace(";", "").Replace(":", "").Replace("\t", "");
                if (inputField)
                    inputField.SetTextWithoutNotify(value);
            }

            _input = value ?? "";
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
            ApplyContentType();
            RefreshHint();
            Print("admin console  ·  ; to close  ·  help for commands");
        }

        private void Relock()
        {
            _unlocked = false;
            _historyIndex = -1;
            _log.Clear();
            SetInputText("");
            ApplyContentType();
            RefreshHint();
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
            SetInputText(_historyIndex >= _history.Count ? "" : _history[_historyIndex]);
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
                SetInputText(matches[0] + (space >= 0 ? prefix.Substring(space) : " "));
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
            RefreshLog();
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
            Add("scale", "scale [scale|x y z|axis value ...] [name|*]", "Change an aircraft's scale, optionally per axis. Default is you.", CmdScale);
            Add("destroy", "destroy [name|*]", "Crash an aircraft by nametag, or * for everyone.", CmdDestroy);
            Add("speed", "speed [speed] [name|*]", "Set max thrust. Base is 60000. Default is you.", CmdSpeed);
            Add("weather", "weather [weather]", "Set the shared weather. No argument lists presets.", CmdWeather);
            Add("mass", "mass [mass] [name|*]", "Set an aircraft's mass in kg. Base is 1500. Default is you.", CmdMass);
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

        private string CmdMass(string[] args)
        {
            if (args == null || args.Length == 0 || !float.TryParse(args[0], out float mass))
                return "usage: mass [mass] [name|*]";
            
            string target = args.Length > 1 ? args[1] : "";
            if (!string.IsNullOrEmpty(target) && target != "*" && !AdminSession.AnyMatch(target))
                return "no aircraft named '" + target + "'";

            string error = AdminSession.Send(AdminCommand.Mass, target, mass);
            return string.IsNullOrEmpty(error) ? "mass " + mass.ToString("0.###") : error;
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
            const string usage = "usage: scale [scale] [name|*]  or  scale [x] [y] [z] [name|*]  or  scale x|y|z [value] ...";
            if (args == null || args.Length == 0)
                return usage;

            if (!TryParseScaleArgs(args, out Vector3 scale, out byte axes, out string target))
                return usage;

            if (!string.IsNullOrEmpty(target) && target != "*" && !AdminSession.AnyMatch(target))
                return "no aircraft named '" + target + "'";

            string error = AdminSession.SendScale(target, scale, axes);
            return string.IsNullOrEmpty(error) ? "scale " + FormatScale(scale, axes) : error;
        }

        private static bool TryParseScaleArgs(string[] args, out Vector3 scale, out byte axes, out string target)
        {
            scale = Vector3.one;
            axes = 0;
            target = "";

            if (float.TryParse(args[0], out float first))
            {
                if (args.Length >= 3
                    && float.TryParse(args[1], out float y)
                    && float.TryParse(args[2], out float z))
                {
                    if (args.Length > 4)
                        return false;
                    scale = ClampScale(new Vector3(first, y, z));
                    axes = 7;
                    target = args.Length > 3 ? args[3] : "";
                    return true;
                }

                if (args.Length > 2)
                    return false;
                scale = ClampScale(new Vector3(first, first, first));
                axes = 7;
                target = args.Length > 1 ? args[1] : "";
                return true;
            }

            int i = 0;
            while (i < args.Length)
            {
                if (!TryAxisIndex(args[i], out int axis))
                {
                    if (i != args.Length - 1 || axes == 0)
                        return false;
                    target = args[i];
                    return true;
                }

                if (i + 1 >= args.Length || !float.TryParse(args[i + 1], out float value))
                    return false;

                byte bit = (byte)(1 << axis);
                if ((axes & bit) != 0)
                    return false;

                axes |= bit;
                if (axis == 0)
                    scale.x = Mathf.Clamp(value, 0f, 50f);
                else if (axis == 1)
                    scale.y = Mathf.Clamp(value, 0f, 50f);
                else
                    scale.z = Mathf.Clamp(value, 0f, 50f);
                i += 2;
            }

            return axes != 0;
        }

        private static bool TryAxisIndex(string token, out int axis)
        {
            axis = -1;
            if (string.Equals(token, "x", StringComparison.OrdinalIgnoreCase))
            {
                axis = 0;
                return true;
            }

            if (string.Equals(token, "y", StringComparison.OrdinalIgnoreCase))
            {
                axis = 1;
                return true;
            }

            if (string.Equals(token, "z", StringComparison.OrdinalIgnoreCase))
            {
                axis = 2;
                return true;
            }

            return false;
        }

        private static Vector3 ClampScale(Vector3 scale)
        {
            return new Vector3(
                Mathf.Clamp(scale.x, 0f, 50f),
                Mathf.Clamp(scale.y, 0f, 50f),
                Mathf.Clamp(scale.z, 0f, 50f));
        }

        private static string FormatScale(Vector3 scale, byte axes)
        {
            if (axes == 7 && Mathf.Approximately(scale.x, scale.y) && Mathf.Approximately(scale.y, scale.z))
                return scale.x.ToString("0.###");

            var parts = new List<string>(3);
            if ((axes & 1) != 0)
                parts.Add("x " + scale.x.ToString("0.###"));
            if ((axes & 2) != 0)
                parts.Add("y " + scale.y.ToString("0.###"));
            if ((axes & 4) != 0)
                parts.Add("z " + scale.z.ToString("0.###"));
            return string.Join(" ", parts);
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
            RefreshLog();
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

        private void SetShown(GameObject target, bool shown)
        {
            if (!target || target == gameObject)
                return;
            if (target.activeSelf != shown)
                target.SetActive(shown);
        }

        private void SetInputText(string value)
        {
            _input = value ?? "";
            if (!inputField)
                return;
            inputField.SetTextWithoutNotify(_input);
            inputField.caretPosition = _input.Length;
            inputField.stringPosition = _input.Length;
        }

        private void ApplyContentType()
        {
            if (!inputField)
                return;

            TMP_InputField.ContentType next = _unlocked
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;
            if (inputField.contentType == next)
                return;

            inputField.contentType = next;
            inputField.ForceLabelUpdate();
        }

        private void RefreshHint()
        {
            if (!hintText)
                return;

            hintText.text = _unlocked
                ? "enter run   up/down history   tab complete   esc / ; close"
                : "enter submit   esc / ; close";
        }

        private void RefreshLog()
        {
            if (!logText)
                return;

            _logBuilder.Length = 0;
            for (int i = 0; i < _log.Count; i++)
            {
                if (i > 0)
                    _logBuilder.Append('\n');
                _logBuilder.Append(_log[i]);
            }

            logText.text = _logBuilder.ToString();
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
