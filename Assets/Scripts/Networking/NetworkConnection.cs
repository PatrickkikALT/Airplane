using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Airplane.Multiplayer
{
    public static class NetworkConnection
    {
        public static Action OnConnectFail;
        public static string Address { get; private set; } = "127.0.0.1";
        public static ushort Port { get; private set; } = 7777;
        public static string Status { get; private set; } = "Offline";

        private static string _bindAddress = "0.0.0.0";
        private static NetworkManager _hooked;

        public static bool StartHost(string address, ushort port, string bindAddress)
        {
            if (!Apply(address, port, bindAddress, true))
                return false;

            Status = Manager.StartHost() ? $"Hosting on {Port}" : "Failed to start host";
            return Manager.IsListening;
        }

        public static bool StartServer(string address, ushort port, string bindAddress)
        {
            if (!Apply(address, port, bindAddress, true))
                return false;

            Status = Manager.StartServer() ? $"Server on {Port}" : "Failed to start server";
            return Manager.IsListening;
        }

        public static bool StartClient(string address, ushort port, string bindAddress)
        {
            if (!Apply(address, port, bindAddress, false))
                return false;

            Status = Manager.StartClient() ? $"Connecting to {Address}:{Port}" : "Failed to start client";
            return Manager.IsListening;
        }

        public static void Disconnect()
        {
            NetworkManager manager = Manager;
            if (!manager || !manager.IsListening)
                return;

            manager.Shutdown();
            Status = "Offline";
            AdminSession.Reset();
        }

        private static bool Apply(string address, ushort port, string bindAddress, bool listening)
        {
            Hook();
            Address = string.IsNullOrWhiteSpace(address) ? "127.0.0.1" : address.Trim();
            Port = port;
            _bindAddress = string.IsNullOrWhiteSpace(bindAddress) ? "0.0.0.0" : bindAddress.Trim();
            NetworkManager manager = Manager;
            if (!manager)
            {
                Status = "No NetworkManager in the scene";
                OnConnectFail?.Invoke();
                return false;
            }

            if (manager.IsListening)
            {
                OnConnectFail?.Invoke();
                Status = "Already connected";
                return false;
            }

            UnityTransport transport = manager.GetComponent<UnityTransport>();
            if (!transport)
            {
                OnConnectFail?.Invoke();
                Status = "NetworkManager has no UnityTransport";
                return false;
            }

            if (listening)
                transport.SetConnectionData(Address, Port, _bindAddress);
            else
                transport.SetConnectionData(Address, Port);

            AircraftSelection.PrepareConnection(manager);
            return true;
        }

        private static void Hook()
        {
            NetworkManager manager = Manager;
            if (manager == _hooked)
                return;

            if (_hooked)
            {
                _hooked.OnClientConnectedCallback -= OnConnected;
                _hooked.OnClientDisconnectCallback -= OnDisconnected;
            }

            _hooked = manager;
            if (!manager)
                return;

            manager.OnClientConnectedCallback += OnConnected;
            manager.OnClientDisconnectCallback += OnDisconnected;
        }

        private static void OnConnected(ulong clientId)
        {
            NetworkManager manager = Manager;
            if (!manager || clientId != manager.LocalClientId)
                return;

            Status = manager.IsHost ? $"Hosting on {Port}" : $"Connected to {Address}:{Port}";
        }

        private static void OnDisconnected(ulong clientId)
        {
            NetworkManager manager = Manager;
            if (!manager || clientId != manager.LocalClientId)
                return;

            string reason = manager.DisconnectReason;
            Status = string.IsNullOrEmpty(reason) ? "Disconnected" : "Disconnected: " + reason;
            AdminSession.Reset();
        }

        private static NetworkManager Manager => NetworkManager.Singleton;
    }
}
