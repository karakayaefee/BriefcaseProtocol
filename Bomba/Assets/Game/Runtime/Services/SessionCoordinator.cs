using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BriefcaseProtocol.Services
{
    [RequireComponent(typeof(NetworkManager))]
    public sealed class SessionCoordinator : MonoBehaviour
    {
        [SerializeField] private int maximumPlayers = 4;
        [SerializeField] private ushort localPort = 7777;

        public string ActiveSessionId { get; private set; } = string.Empty;
        public string JoinCode { get; private set; } = string.Empty;
        public bool IsBusy { get; private set; }
        public event Action<string> StatusChanged;
        public event Action<string> JoinCodeChanged;

        private NetworkManager networkManager;

        private void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
        }

        public async void CreatePrivateSession()
        {
            await CreatePrivateSessionAsync();
        }

        public async Task<bool> CreatePrivateSessionAsync()
        {
            if (IsBusy) return false;
            IsBusy = true;
            try
            {
                if (!await ProjectBootstrap.ServicesReadyTask)
                {
                    SetStatus("UGS unavailable. Use Local Host for development.");
                    return false;
                }

                var options = new SessionOptions
                {
                    MaxPlayers = maximumPlayers,
                    IsPrivate = true,
                    Name = "Briefcase Protocol Private Match"
                }.WithRelayNetwork();
                var session = await MultiplayerService.Instance.CreateSessionAsync(options);
                ActiveSessionId = session.Id;
                JoinCode = session.Code;
                JoinCodeChanged?.Invoke(JoinCode);
                SetStatus($"Session created: {JoinCode}");
                LoadLobby();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus(exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public async void JoinByCode(string code)
        {
            await JoinByCodeAsync(code);
        }

        public async Task<bool> JoinByCodeAsync(string code)
        {
            if (IsBusy || string.IsNullOrWhiteSpace(code)) return false;
            IsBusy = true;
            try
            {
                if (!await ProjectBootstrap.ServicesReadyTask)
                {
                    SetStatus("UGS unavailable.");
                    return false;
                }

                var session = await MultiplayerService.Instance.JoinSessionByCodeAsync(code.Trim().ToUpperInvariant());
                ActiveSessionId = session.Id;
                JoinCode = session.Code;
                JoinCodeChanged?.Invoke(JoinCode);
                SetStatus("Joined session.");
                LoadLobby();
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                SetStatus(exception.Message);
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        public void StartLocalHost()
        {
            ConfigureLocalTransport();
            if (networkManager.StartHost())
            {
                JoinCode = "LOCAL";
                JoinCodeChanged?.Invoke(JoinCode);
                SetStatus("Local host started.");
                LoadLobby();
            }
        }

        public void StartLocalClient()
        {
            ConfigureLocalTransport();
            if (networkManager.StartClient())
            {
                SetStatus("Connecting to local host.");
                LoadLobby();
            }
        }

        public void Shutdown()
        {
            if (networkManager.IsListening)
            {
                networkManager.Shutdown();
            }

            ActiveSessionId = string.Empty;
            JoinCode = string.Empty;
            JoinCodeChanged?.Invoke(string.Empty);
            SceneManager.LoadScene("MainMenu");
        }

        private void ConfigureLocalTransport()
        {
            if (networkManager.NetworkConfig.NetworkTransport is UnityTransport transport)
            {
                transport.SetConnectionData("127.0.0.1", localPort);
            }
        }

        private static void LoadLobby()
        {
            if (SceneManager.GetActiveScene().name != "Lobby")
            {
                SceneManager.LoadScene("Lobby");
            }
        }

        private void SetStatus(string status)
        {
            StatusChanged?.Invoke(status);
        }
    }
}
