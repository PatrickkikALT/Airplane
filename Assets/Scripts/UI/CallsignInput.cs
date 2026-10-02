using System.Runtime.CompilerServices;
using Airplane.Multiplayer;
using TMPro;
using UnityEngine;

namespace Airplane.UI
{
    public class CallsignInput : MonoBehaviour
    {
        [SerializeField] private TMP_InputField inputField;

        public void OnEnable()
        {
            SetFieldToSavedCallsign();
        }
        
        private void SetFieldToSavedCallsign()
        {
            string callsign = PlayerPrefs.GetString("Callsign");
            inputField.SetTextWithoutNotify(callsign);
            LocalPlayerIdentity.PilotName = callsign;
        }
        
        public void SetCallsign(string input)
        {
            LocalPlayerIdentity.PilotName = input;
            PlayerPrefs.SetString("Callsign", input);
        }
    }
}
