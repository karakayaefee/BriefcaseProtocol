using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace BriefcaseProtocol.UI
{
    /// <summary>
    /// Lobby sahnesindeki bekleme odası. Bağlı oyuncu sayısını gösterir ve
    /// yeterli oyuncu olunca yalnız host'a Başlat butonunu açar.
    /// Maç başladığında panel kendini gizler.
    /// UI'ı koddan kurar; ayrı bir UXML dosyası gerektirmez.
    /// </summary>
    public class LobbyWaitRoom : MonoBehaviour
    {
        [Tooltip("Maçın başlaması için gereken oyuncu sayısı.")]
        [SerializeField] int requiredPlayers = 4;

        [Tooltip("Yüklenecek oyun sahnesi. Build Settings'te olmalı.")]
        [SerializeField] string gameSceneName = "Game";

        [Tooltip("Açıkken oyuncu sayısı yetmese de Başlat aktif olur. Sadece test için.")]
        [SerializeField] bool allowStartBelowRequired = true;

        UIDocument document;
        Label statusLabel;
        Label phaseLabel;
        Button startButton;

        void Awake()
        {
            document = gameObject.GetComponent<UIDocument>();
            if (document == null) document = gameObject.AddComponent<UIDocument>();
        }

        void OnEnable()
        {
            BuildUI();
        }

        void BuildUI()
        {
            var root = document.rootVisualElement;
            if (root == null) return;

            root.Clear();

            var panel = new VisualElement();
            panel.style.position = Position.Absolute;
            panel.style.top = 20;
            panel.style.left = 20;
            panel.style.paddingTop = 12;
            panel.style.paddingBottom = 12;
            panel.style.paddingLeft = 16;
            panel.style.paddingRight = 16;
            panel.style.backgroundColor = new Color(0f, 0f, 0f, 0.72f);
            panel.style.minWidth = 260;

            var title = new Label("LOBI - OYUNCU BEKLENIYOR");
            title.style.color = Color.white;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8;
            panel.Add(title);

            statusLabel = new Label("Baglaniyor...");
            statusLabel.style.color = Color.white;
            statusLabel.style.marginBottom = 4;
            panel.Add(statusLabel);

            phaseLabel = new Label(string.Empty);
            phaseLabel.style.color = new Color(0.7f, 0.85f, 1f);
            phaseLabel.style.marginBottom = 8;
            panel.Add(phaseLabel);

            startButton = new Button(OnStartClicked) { text = "BASLAT" };
            startButton.style.display = DisplayStyle.None;
            panel.Add(startButton);

            root.Add(panel);
        }

        void Update()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || statusLabel == null) return;

            int connected = nm.IsServer
                ? nm.ConnectedClientsIds.Count
                : (nm.IsConnectedClient ? Mathf.Max(1, Core.NetworkPlayerState.All.Count) : 0);

            statusLabel.text = $"Oyuncu: {connected} / {requiredPlayers}";

            var match = Core.MatchManager.Instance;
            var round = Core.RoundManager.Instance;

            bool matchRunning = match != null && match.MatchInProgress.Value;

            if (matchRunning && round != null)
            {
                phaseLabel.text = round.HasTimer
                    ? $"{round.Phase.Value}  -  {Mathf.CeilToInt(round.RemainingSeconds)} sn"
                    : round.Phase.Value.ToString();
            }
            else
            {
                phaseLabel.text = nm.IsHost ? "Host sensin." : "Host'un baslatmasi bekleniyor.";
            }

            if (startButton == null) return;

            bool canShow = nm.IsServer && !matchRunning;
            bool enough = connected >= requiredPlayers || allowStartBelowRequired;

            startButton.style.display = canShow ? DisplayStyle.Flex : DisplayStyle.None;
            startButton.SetEnabled(enough);
        }

        void OnStartClicked()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            if (startButton != null) startButton.SetEnabled(false);

            var status = nm.SceneManager.LoadScene(gameSceneName, UnityEngine.SceneManagement.LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogError("[LobbyWaitRoom] '" + gameSceneName + "' yuklenemedi: " + status + ". Sahne Build Settings'te ve etkin mi?");
                if (startButton != null) startButton.SetEnabled(true);
                return;
            }

            Debug.Log("[LobbyWaitRoom] '" + gameSceneName + "' yukleniyor, mac orada baslayacak.");
        }


        /// <summary>Test için gereken oyuncu sayısı. MatchStarter bunu okur.</summary>
        public int RequiredPlayers => requiredPlayers;

    }
}
