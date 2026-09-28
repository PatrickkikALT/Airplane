using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Airplane.Multiplayer
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Airplane/Networking/Main Menu Session UI")]
    public sealed class MainMenuSessionUi : MonoBehaviour
    {
        [Header("Connection")]
        [SerializeField] private string address = "127.0.0.1";
        [SerializeField] private ushort port = 7777;

        [Tooltip("Address the server binds to. 0.0.0.0 accepts connections on every interface.")]
        [SerializeField] private string serverBindAddress = "0.0.0.0";

        [Header("Scene")]
        [Tooltip("Loaded after Host, Join, or Server succeeds. Must be in Build Settings.")]
        [SerializeField] private string gameScene = "Patrick";

        [Header("UI")]
        [SerializeField] private TMP_InputField addressInput;
        [SerializeField] private TMP_InputField portInput;
        [SerializeField] private Button hostButton;
        [SerializeField] private Button joinButton;
        [SerializeField] private Toggle serverToggle;
        [SerializeField] private TMP_Text statusText;

        private bool _seeded;
        private string _fault;
        private bool _shouldStartAsServer;

        private void OnEnable()
        {
            Wire(true);
            NetworkConnection.OnConnectFail += () =>
            {
                Set(statusText, string.IsNullOrEmpty(_fault) ? NetworkConnection.Status : _fault);
            };
            SeedFields();
        }

        private void OnDisable()
        {
            Wire(false);
        }

        public void StartHost()
        {
            if (_shouldStartAsServer)
            {
                StartServer();
                return;
            }
            PullFields();
            if (NetworkConnection.StartHost(address, port, serverBindAddress))
                LoadGame();
        }

        public void StartServer()
        {
            PullFields();
            if (NetworkConnection.StartServer(address, port, serverBindAddress))
                LoadGame();
        }

        public void StartClient()
        {
            PullFields();
            if (NetworkConnection.StartClient(address, port, serverBindAddress))
                LoadGame();
        }

        private void LoadGame()
        {
            if (string.IsNullOrWhiteSpace(gameScene))
                return;

            if (!Application.CanStreamedLevelBeLoaded(gameScene))
            {
                _fault = "Scene not in Build Settings: " + gameScene;
                return;
            }

            SceneManager.LoadScene(gameScene);
        }

        public void Quit()
        {
            Application.Quit();
        }

        private void SeedFields()
        {
            if (_seeded)
                return;
            _seeded = true;

            if (addressInput)
                addressInput.SetTextWithoutNotify(address);
            if (portInput)
                portInput.SetTextWithoutNotify(port.ToString());
        }

        private void PullFields()
        {
            if (addressInput && !string.IsNullOrWhiteSpace(addressInput.text))
                address = addressInput.text.Trim();
            if (portInput && ushort.TryParse(portInput.text, out ushort parsed))
                port = parsed;
        }
        

        private void Wire(bool subscribe)
        {
            if (subscribe)
            {
                if (hostButton) hostButton.onClick.AddListener(StartHost);
                if (joinButton) joinButton.onClick.AddListener(StartClient);
                if (serverToggle) serverToggle.onValueChanged.AddListener((value) => _shouldStartAsServer = value);
                return;
            }

            if (hostButton) hostButton.onClick.RemoveListener(StartHost);
            if (joinButton) joinButton.onClick.RemoveListener(StartClient);
            if (serverToggle) serverToggle.onValueChanged.RemoveAllListeners();
        }

        private static void Set(TMP_Text text, string value)
        {
            if (!text || text.text == value)
                return;
            text.text = value;
        }
    }
}
