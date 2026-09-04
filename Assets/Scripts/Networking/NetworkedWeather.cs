using Airplane.Extensions;
using Airplane.Weather;
using Unity.Netcode;

namespace Airplane.Multiplayer
{
    public class NetworkedWeather : SingletonNetworkBehaviour<NetworkedWeather>
    {
        [ClientRpc]
        public void UpdateWeatherClientRpc()
        {
            WeatherManager.Instance.UpdateWeatherInternal();
            
        }
        
    }
}