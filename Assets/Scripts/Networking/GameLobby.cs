using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using UnityEngine;

namespace Airplane.Multiplayer
{
    public static class GameLobby
    {
        public const int DefaultMaxPlayers = 8;
        public const string CallsignKey = "callsign";

        public static event Action Changed;

        public static ISession Current { get; private set; }
        public static bool Busy { get; private set; }
        public static IReadOnlyList<ISessionInfo> OpenSessions => _open;

        private static readonly List<ISessionInfo> _open = new List<ISessionInfo>();
        private static Task _ready;
        private static int _token;

        public static async Task<bool> Create(string name, int maxPlayers, bool isPrivate)
        {
            return await Run(async () =>
            {
                await EnsureReady();
                PrepareNetcode();
                SessionOptions options = new SessionOptions
                {
                    Name = CleanName(name),
                    MaxPlayers = Mathf.Clamp(maxPlayers, 1, 100),
                    IsPrivate = isPrivate,
                    PlayerProperties = PlayerData()
                }.WithRelayNetwork();

                IHostSession session = await MultiplayerService.Instance.CreateSessionAsync(options);
                Bind(session);
                return Listening();
            });
        }

        public static async Task<bool> JoinCode(string code)
        {
            code = string.IsNullOrWhiteSpace(code) ? "" : code.Trim();
            if (code.Length == 0)
            {
                NetworkConnection.Report("Enter a join code");
                Changed?.Invoke();
                return false;
            }

            return await Run(async () =>
            {
                await EnsureReady();
                PrepareNetcode();
                ISession session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code, JoinData());
                Bind(session);
                return Listening();
            });
        }

        public static async Task<bool> JoinId(string sessionId)
        {
            if (string.IsNullOrWhiteSpace(sessionId))
                return false;

            return await Run(async () =>
            {
                await EnsureReady();
                PrepareNetcode();
                ISession session = await MultiplayerService.Instance.JoinSessionByIdAsync(sessionId, JoinData());
                Bind(session);
                return Listening();
            });
        }

        public static async Task Refresh()
        {
            if (Busy || Current != null)
                return;

            int token = _token;
            try
            {
                await EnsureReady();
                if (token != _token || Current != null)
                    return;

                QuerySessionsResults results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions
                {
                    Count = 20
                });

                if (token != _token || Current != null)
                    return;

                _open.Clear();
                if (results?.Sessions != null)
                {
                    for (int i = 0; i < results.Sessions.Count; i++)
                    {
                        ISessionInfo info = results.Sessions[i];
                        if (info == null || info.IsLocked || info.HasPassword || info.AvailableSlots <= 0)
                            continue;
                        _open.Add(info);
                    }
                }

                if (string.IsNullOrEmpty(NetworkConnection.Status) || NetworkConnection.Status == "Offline" || NetworkConnection.Status.StartsWith("Open", StringComparison.Ordinal))
                    NetworkConnection.Report(_open.Count == 0 ? "No open lobbies" : "Open lobbies: " + _open.Count);
            }
            catch (Exception exception)
            {
                if (token != _token)
                    return;
                _open.Clear();
                NetworkConnection.Report(Fail(exception));
            }

