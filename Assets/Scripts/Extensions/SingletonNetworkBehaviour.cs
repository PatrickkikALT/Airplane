using Unity.Netcode;
using UnityEngine;

namespace Airplane.Extensions
{
    public class SingletonNetworkBehaviour<T> : NetworkBehaviour where T : NetworkBehaviour
    {
        private static T instance = null;

        public static T Instance
        {
            get
            {
                if (!instance)
                    instance = FindAnyObjectByType<T>();
                return instance;
            }
        }

        protected virtual void Awake()
        {
            if (instance != null)
                Debug.LogWarning($"Instance is already set {instance}, overwriting it with {this}");
            instance = this as T;
        }

        protected virtual void OnDestroy()
        {
            if (instance == this as T)
                instance = null;
        }
    }
}