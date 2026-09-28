using UnityEngine;

namespace Airplane.Multiplayer
{
    using Unity.Netcode;
    using UnityEngine;

    public class NetworkManagerAutoShutdown : MonoBehaviour
    {
        private void OnEnable()
        {
            Application.quitting += OnApplicationQuitting;
        }

        private void OnDisable()
        {
            Application.quitting -= OnApplicationQuitting;
        }

        private void OnDestroy()
        {
            ShutdownNetwork();
        }

        private void OnApplicationQuitting()
        {
            ShutdownNetwork();
        }

        private void ShutdownNetwork()
        {
            NetworkManager manager = GetComponent<NetworkManager>();
            if (!manager || NetworkManager.Singleton != manager || !manager.IsListening)
                return;
            manager.Shutdown();
        }
    }
}