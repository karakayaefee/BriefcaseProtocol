using System;
using System.Collections.Generic;
using System.Linq;
using BriefcaseProtocol.Core;
using BriefcaseProtocol.Gameplay.Briefcases;
using BriefcaseProtocol.Gameplay.Player;
using BriefcaseProtocol.Gameplay.Setup;
using BriefcaseProtocol.Gameplay.Traps;
using BriefcaseProtocol.Networking;
using BriefcaseProtocol.Services;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BriefcaseProtocol.UI
{
    public sealed class PrototypeUIController : MonoBehaviour
    {
        [SerializeField] private SessionCoordinator sessions;
        [SerializeField] private VivoxVoiceService voice;
        [SerializeField] private bool visible = true;

        private readonly Queue<string> eventFeed = new();
        private string displayName = "Player";
        private string joinCode = string.Empty;
        private string codeEntry = string.Empty;
        private string interactionPrompt = string.Empty;
        private BriefcaseController activeBriefcase;
        private bool manualOpen;
        private bool ready;
        private Vector2 eventScroll;

        private GUIStyle titleStyle;
        private GUIStyle panelStyle;
        private GUIStyle labelStyle;

        private void Awake()
        {
            if (sessions == null) sessions = GetComponent<SessionCoordinator>();
            if (voice == null) voice = GetComponent<VivoxVoiceService>();
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            BriefcaseController.CodeEntryRequested += OpenCodeEntry;
            NetworkPlayerController.InteractionPromptChanged += SetPrompt;
            NetworkPlayerController.ManualToggleRequested += ToggleManual;
            if (sessions != null) sessions.JoinCodeChanged += SetJoinCode;
            AttachMatchEvents();
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            BriefcaseController.CodeEntryRequested -= OpenCodeEntry;
            NetworkPlayerController.InteractionPromptChanged -= SetPrompt;
            NetworkPlayerController.ManualToggleRequested -= ToggleManual;
            if (sessions != null) sessions.JoinCodeChanged -= SetJoinCode;
            DetachMatchEvents();
        }

        private void Update()
        {
            if (KeyboardShortcutPressed()) visible = !visible;
        }

        private void OnGUI()
        {
            if (!visible) return;
            EnsureStyles();
            var scene = SceneManager.GetActiveScene().name;
            if (scene is "Bootstrap" or "MainMenu") DrawMainMenu();
            else if (scene == "Lobby") DrawLobby();
            else if (scene == "Game") DrawGame();

            if (manualOpen) DrawManual();
            if (activeBriefcase != null) DrawCodeEntry();
        }

        private void DrawMainMenu()
        {
            GUILayout.BeginArea(new Rect(40, 40, 440, 620), panelStyle);
            GUILayout.Label("BRIEFCASE PROTOCOL", titleStyle);
            GUILayout.Label("2v2 deception prototype", labelStyle);
            Space();
            GUILayout.Label("Display name");
            displayName = GUILayout.TextField(displayName, 24);
            Space();
            GUI.enabled = sessions != null && !sessions.IsBusy;
            if (GUILayout.Button(Localizer.Get("menu.host"), GUILayout.Height(42)))
            {
                sessions.CreatePrivateSession();
            }
            GUILayout.Label("Join code");
            joinCode = GUILayout.TextField(joinCode, 12).ToUpperInvariant();
            if (GUILayout.Button(Localizer.Get("menu.join"), GUILayout.Height(42)))
            {
                sessions.JoinByCode(joinCode);
            }
            Space();
            if (GUILayout.Button("LOCAL HOST (DEV)", GUILayout.Height(34))) sessions.StartLocalHost();
            if (GUILayout.Button("LOCAL CLIENT (DEV)", GUILayout.Height(34))) sessions.StartLocalClient();
            GUI.enabled = true;
            Space();
            if (GUILayout.Button(Localizer.Current == GameLanguage.English ? "TÜRKÇE" : "ENGLISH"))
            {
                Localizer.SetLanguage(Localizer.Current == GameLanguage.English ? GameLanguage.Turkish : GameLanguage.English);
            }
            GUILayout.Label("F10: prototype UI on/off");
            GUILayout.EndArea();
        }

        private void DrawLobby()
        {
            var match = NetworkMatchManager.Instance;
            GUILayout.BeginArea(new Rect(30, 30, 520, Screen.height - 60), panelStyle);
            GUILayout.Label("PRIVATE LOBBY", titleStyle);
            GUILayout.Label($"Code: {(sessions != null ? sessions.JoinCode : joinCode)}");
            if (match == null || !match.IsSpawned)
            {
                GUILayout.Label("Waiting for NetworkMatchManager...");
                GUILayout.EndArea();
                return;
            }

            foreach (var player in match.Players)
            {
                GUILayout.Label($"{player.DisplayName} | {player.Team} {player.Slot} | {(player.Ready ? "READY" : "WAITING")}");
            }

            Space();
            GUILayout.Label("Choose a unique slot:");
            GUILayout.BeginHorizontal();
            SlotButton(match, TeamId.Red, RoleSlot.Operator);
            SlotButton(match, TeamId.Red, RoleSlot.Support);
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            SlotButton(match, TeamId.Blue, RoleSlot.Operator);
            SlotButton(match, TeamId.Blue, RoleSlot.Support);
            GUILayout.EndHorizontal();
            if (GUILayout.Button(ready ? "NOT READY" : Localizer.Get("lobby.ready"), GUILayout.Height(42)))
            {
                ready = !ready;
                match.SetReadyServerRpc(ready);
                if (ready && voice != null) _ = voice.JoinMatchChannelAsync(sessions != null ? sessions.JoinCode : "local", displayName);
            }
            GUILayout.EndArea();
        }

        private void DrawGame()
        {
            var match = NetworkMatchManager.Instance;
            if (match == null) return;
            var state = match.State;
            var remaining = Math.Max(0d, state.PhaseEndsAt - match.ServerNow);

            GUILayout.BeginArea(new Rect(Screen.width / 2f - 210, 15, 420, 115), panelStyle);
            GUILayout.Label($"ROUND {state.RoundIndex + 1}  •  {Localizer.Get($"phase.{state.Phase}")}", titleStyle);
            GUILayout.Label($"{Math.Floor(remaining / 60):00}:{Math.Floor(remaining % 60):00}   STRIKES {state.Strikes}/{match.Balance.strikeLimit}", labelStyle);
            GUILayout.EndArea();

            GUILayout.BeginArea(new Rect(20, 20, 310, 220), panelStyle);
            GUILayout.Label($"Builders: {state.BuilderTeam}");
            GUILayout.Label($"Solvers: {state.SolverTeam}");
            GUILayout.Label($"Role: {LocalRole(match)}");
            GUILayout.Label(Localizer.Get("voice.push"));
            if (!string.IsNullOrEmpty(interactionPrompt)) GUILayout.Label(Localizer.Get(interactionPrompt));
            GUILayout.EndArea();

            if (state.Phase == MatchPhase.Setup) DrawShop(match);
            if (state.Phase == MatchPhase.Operation && LocalRole(match) == GameplayRole.Trapper) DrawRemoteTraps();
            DrawEventFeed();
        }

        private void DrawShop(NetworkMatchManager match)
        {
            var budget = NetworkBudgetManager.Instance;
            if (budget == null) return;
            var role = LocalRole(match);
            GUILayout.BeginArea(new Rect(20, 260, 310, 420), panelStyle);
            GUILayout.Label($"BOMB {budget.BombRemaining}  |  DECOY {budget.DecoyRemaining}", titleStyle);
            if (role == GameplayRole.BombMaker)
            {
                PurchaseButton(budget, ShopItemKind.WireModule);
                PurchaseButton(budget, ShopItemKind.SequenceModule);
            }
            else if (role == GameplayRole.Trapper)
            {
                PurchaseButton(budget, ShopItemKind.FakeBriefcase);
                PurchaseButton(budget, ShopItemKind.FakeWirePanel);
                PurchaseButton(budget, ShopItemKind.FakeKeypad);
                PurchaseButton(budget, ShopItemKind.FakeLed);
                PurchaseButton(budget, ShopItemKind.FakeTimer);
                PurchaseButton(budget, ShopItemKind.WeakSignal);
                PurchaseButton(budget, ShopItemKind.SoundLure);
                PurchaseButton(budget, ShopItemKind.ControlledDoor);
            }
            else
            {
                GUILayout.Label("Solvers wait in staging during setup.");
            }
            GUILayout.EndArea();
        }

        private static void PurchaseButton(NetworkBudgetManager budget, ShopItemKind item)
        {
            if (GUILayout.Button($"{item} ({NetworkBudgetManager.CostFor(item, NetworkMatchManager.Instance.Balance)})"))
            {
                budget.RequestPurchase(item);
            }
        }

        private void DrawRemoteTraps()
        {
            GUILayout.BeginArea(new Rect(20, 260, 310, 180), panelStyle);
            GUILayout.Label("REMOTE TRAPS", titleStyle);
            var sound = FindAnyObjectByType<SoundLureTrap>();
            var door = FindAnyObjectByType<ControlledDoorTrap>();
            if (sound != null && GUILayout.Button($"SOUND LURE ({sound.Charges})")) sound.RequestTrigger();
            if (door != null && GUILayout.Button($"CONTROLLED DOOR ({door.Charges})")) door.RequestTrigger();
            GUILayout.EndArea();
        }

        private void DrawEventFeed()
        {
            GUILayout.BeginArea(new Rect(Screen.width - 350, 20, 330, 270), panelStyle);
            GUILayout.Label("RECENT EVENTS", titleStyle);
            eventScroll = GUILayout.BeginScrollView(eventScroll);
            foreach (var item in eventFeed) GUILayout.Label(item);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawManual()
        {
            GUILayout.BeginArea(new Rect(Screen.width / 2f - 330, Screen.height / 2f - 250, 660, 500), panelStyle);
            GUILayout.Label("FIELD MANUAL", titleStyle);
            GUILayout.Label(Localizer.Get("manual.color.title"), titleStyle);
            GUILayout.Label(Localizer.Get("manual.color.body"), labelStyle);
            Space();
            GUILayout.Label(Localizer.Get("manual.serial.title"), titleStyle);
            GUILayout.Label(Localizer.Get("manual.serial.body"), labelStyle);
            Space();
            if (GUILayout.Button("CLOSE / KAPAT")) manualOpen = false;
            GUILayout.EndArea();
        }

        private void DrawCodeEntry()
        {
            GUILayout.BeginArea(new Rect(Screen.width / 2f - 180, Screen.height / 2f - 130, 360, 260), panelStyle);
            GUILayout.Label($"BRIEFCASE {activeBriefcase.Label}", titleStyle);
            GUILayout.Label($"Clue: {activeBriefcase.Clue}");
            codeEntry = GUILayout.TextField(codeEntry, 3);
            if (GUILayout.Button("SUBMIT", GUILayout.Height(40)) && int.TryParse(codeEntry, out var code))
            {
                activeBriefcase.SubmitCode(code);
                activeBriefcase = null;
                codeEntry = string.Empty;
            }
            if (GUILayout.Button("CANCEL")) activeBriefcase = null;
            GUILayout.EndArea();
        }

        private void SlotButton(NetworkMatchManager match, TeamId team, RoleSlot slot)
        {
            if (GUILayout.Button($"{team} {slot}", GUILayout.Height(38)))
            {
                match.SelectLobbySlotServerRpc(team, slot, new FixedString64Bytes(displayName));
                ready = false;
            }
        }

        private GameplayRole LocalRole(NetworkMatchManager match)
        {
            if (NetworkManager.Singleton == null) return GameplayRole.None;
            var localId = NetworkManager.Singleton.LocalClientId;
            foreach (var player in match.Players)
            {
                if (player.ClientId == localId) return player.RoleFor(match.State.BuilderTeam);
            }
            return GameplayRole.None;
        }

        private void OnMatchEvent(MatchEventData data)
        {
            eventFeed.Enqueue($"{data.Type}: {data.Subject} {data.Value}");
            while (eventFeed.Count > 5) eventFeed.Dequeue();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            AttachMatchEvents();
        }

        private void AttachMatchEvents()
        {
            if (NetworkMatchManager.Instance == null) return;
            NetworkMatchManager.Instance.MatchEventReceived -= OnMatchEvent;
            NetworkMatchManager.Instance.MatchEventReceived += OnMatchEvent;
        }

        private void DetachMatchEvents()
        {
            if (NetworkMatchManager.Instance != null) NetworkMatchManager.Instance.MatchEventReceived -= OnMatchEvent;
        }

        private void OpenCodeEntry(BriefcaseController briefcase)
        {
            activeBriefcase = briefcase;
            codeEntry = string.Empty;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void ToggleManual()
        {
            manualOpen = !manualOpen;
            Cursor.lockState = manualOpen ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = manualOpen;
        }

        private void SetPrompt(string key) => interactionPrompt = key;
        private void SetJoinCode(string value) => joinCode = value;

        private static bool KeyboardShortcutPressed()
        {
            return UnityEngine.InputSystem.Keyboard.current?.f10Key.wasPressedThisFrame == true;
        }

        private void EnsureStyles()
        {
            if (titleStyle != null) return;
            titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, wordWrap = true };
            labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 16, wordWrap = true };
            panelStyle = new GUIStyle(GUI.skin.box) { padding = new RectOffset(16, 16, 16, 16) };
        }

        private static void Space() => GUILayout.Space(16);
    }
}
