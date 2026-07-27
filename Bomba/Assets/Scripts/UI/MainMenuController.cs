using UnityEngine;
using UnityEngine.UIElements;

namespace BriefcaseProtocol.UI
{
    /// <summary>
    /// Ana menü panellerini yönetir: giriş ekranı, lobi kurma ve lobiye katılma.
    /// Session işlerini Blocks element'leri kendi içinde yapar; bu sınıf yalnız
    /// hangi panelin görüneceğine ve veri kaynağının bağlanmasına bakar.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MainMenuController : MonoBehaviour
    {
        [Tooltip("BriefcaseSessionSettings asset'i. Blocks element'lerinin binding kaynağı.")]
        [SerializeField] ScriptableObject sessionSettings;

        [Tooltip("Oturuma girildikten sonra yüklenecek sahnenin adı.")]
        [SerializeField] string gameSceneName = "Lobby";

        UIDocument document;
        VisualElement panelMain;
        VisualElement panelHost;
        VisualElement panelJoin;

        void OnEnable()
        {
            document = GetComponent<UIDocument>();

            var root = document.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[MainMenu] rootVisualElement yok. UIDocument'a UXML atanmış mı?");
                return;
            }

            // Blocks element'leri SessionSettings'i buradan miras alır.
            if (sessionSettings != null)
            {
                var menuRoot = root.Q<VisualElement>("menu-root") ?? root;
                menuRoot.dataSource = sessionSettings;
            }
            else
            {
                Debug.LogWarning("[MainMenu] sessionSettings atanmadı, session element'leri çalışmaz.");
            }

            panelMain = root.Q<VisualElement>("panel-main");
            panelHost = root.Q<VisualElement>("panel-host");
            panelJoin = root.Q<VisualElement>("panel-join");

            Bind(root, "btn-host", () => Show(panelHost));
            Bind(root, "btn-join", () => Show(panelJoin));
            Bind(root, "btn-back-host", () => Show(panelMain));
            Bind(root, "btn-back-join", () => Show(panelMain));
            Bind(root, "btn-quit", Quit);

            // Menüde fare imleci serbest olmalı.
            UnityEngine.Cursor.visible = true;
            UnityEngine.Cursor.lockState = CursorLockMode.None;

            Show(panelMain);
        }

        static void Bind(VisualElement root, string name, System.Action action)
        {
            var button = root.Q<Button>(name);
            if (button == null)
            {
                Debug.LogWarning($"[MainMenu] '{name}' butonu UXML'de bulunamadı.");
                return;
            }

            button.clicked += action;
        }

        void Show(VisualElement target)
        {
            SetVisible(panelMain, target == panelMain);
            SetVisible(panelHost, target == panelHost);
            SetVisible(panelJoin, target == panelJoin);
        }

        static void SetVisible(VisualElement element, bool visible)
        {
            if (element == null) return;
            element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Oyun sahnesinin adı. Oturum kurulduktan sonra sahne geçişini yapacak
        /// kod bunu kullanacak (host tarafında NetworkManager.SceneManager ile).
        /// </summary>
        public string GameSceneName => gameSceneName;
    }
}
