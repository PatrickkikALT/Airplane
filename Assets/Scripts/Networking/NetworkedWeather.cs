using Airplane.Extensions;
using Airplane.Weather;
using Unity.Collections;
using Unity.Netcode;

namespace Airplane.Multiplayer
{
    public class NetworkedWeather : SingletonNetworkBehaviour<NetworkedWeather>
    {
        private FixedString64Bytes _weatherName;
        private float _blendSeconds;
        private bool _hasWeather;

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();
            if (IsServer)
                return;

            RequestWeatherRpc();
        }

        public void Publish(string name, float blendSeconds)
        {
            if (!IsSpawned || !IsServer || string.IsNullOrWhiteSpace(name))
                return;

            _weatherName = ToFixed(name);
            _blendSeconds = blendSeconds;
            _hasWeather = true;
            ApplyWeatherRpc(_weatherName, blendSeconds, RpcTarget.NotServer);
        }

        [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestWeatherRpc(RpcParams rpcParams = default)
        {
            if (!_hasWeather)
                return;

            float remaining = _blendSeconds;
            WeatherManager manager = WeatherManager.Instance;
            if (manager)
                remaining = manager.RemainingBlendSeconds;

            ApplyWeatherRpc(
                _weatherName,
                remaining,
                RpcTarget.Single(rpcParams.Receive.SenderClientId, RpcTargetUse.Temp));
        }

        [Rpc(SendTo.SpecifiedInParams, InvokePermission = RpcInvokePermission.Server)]
        private void ApplyWeatherRpc(FixedString64Bytes weatherName, float blendSeconds, RpcParams rpcParams = default)
        {
            WeatherManager.Instance?.ApplyReplicated(weatherName.ToString(), blendSeconds);
        }

        private static FixedString64Bytes ToFixed(string value)
        {
            if (value.Length > 60)
                value = value.Substring(0, 60);
            return new FixedString64Bytes(value);
        }
    }
}
