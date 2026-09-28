using Airplane.FlightSimulation;
using Unity.Netcode;
using UnityEngine;

namespace Airplane.Multiplayer
{
    [CreateAssetMenu(fileName = "AircraftCatalog", menuName = "Airplane/Aircraft Catalog")]
    public sealed class AircraftCatalog : ScriptableObject
    {
        [System.Serializable]
        public struct Entry
        {
            [Tooltip("Label shown in the aircraft selector. Uses the gameplay prefab name when empty.")]
            public string displayName;

            [Tooltip("Gameplay prefab spawned in a match. Root needs a PlaneRigidbody and a NetworkObject.")]
            public GameObject prefab;

            [Tooltip("Visual prefab shown in the hangar. No gameplay scripts.")]
            public GameObject previewPrefab;
        }

        [SerializeField] private Entry[] aircraft = System.Array.Empty<Entry>();

        public int Count
        {
            get
            {
                if (aircraft == null)
                    return 0;

                int count = 0;
                for (int i = 0; i < aircraft.Length; i++)
                {
                    if (IsPlayable(aircraft[i].prefab))
                        count++;
                }

                return count;
            }
        }

        public string GetDisplayName(int index)
        {
            if (!TryGet(index, out Entry entry))
                return "Aircraft";
            if (!string.IsNullOrWhiteSpace(entry.displayName))
                return entry.displayName.Trim();
            return entry.prefab ? entry.prefab.name : "Aircraft";
        }

        public GameObject GetPreviewPrefab(int index)
        {
            if (!TryGet(index, out Entry entry))
                return null;
            return entry.previewPrefab;
        }

        public GameObject GetPrefab(int index)
        {
            if (!TryGet(index, out Entry entry))
                return null;
            return entry.prefab;
        }

        public NetworkObject GetNetworkPrefab(int index)
        {
            GameObject prefab = GetPrefab(index);
            return prefab ? prefab.GetComponent<NetworkObject>() : null;
        }

        public void Register(NetworkManager manager)
        {
            if (!manager || manager.IsListening || manager.NetworkConfig == null || manager.NetworkConfig.Prefabs == null)
                return;
            if (aircraft == null)
                return;

            for (int i = 0; i < aircraft.Length; i++)
            {
                GameObject prefab = aircraft[i].prefab;
                if (!IsPlayable(prefab) || IsRegistered(manager, prefab))
                    continue;
                manager.AddNetworkPrefab(prefab);
            }
        }

        private static bool IsRegistered(NetworkManager manager, GameObject prefab)
        {
            if (manager.NetworkConfig.Prefabs.Contains(prefab))
                return true;

            var lists = manager.NetworkConfig.Prefabs.NetworkPrefabsLists;
            if (lists == null)
                return false;

            for (int i = 0; i < lists.Count; i++)
            {
                if (lists[i] && lists[i].Contains(prefab))
                    return true;
            }

            return false;
        }

        private bool TryGet(int index, out Entry entry)
        {
            entry = default;
            int count = Count;
            if (count == 0)
                return false;

            int wrapped = index % count;
            if (wrapped < 0)
                wrapped += count;

            int seen = 0;
            for (int i = 0; i < aircraft.Length; i++)
            {
                if (!IsPlayable(aircraft[i].prefab))
                    continue;
                if (seen == wrapped)
                {
                    entry = aircraft[i];
                    return true;
                }

                seen++;
            }

            return false;
        }

        private static bool IsPlayable(GameObject prefab)
        {
            return prefab
                   && prefab.GetComponent<PlaneRigidbody>()
                   && prefab.GetComponent<NetworkObject>();
        }

        private void OnValidate()
        {
            if (aircraft == null)
                return;

            for (int i = 0; i < aircraft.Length; i++)
            {
                GameObject prefab = aircraft[i].prefab;
                GameObject preview = aircraft[i].previewPrefab;
                if (prefab)
                {
                    if (!prefab.GetComponent<PlaneRigidbody>())
                        Debug.LogError($"Aircraft catalog entry '{prefab.name}' needs a PlaneRigidbody on the prefab root.", prefab);
                    if (!prefab.GetComponent<NetworkObject>())
                        Debug.LogError($"Aircraft catalog entry '{prefab.name}' needs a NetworkObject on the prefab root.", prefab);
                }

                if (!preview)
                    continue;
                if (preview == prefab)
                    Debug.LogError($"Aircraft catalog preview for '{preview.name}' must be a separate prefab from the gameplay one.", preview);
                if (preview.GetComponent<NetworkObject>())
                    Debug.LogError($"Aircraft catalog preview '{preview.name}' should not have a NetworkObject.", preview);
                if (preview.GetComponent<PlaneRigidbody>())
                    Debug.LogError($"Aircraft catalog preview '{preview.name}' should not have a PlaneRigidbody.", preview);
            }
        }
    }
}
