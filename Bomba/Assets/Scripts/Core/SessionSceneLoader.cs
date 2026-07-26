using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Core
{
    /// <summary>
    /// Oturum kurulup network başladığı anda host'u oyun sahnesine taşır.
    /// Client'lar NGO'nun scene management'ı sayesinde host'u otomatik takip eder,
    /// onlar için ek bir işlem gerekmez.
    ///
    /// MainMenu sahnesinde durur ve NetworkManager ile birlikte yaşar.
    /// </summary>
    public class SessionSceneLoader : MonoBehaviour
    {
        [Tooltip("Oturum kurulunca yüklenecek sahne. Build listesinde olmalı.")]
        [SerializeField] string gameSceneName = "Lobby";

        bool sceneRequested;

        void Start()
        {
            if (NetworkManager.Singleton == null)
            {
                Debug.LogError("[SessionSceneLoader] NetworkManager.Singleton yok. Bu script NetworkManager ile aynı sahnede mi?");
                return;
            }

            NetworkManager.Singleton.OnServerStarted += HandleServerStarted;
            NetworkManager.Singleton.OnClientStopped += HandleStopped;
        }

        void OnDestroy()
        {
            if (NetworkManager.Singleton == null) return;

            NetworkManager.Singleton.OnServerStarted -= HandleServerStarted;
            NetworkManager.Singleton.OnClientStopped -= HandleStopped;
        }

        void HandleServerStarted()
        {
            if (sceneRequested) return;

            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            if (!nm.NetworkConfig.EnableSceneManagement)
            {
                Debug.LogError("[SessionSceneLoader] EnableSceneManagement kapalı, sahne senkronize edilemez.");
                return;
            }

            sceneRequested = true;

            var status = nm.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                sceneRequested = false;
                Debug.LogError($"[SessionSceneLoader] '{gameSceneName}' yüklenemedi: {status}. Sahne Build Settings'te ve etkin mi?");
            }
        }

        void HandleStopped(bool wasHost)
        {
            // Oturumdan çıkıldı; tekrar kurulursa yeniden yüklenebilsin.
            sceneRequested = false;
        }
    }
}
