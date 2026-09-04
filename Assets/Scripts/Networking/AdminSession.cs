using System;
using System.Collections.Generic;
using Airplane.Weather;
using Unity.Netcode;
using UnityEngine;

namespace Airplane.Multiplayer
{
    public enum AdminCommand : byte
    {
        Weather = 1,
        Destroy = 2,
        Heal = 3,
        Reload = 4,
        Scale = 5,
        Speed = 6,
        Timescale = 7,
        Bots = 8,
        Dummy = 9
    }

    /// <summary>
    /// Server-routed admin console. The console itself is a local IMGUI overlay, so every command
    /// that should be visible to other peers goes through a spawned <see cref="NetworkedAircraft"/>
    /// (any one will do) and lands here on the server.
    /// </summary>
    public static class AdminSession
    {
        private static string _weather = "";
        private static float _timescale = 1f;
        private static bool _hasWorldState;

        internal static bool IsListening
        {
            get
            {
                NetworkManager manager = NetworkManager.Singleton;
                return manager != null && manager.IsListening;
            }
        }

        internal static bool AnyMatch(string displayName)
        {
            IReadOnlyList<NetworkedAircraft> all = NetworkedAircraft.All;
            for (int i = 0; i < all.Count; i++)
            {
                NetworkedAircraft aircraft = all[i];
                if (aircraft && string.Equals(aircraft.DisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        internal static string Send(AdminCommand command, string target, float value)
        {
            if (!IsListening)
                return ExecuteLocal(command, target, value);

            if (NeedsAircraft(command)
                && string.IsNullOrEmpty(target)
                && NetworkedAircraft.Local == null)
                return "no local aircraft";

            NetworkedAircraft carrier = FindCarrier();
            if (!carrier)
                return "no networked aircraft to send from";

            carrier.RequestAdmin(command, target ?? "", value);
            return "";
        }

        private static bool NeedsAircraft(AdminCommand command)
        {
            switch (command)
            {
                case AdminCommand.Destroy:
                case AdminCommand.Heal:
                case AdminCommand.Reload:
                case AdminCommand.Scale:
                case AdminCommand.Speed:
                    return true;
                default:
                    return false;
            }
        }

        internal static void ExecuteOnServer(
            AdminCommand command,
            string target,
            float value,
            ulong senderClientId,
            NetworkedAircraft carrier)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsServer)
                return;

            switch (command)
            {
                case AdminCommand.Weather:
                    ApplyWeather(target, carrier);
                    return;
                case AdminCommand.Timescale:
                    ApplyTimescale(value, carrier);
                    return;
                case AdminCommand.Bots:
                    AircraftNetworkSpawner.Instance?.SetBotCount(Mathf.RoundToInt(value));
                    return;
                case AdminCommand.Dummy:
                    AircraftNetworkSpawner.Instance?.SpawnDummyFor(senderClientId);
                    return;
                case AdminCommand.Destroy:
                    DestroyTargets(target, senderClientId);
                    return;
                case AdminCommand.Scale:
                    ScaleTargets(target, senderClientId, value);
                    return;
                case AdminCommand.Heal:
                case AdminCommand.Reload:
                case AdminCommand.Speed:
                    ApplyOwnerCommand(command, target, senderClientId, value);
                    return;
            }
        }

        internal static void SyncToAircraft(NetworkedAircraft aircraft)
        {
            if (!aircraft || !_hasWorldState)
                return;

            aircraft.SyncWorldState(_weather, _timescale);
        }

        internal static void Reset()
        {
            _weather = "";
            _timescale = 1f;
            _hasWorldState = false;
            Time.timeScale = 1f;
        }

        private static void ApplyWeather(string name, NetworkedAircraft carrier)
        {
            WeatherManager manager = WeatherManager.Instance;
            if (manager == null || !manager.HasWeather(name) || !carrier)
                return;

            _weather = name;
            _hasWorldState = true;
            carrier.BroadcastWorldAdmin(AdminCommand.Weather, name, 0f);
        }

        private static void ApplyTimescale(float value, NetworkedAircraft carrier)
        {
            if (!carrier)
                return;

            _timescale = Mathf.Clamp(value, 0f, 8f);
            _hasWorldState = true;
            carrier.BroadcastWorldAdmin(AdminCommand.Timescale, "", _timescale);
        }

        private static void DestroyTargets(string target, ulong senderClientId)
        {
            List<NetworkedAircraft> targets = ResolveTargets(target, senderClientId);
            for (int i = 0; i < targets.Count; i++)
                targets[i].ForceDestroyFromServer();
        }

        private static void ScaleTargets(string target, ulong senderClientId, float scale)
        {
            List<NetworkedAircraft> targets = ResolveTargets(target, senderClientId);
            for (int i = 0; i < targets.Count; i++)
                targets[i].ServerSetScale(scale);
        }

        private static void ApplyOwnerCommand(
            AdminCommand command,
            string target,
            ulong senderClientId,
            float value)
        {
            List<NetworkedAircraft> targets = ResolveTargets(target, senderClientId);
            for (int i = 0; i < targets.Count; i++)
                targets[i].ApplyOwnerAdmin(command, value);
        }

        private static string ExecuteLocal(AdminCommand command, string target, float value)
        {
            switch (command)
            {
                case AdminCommand.Weather:
                    WeatherManager manager = WeatherManager.Instance;
                    if (manager == null)
                        return "no weather manager";
                    return manager.TrySetWeather(target) ? "" : "Invalid weather, run weather for possible weathers.";
                case AdminCommand.Timescale:
                    Time.timeScale = Mathf.Clamp(value, 0f, 8f);
                    return "";
                case AdminCommand.Destroy:
                    DestroyLocal(target);
                    return "";
                case AdminCommand.Heal:
                case AdminCommand.Reload:
                case AdminCommand.Speed:
                case AdminCommand.Scale:
                    ApplyLocalAircraft(command, target, value);
                    return "";
                case AdminCommand.Bots:
                    return "bots can only be changed on the server";
                case AdminCommand.Dummy:
                    return "dummy can only be spawned on the server";
                default:
                    return "";
            }
        }

        private static void DestroyLocal(string target)
        {
            List<NetworkedAircraft> targets = ResolveTargets(target, LocalSenderId());
            for (int i = 0; i < targets.Count; i++)
                targets[i].ReportCrash(Vector3.zero, 500f);
        }

        private static void ApplyLocalAircraft(AdminCommand command, string target, float value)
        {
            List<NetworkedAircraft> targets = ResolveTargets(target, LocalSenderId());
            for (int i = 0; i < targets.Count; i++)
            {
                NetworkedAircraft aircraft = targets[i];
                if (command == AdminCommand.Scale)
                {
                    float scale = Mathf.Clamp(value, 0f, 50f);
                    aircraft.transform.localScale = new Vector3(scale, scale, scale);
                    continue;
                }

                aircraft.ApplyOwnerAdminLocal(command, value);
            }
        }

        private static ulong LocalSenderId()
        {
            NetworkedAircraft local = NetworkedAircraft.Local;
            return local ? local.OwnerClientId : 0;
        }

        private static NetworkedAircraft FindCarrier()
        {
            NetworkedAircraft local = NetworkedAircraft.Local;
            if (local && local.IsSpawned)
                return local;

            IReadOnlyList<NetworkedAircraft> all = NetworkedAircraft.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] && all[i].IsSpawned)
                    return all[i];
            }

            return null;
        }

        private static List<NetworkedAircraft> ResolveTargets(string target, ulong senderClientId)
        {
            List<NetworkedAircraft> list = new List<NetworkedAircraft>();
            IReadOnlyList<NetworkedAircraft> all = NetworkedAircraft.All;
            bool named = !string.IsNullOrWhiteSpace(target);
            bool everyone = named && target == "*";

            for (int i = 0; i < all.Count; i++)
            {
                NetworkedAircraft aircraft = all[i];
                if (!aircraft || !aircraft.IsSpawned || !aircraft.IsAlive)
                    continue;

                if (!named)
                {
                    if (!aircraft.IsBot && aircraft.OwnerClientId == senderClientId)
                        list.Add(aircraft);
                    continue;
                }

                if (everyone
                    || string.Equals(aircraft.DisplayName, target, StringComparison.OrdinalIgnoreCase))
                    list.Add(aircraft);
            }

            return list;
        }
    }
}
