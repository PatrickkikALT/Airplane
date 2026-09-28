using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

namespace Airplane.Multiplayer
{
    public static class AircraftSelection
    {
        private static readonly Dictionary<ulong, int> Chosen = new Dictionary<ulong, int>();

        public static AircraftCatalog Catalog { get; private set; }

        public static int LocalIndex => LocalPlayerIdentity.AircraftIndex;

        public static void Bind(AircraftCatalog catalog)
        {
            if (catalog)
                Catalog = catalog;
        }

        public static void SetLocalIndex(int index, int count)
        {
            if (count > 0)
            {
                index %= count;
                if (index < 0)
                    index += count;
            }
            else
            {
                index = Mathf.Max(0, index);
            }

            LocalPlayerIdentity.AircraftIndex = index;
        }

        public static void Remember(ulong clientId, int index)
        {
            Chosen[clientId] = Mathf.Max(0, index);
        }

        public static void Forget(ulong clientId)
        {
            Chosen.Remove(clientId);
        }

        public static void Clear()
        {
            Chosen.Clear();
        }

        public static int ForClient(ulong clientId)
        {
            if (Chosen.TryGetValue(clientId, out int index))
                return index;

            NetworkManager manager = NetworkManager.Singleton;
            if (manager && clientId == manager.LocalClientId)
                return LocalIndex;
            return 0;
        }

        public static void PrepareConnection(NetworkManager manager)
        {
            if (!manager || manager.IsListening)
                return;

            Catalog?.Register(manager);
            manager.NetworkConfig.ConnectionApproval = true;
            manager.NetworkConfig.ConnectionData = BitConverter.GetBytes(LocalIndex);
            manager.ConnectionApprovalCallback = Approve;
        }

        private static void Approve(NetworkManager.ConnectionApprovalRequest request, NetworkManager.ConnectionApprovalResponse response)
        {
            int index = 0;
            byte[] payload = request.Payload;
            if (payload != null && payload.Length >= sizeof(int))
                index = Mathf.Max(0, BitConverter.ToInt32(payload, 0));

            Remember(request.ClientNetworkId, index);
            response.Approved = true;
            response.CreatePlayerObject = false;
        }
    }
}
