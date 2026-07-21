using System;
using System.Threading.Tasks;
using BriefcaseProtocol.Core;
using Unity.Services.Authentication;
using Unity.Services.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BriefcaseProtocol.Services
{
    public sealed class ProjectBootstrap : MonoBehaviour
    {
        public static ProjectBootstrap Instance { get; private set; }
        private static readonly TaskCompletionSource<bool> ServicesCompletion = new();

        [SerializeField] private bool initializeOnlineServices = true;
        [SerializeField] private bool useTurkishByDefault;

        public static Task<bool> ServicesReadyTask => ServicesCompletion.Task;
        public static bool ServicesAvailable { get; private set; }
        public static event Action<bool, string> ServicesStateChanged;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            Localizer.SetLanguage(useTurkishByDefault ? GameLanguage.Turkish : GameLanguage.English);
        }

        private async void Start()
        {
            if (!initializeOnlineServices)
            {
                CompleteServices(false, "Online services disabled in Bootstrap.");
                OpenMainMenu();
                return;
            }

            try
            {
                if (UnityServices.State == ServicesInitializationState.Uninitialized)
                {
                    await UnityServices.InitializeAsync();
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                CompleteServices(true, AuthenticationService.Instance.PlayerId);
                OpenMainMenu();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"UGS initialization failed; local networking remains available. {exception.Message}");
                CompleteServices(false, exception.Message);
                OpenMainMenu();
            }
        }

        private static void OpenMainMenu()
        {
            if (SceneManager.GetActiveScene().name == "Bootstrap")
            {
                SceneManager.LoadScene("MainMenu");
            }
        }

        private static void CompleteServices(bool available, string message)
        {
            ServicesAvailable = available;
            ServicesCompletion.TrySetResult(available);
            ServicesStateChanged?.Invoke(available, message);
        }
    }
}
