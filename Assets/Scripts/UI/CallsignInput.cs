using System.Runtime.CompilerServices;
using Airplane.Multiplayer;
using UnityEngine;

namespace Airplane.UI
{
    public class CallsignInput : MonoBehaviour
    {
        public void SetCallsign(string input)
        {
            LocalPlayerIdentity.PilotName = input;
            Debug.Log($"Pilot name set to {LocalPlayerIdentity.PilotName}");
        }
    }
}