            Changed?.Invoke();
        }

        public static async Task<bool> Lock()
        {
            ISession session = Current;
            if (session == null || !session.IsHost)
                return Listening();

            try
            {
                IHostSession host = session.AsHost();
                host.IsLocked = true;
                await host.SavePropertiesAsync();
            }
            catch (Exception exception)
            {
                NetworkConnection.Report(Fail(exception));
                Changed?.Invoke();
                return false;
            }

            return Listening();
        }

        public static async Task Leave()
        {
            int token = ++_token;
            ISession session = Detach();
            Busy = false;
            if (session == null)
            {
                NetworkConnection.ShutdownManager();
                NetworkConnection.Report("Offline");
                Changed?.Invoke();
                return;
            }

            try
            {
                await session.LeaveAsync();
            }
            catch (Exception)
            {
                if (token == _token)
                    NetworkConnection.ShutdownManager();
            }

            if (token != _token)
                return;

            NetworkConnection.Report("Offline");
            AdminSession.Reset();
            Changed?.Invoke();
        }

        public static string CallsignOf(IReadOnlyPlayer player)
        {
            if (player?.Properties != null &&
                player.Properties.TryGetValue(CallsignKey, out PlayerProperty property) &&
                !string.IsNullOrWhiteSpace(property.Value))
                return property.Value;

            string serviceName = player != null ? player.GetPlayerName() : null;
            return string.IsNullOrWhiteSpace(serviceName) ? "Pilot" : serviceName;
        }

        public static string Describe()
        {
            ISession session = Current;
            if (session == null)
                return NetworkConnection.Status;

            string role = session.IsHost ? "Host" : "Guest";
            return role + "  ·  " + session.Name + "  ·  " + session.Code + "  ·  " + session.PlayerCount + "/" + session.MaxPlayers;
        }

        private static async Task<bool> Run(Func<Task<bool>> action)
        {
            if (Busy || Current != null)
                return false;

            int token = ++_token;
            Busy = true;
            Changed?.Invoke();
            try
            {
                bool ok = await action();
                if (token != _token)
                    return false;
                if (!ok)
                    await DropFailedSession();
                NetworkConnection.Report(ok ? Describe() : "Failed to start the network");
                return ok;
            }
            catch (Exception exception)
            {
                if (token != _token)
                    return false;
                Detach();
                NetworkConnection.Report(Fail(exception));
                return false;
            }
            finally
            {
                if (token == _token)
                {
                    Busy = false;
                    Changed?.Invoke();
                }
            }
        }

        private static Task EnsureReady()
        {
            if (_ready == null || _ready.IsFaulted || _ready.IsCanceled)
                _ready = Ready();
            return _ready;
        }

        private static async Task Ready()
        {
            await Task.Yield();

            if (UnityServices.State == ServicesInitializationState.Uninitialized)
            {
                InitializationOptions options = new InitializationOptions();
                options.SetProfile("p" + (Application.dataPath.GetHashCode() & 0x7fffffff).ToString("x"));
                await UnityServices.InitializeAsync(options);
            }

            while (UnityServices.State == ServicesInitializationState.Initializing)
                await Task.Yield();

            if (UnityServices.State != ServicesInitializationState.Initialized)
                throw new InvalidOperationException("Unity Gaming Services did not initialize. Link the project and enable Lobby and Relay.");

            IAuthenticationService auth = AuthenticationService.Instance;
            if (!auth.IsSignedIn)
                await auth.SignInAnonymouslyAsync();
        }

        private static void PrepareNetcode()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager && manager.NetworkConfig != null && !manager.IsListening)
                manager.NetworkConfig.PlayerPrefab = null;
            AircraftSelection.PrepareConnection(manager);
        }

        private static Dictionary<string, PlayerProperty> PlayerData()
        {
            return new Dictionary<string, PlayerProperty>
            {
                { CallsignKey, new PlayerProperty(LocalPlayerIdentity.PilotName, VisibilityPropertyOptions.Member) }
            };
        }

        private static JoinSessionOptions JoinData()
        {
            return new JoinSessionOptions
            {
                PlayerProperties = PlayerData()
            };
        }

        private static async Task DropFailedSession()
        {
            ISession session = Detach();
            if (session == null)
            {
                NetworkConnection.ShutdownManager();
                return;
            }

            try
            {
                await session.LeaveAsync();
            }
            catch (Exception)
            {
                NetworkConnection.ShutdownManager();
            }
        }

        private static void Bind(ISession session)
        {
            Detach();
            Current = session;
            if (session == null)
                return;

            session.Changed += OnChanged;
            session.Deleted += OnEnded;
            session.RemovedFromSession += OnEnded;
            session.PlayerJoined += OnPlayer;
            session.PlayerHasLeft += OnPlayer;
            NetworkConnection.Report(Describe());
        }

        private static ISession Detach()
        {
            ISession session = Current;
            Current = null;
            if (session == null)
                return null;

            session.Changed -= OnChanged;
            session.Deleted -= OnEnded;
            session.RemovedFromSession -= OnEnded;
            session.PlayerJoined -= OnPlayer;
            session.PlayerHasLeft -= OnPlayer;
            return session;
        }

        private static void OnChanged()
        {
            if (Current != null)
                NetworkConnection.Report(Describe());
            Changed?.Invoke();
        }

        private static void OnPlayer(string _)
        {
            OnChanged();
        }

        private static void OnEnded()
        {
            Detach();
            NetworkConnection.Report("Lobby closed");
            AdminSession.Reset();
            Changed?.Invoke();
        }

        private static bool Listening()
        {
            NetworkManager manager = NetworkManager.Singleton;
            return manager && manager.IsListening;
        }

        private static string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = LocalPlayerIdentity.PilotName;
            if (string.IsNullOrWhiteSpace(name))
                name = "Flight";
            name = name.Trim();
            if (name.Length > 32)
                name = name.Substring(0, 32);
            return name;
        }

        private static string Fail(Exception exception)
        {
            string message = exception.Message;
            if (string.IsNullOrWhiteSpace(message))
                message = "Lobby request failed";
            if (message.IndexOf("project", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("initialize", StringComparison.OrdinalIgnoreCase) >= 0)
                return message;
            return message;
        }
    }
}
