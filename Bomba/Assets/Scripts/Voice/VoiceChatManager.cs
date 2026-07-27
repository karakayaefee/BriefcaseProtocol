using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BriefcaseProtocol.Core;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BriefcaseProtocol.Voice
{
    /// <summary>
    /// Vivox sesli sohbet yaşam döngüsünü Multiplayer Session ve NGO sahneleriyle eşler.
    /// Lobby'de ortak, Game'de takım başına ayrı bir 2D ses kanalı kullanır.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceChatManager : MonoBehaviour
    {
        const string LobbySceneName = "Lobby";
        const string GameSceneName = "Game";
        const float ReconcileIntervalSeconds = 0.5f;
        const float RetryDelaySeconds = 15f;

        static VoiceChatManager instance;

        readonly HashSet<VivoxParticipant> observedParticipants = new HashSet<VivoxParticipant>();

        bool lifecycleBusy;
        bool vivoxInitialized;
        bool eventsSubscribed;
        bool shuttingDown;
        bool pushToTalkActive;
        float nextReconcileTime;
        float retryAfterTime;
        string currentChannel;
        string currentChannelLabel;
        string statusText = "Ses: oturum bekleniyor";

        public static VoiceChatManager Instance => instance;
        public string CurrentChannel => currentChannel;
        public string CurrentChannelLabel => currentChannelLabel;
        public string StatusText => statusText;
        public bool IsPushToTalkActive => pushToTalkActive;
        public bool IsReady => vivoxInitialized && VivoxService.Instance.IsLoggedIn && !string.IsNullOrEmpty(currentChannel);

        public event Action StateChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void CreatePersistentInstance()
        {
            if (instance != null || Application.isBatchMode) return;

            var voiceObject = new GameObject("VoiceChatManager");
            voiceObject.AddComponent<VoiceChatManager>();
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        void Update()
        {
            if (shuttingDown || Application.isBatchMode) return;

            UpdatePushToTalk();
            UpdateOutputMuteShortcut();

            if (!lifecycleBusy && Time.unscaledTime >= nextReconcileTime)
            {
                nextReconcileTime = Time.unscaledTime + ReconcileIntervalSeconds;
                ReconcileVoiceLifecycleAsync();
            }
        }

        void OnGUI()
        {
            if (Application.isBatchMode || !IsVoiceScene()) return;

            DrawVoiceOverlay();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) SetPushToTalk(false);
        }

        void OnApplicationQuit()
        {
            shuttingDown = true;
            SetPushToTalk(false);

            if (vivoxInitialized && VivoxService.Instance.IsLoggedIn)
            {
                _ = ShutdownVivoxAsync();
            }
        }

        void OnDestroy()
        {
            if (instance != this) return;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeVivoxEvents();
            instance = null;
        }

        void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            nextReconcileTime = 0f;
            RaiseStateChanged();
        }

        async void ReconcileVoiceLifecycleAsync()
        {
            lifecycleBusy = true;

            try
            {
                if (!TryGetTargetChannel(out string targetChannel, out string targetLabel))
                {
                    await LeaveCurrentChannelAsync();
                    SetStatus(IsVoiceScene() ? "Ses: oturum veya takım bekleniyor" : "Ses: kapalı");
                    return;
                }

                if (!vivoxInitialized || !VivoxService.Instance.IsLoggedIn)
                {
                    if (Time.unscaledTime < retryAfterTime) return;
                    await InitializeVivoxAsync();
                }

                if (string.Equals(currentChannel, targetChannel, StringComparison.Ordinal))
                {
                    currentChannelLabel = targetLabel;
                    SetStatus("Ses: " + targetLabel);
                    return;
                }

                await SwitchChannelAsync(targetChannel, targetLabel);
            }
            catch (Exception exception)
            {
                retryAfterTime = Time.unscaledTime + RetryDelaySeconds;
                SetPushToTalk(false);
                SetStatus("Ses bağlantı hatası: " + FriendlyError(exception));
                Debug.LogWarning("[VoiceChat] " + exception);
            }
            finally
            {
                lifecycleBusy = false;
            }
        }

        async Task InitializeVivoxAsync()
        {
            SetStatus("Ses: mikrofon izni bekleniyor");
            bool hasMicrophonePermission = await EnsureMicrophonePermissionAsync();
            if (!hasMicrophonePermission)
            {
                throw new InvalidOperationException("Mikrofon izni verilmedi");
            }

            SetStatus("Ses: servisler hazırlanıyor");
            while (UnityServices.State == ServicesInitializationState.Initializing)
            {
                await Task.Yield();
            }

            if (UnityServices.State != ServicesInitializationState.Initialized)
            {
                await UnityServices.InitializeAsync();
            }

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            }

            if (!vivoxInitialized)
            {
                try
                {
                    await VivoxService.Instance.InitializeAsync();
                }
                catch (NullReferenceException exception)
                {
                    throw new InvalidOperationException(
                        "Vivox kimlik bilgileri eksik. Dashboard'da Vivox'u etkinleştirip " +
                        "Unity > Project Settings > Services > Vivox > Environment: Automatic seçin.",
                        exception);
                }

                vivoxInitialized = true;
                SubscribeVivoxEvents();
            }

            if (!VivoxService.Instance.IsLoggedIn)
            {
                var loginOptions = new LoginOptions
                {
                    DisplayName = ResolveLocalDisplayName(),
                    ParticipantUpdateFrequency = ParticipantPropertyUpdateFrequency.StateChange
                };

                await VivoxService.Instance.LoginAsync(loginOptions);
            }

            // Bas-konuş varsayılanı: hiçbir tuşa basılmıyorken mikrofon kapalıdır.
            VivoxService.Instance.MuteInputDevice();
            pushToTalkActive = false;
        }

        async Task SwitchChannelAsync(string targetChannel, string targetLabel)
        {
            await LeaveCurrentChannelAsync();

            SetStatus("Ses: " + targetLabel + " kanalına bağlanıyor");
            await VivoxService.Instance.JoinGroupChannelAsync(
                targetChannel,
                ChatCapability.AudioOnly,
                new ChannelOptions { MakeActiveChannelUponJoining = true });

            await VivoxService.Instance.SetChannelTransmissionModeAsync(TransmissionMode.Single, targetChannel);

            currentChannel = targetChannel;
            currentChannelLabel = targetLabel;
            VivoxService.Instance.MuteInputDevice();
            pushToTalkActive = false;
            ObserveCurrentParticipants();
            SetStatus("Ses: " + targetLabel);

            Debug.Log("[VoiceChat] Kanala bağlanıldı: " + targetChannel);
        }

        async Task LeaveCurrentChannelAsync()
        {
            if (string.IsNullOrEmpty(currentChannel)) return;

            string channelToLeave = currentChannel;
            SetPushToTalk(false);

            if (vivoxInitialized && VivoxService.Instance.IsLoggedIn &&
                VivoxService.Instance.ActiveChannels.ContainsKey(channelToLeave))
            {
                await VivoxService.Instance.LeaveChannelAsync(channelToLeave);
            }

            if (string.Equals(currentChannel, channelToLeave, StringComparison.Ordinal))
            {
                currentChannel = null;
                currentChannelLabel = null;
            }

            ClearObservedParticipants();
            Debug.Log("[VoiceChat] Kanaldan çıkıldı: " + channelToLeave);
            RaiseStateChanged();
        }

        async Task ShutdownVivoxAsync()
        {
            try
            {
                await VivoxService.Instance.LeaveAllChannelsAsync();
                await VivoxService.Instance.LogoutAsync();
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[VoiceChat] Kapanış sırasında hata: " + exception.Message);
            }
        }

        static async Task<bool> EnsureMicrophonePermissionAsync()
        {
            if (Application.HasUserAuthorization(UserAuthorization.Microphone)) return true;

            AsyncOperation request = Application.RequestUserAuthorization(UserAuthorization.Microphone);
            while (!request.isDone)
            {
                await Task.Yield();
            }

            return Application.HasUserAuthorization(UserAuthorization.Microphone);
        }

        void UpdatePushToTalk()
        {
            bool shouldTransmit = IsReady && Application.isFocused &&
                                  Keyboard.current != null && Keyboard.current.vKey.isPressed;
            SetPushToTalk(shouldTransmit);
        }

        void SetPushToTalk(bool active)
        {
            if (pushToTalkActive == active) return;

            pushToTalkActive = active;
            if (vivoxInitialized && VivoxService.Instance.IsLoggedIn)
            {
                if (active && !string.IsNullOrEmpty(currentChannel))
                {
                    VivoxService.Instance.UnmuteInputDevice();
                }
                else
                {
                    VivoxService.Instance.MuteInputDevice();
                }
            }

            RaiseStateChanged();
        }

        void UpdateOutputMuteShortcut()
        {
            if (!vivoxInitialized || !VivoxService.Instance.IsLoggedIn || Keyboard.current == null) return;
            if (!Keyboard.current.mKey.wasPressedThisFrame) return;

            ToggleAllIncomingVoice();
        }

        public void ToggleAllIncomingVoice()
        {
            if (!vivoxInitialized || !VivoxService.Instance.IsLoggedIn) return;

            if (VivoxService.Instance.IsOutputDeviceMuted)
            {
                VivoxService.Instance.UnmuteOutputDevice();
            }
            else
            {
                VivoxService.Instance.MuteOutputDevice();
            }

            RaiseStateChanged();
        }

        public void ToggleParticipantMute(VivoxParticipant participant)
        {
            if (participant == null || participant.IsSelf) return;

            if (participant.IsMuted)
            {
                participant.UnmutePlayerLocally();
            }
            else
            {
                participant.MutePlayerLocally();
            }

            RaiseStateChanged();
        }

        bool TryGetTargetChannel(out string channelName, out string channelLabel)
        {
            channelName = null;
            channelLabel = null;

            string sceneName = SceneManager.GetActiveScene().name;
            if (sceneName != LobbySceneName && sceneName != GameSceneName) return false;

            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager == null || !networkManager.IsListening || !networkManager.IsClient) return false;
            if (!TryGetCurrentSession(out ISession session)) return false;

            string sessionIdentifier = !string.IsNullOrWhiteSpace(session.Id) ? session.Id : session.Code;
            if (string.IsNullOrWhiteSpace(sessionIdentifier)) return false;

            string channelPrefix = "bp-" + SanitizeChannelPart(sessionIdentifier);
            if (sceneName == LobbySceneName)
            {
                channelName = channelPrefix + "-lobby";
                channelLabel = "Lobi";
                return true;
            }

            NetworkPlayerState localPlayer = NetworkPlayerState.Find(networkManager.LocalClientId);
            if (localPlayer == null || localPlayer.Team.Value == TeamId.None) return false;

            if (localPlayer.Team.Value == TeamId.TeamA)
            {
                channelName = channelPrefix + "-team-a";
                channelLabel = "Takım A";
                return true;
            }

            if (localPlayer.Team.Value == TeamId.TeamB)
            {
                channelName = channelPrefix + "-team-b";
                channelLabel = "Takım B";
                return true;
            }

            return false;
        }

        static bool TryGetCurrentSession(out ISession currentSession)
        {
            currentSession = null;

            try
            {
                var sessions = MultiplayerService.Instance?.Sessions;
                if (sessions == null) return false;

                foreach (var pair in sessions)
                {
                    ISession session = pair.Value;
                    if (session == null || !session.IsMember) continue;

                    currentSession = session;
                    return true;
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[VoiceChat] Aktif oturum okunamadı: " + exception.Message);
            }

            return false;
        }

        string ResolveLocalDisplayName()
        {
            NetworkManager networkManager = NetworkManager.Singleton;
            if (networkManager != null)
            {
                NetworkPlayerState localPlayer = NetworkPlayerState.Find(networkManager.LocalClientId);
                if (localPlayer != null && !localPlayer.DisplayName.Value.IsEmpty)
                {
                    string networkName = localPlayer.DisplayName.Value.ToString().Trim();
                    if (!string.IsNullOrEmpty(networkName)) return LimitDisplayName(networkName);
                }
            }

            string playerId = AuthenticationService.Instance.PlayerId;
            if (string.IsNullOrEmpty(playerId)) return "Oyuncu";

            string suffix = playerId.Length > 6 ? playerId.Substring(playerId.Length - 6) : playerId;
            return "Oyuncu-" + suffix;
        }

        static string LimitDisplayName(string displayName)
        {
            const int maxLength = 50;
            return displayName.Length <= maxLength ? displayName : displayName.Substring(0, maxLength);
        }

        static string SanitizeChannelPart(string value)
        {
            var builder = new StringBuilder(Math.Min(value.Length, 80));
            for (int i = 0; i < value.Length && builder.Length < 80; i++)
            {
                char character = char.ToLowerInvariant(value[i]);
                if (char.IsLetterOrDigit(character) || character == '-')
                {
                    builder.Append(character);
                }
                else
                {
                    builder.Append('-');
                }
            }

            return builder.Length > 0 ? builder.ToString() : "session";
        }

        static string FriendlyError(Exception exception)
        {
            string message = exception.GetBaseException().Message;
            if (string.IsNullOrWhiteSpace(message)) return exception.GetType().Name;
            return message.Length <= 120 ? message : message.Substring(0, 120) + "…";
        }

        bool IsVoiceScene()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            return sceneName == LobbySceneName || sceneName == GameSceneName;
        }

        void SubscribeVivoxEvents()
        {
            if (eventsSubscribed) return;

            VivoxService.Instance.ChannelJoined += HandleChannelJoined;
            VivoxService.Instance.ChannelLeft += HandleChannelLeft;
            VivoxService.Instance.LoggedOut += HandleLoggedOut;
            VivoxService.Instance.ParticipantAddedToChannel += HandleParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel += HandleParticipantRemoved;
            eventsSubscribed = true;
        }

        void UnsubscribeVivoxEvents()
        {
            if (!eventsSubscribed) return;

            VivoxService.Instance.ChannelJoined -= HandleChannelJoined;
            VivoxService.Instance.ChannelLeft -= HandleChannelLeft;
            VivoxService.Instance.LoggedOut -= HandleLoggedOut;
            VivoxService.Instance.ParticipantAddedToChannel -= HandleParticipantAdded;
            VivoxService.Instance.ParticipantRemovedFromChannel -= HandleParticipantRemoved;
            ClearObservedParticipants();
            eventsSubscribed = false;
        }

        void HandleChannelJoined(string channelName)
        {
            if (!string.Equals(channelName, currentChannel, StringComparison.Ordinal) && string.IsNullOrEmpty(currentChannel))
            {
                currentChannel = channelName;
            }

            ObserveCurrentParticipants();
            RaiseStateChanged();
        }

        void HandleChannelLeft(string channelName)
        {
            if (string.Equals(channelName, currentChannel, StringComparison.Ordinal))
            {
                currentChannel = null;
                currentChannelLabel = null;
            }

            ClearObservedParticipants();
            RaiseStateChanged();
        }

        void HandleLoggedOut()
        {
            currentChannel = null;
            currentChannelLabel = null;
            pushToTalkActive = false;
            ClearObservedParticipants();
            RaiseStateChanged();
        }

        void HandleParticipantAdded(VivoxParticipant participant)
        {
            ObserveParticipant(participant);
            RaiseStateChanged();
        }

        void HandleParticipantRemoved(VivoxParticipant participant)
        {
            StopObservingParticipant(participant);
            RaiseStateChanged();
        }

        void ObserveCurrentParticipants()
        {
            if (string.IsNullOrEmpty(currentChannel) ||
                !VivoxService.Instance.ActiveChannels.TryGetValue(currentChannel, out var participants)) return;

            for (int i = 0; i < participants.Count; i++)
            {
                ObserveParticipant(participants[i]);
            }
        }

        void ObserveParticipant(VivoxParticipant participant)
        {
            if (participant == null || !observedParticipants.Add(participant)) return;

            participant.ParticipantSpeechDetected += RaiseStateChanged;
            participant.ParticipantMuteStateChanged += RaiseStateChanged;
        }

        void StopObservingParticipant(VivoxParticipant participant)
        {
            if (participant == null || !observedParticipants.Remove(participant)) return;

            participant.ParticipantSpeechDetected -= RaiseStateChanged;
            participant.ParticipantMuteStateChanged -= RaiseStateChanged;
        }

        void ClearObservedParticipants()
        {
            foreach (VivoxParticipant participant in observedParticipants)
            {
                if (participant == null) continue;
                participant.ParticipantSpeechDetected -= RaiseStateChanged;
                participant.ParticipantMuteStateChanged -= RaiseStateChanged;
            }

            observedParticipants.Clear();
        }

        List<VivoxParticipant> GetParticipantsSnapshot()
        {
            var snapshot = new List<VivoxParticipant>();
            if (!vivoxInitialized || string.IsNullOrEmpty(currentChannel) ||
                !VivoxService.Instance.ActiveChannels.TryGetValue(currentChannel, out var participants)) return snapshot;

            for (int i = 0; i < participants.Count; i++)
            {
                if (participants[i] != null) snapshot.Add(participants[i]);
            }

            return snapshot;
        }

        void DrawVoiceOverlay()
        {
            List<VivoxParticipant> participants = GetParticipantsSnapshot();
            int remoteParticipantCount = 0;
            for (int i = 0; i < participants.Count; i++)
            {
                if (!participants[i].IsSelf) remoteParticipantCount++;
            }

            float panelHeight = 130f + remoteParticipantCount * 30f;
            var panelRect = new Rect(Screen.width - 330f, 12f, 318f, panelHeight);

            GUILayout.BeginArea(panelRect, GUIContent.none, GUI.skin.box);
            GUILayout.Label(statusText);

            if (IsReady)
            {
                GUILayout.Label(pushToTalkActive ? "V: KONUŞUYORSUN" : "V basılı tut: konuş");
                bool outputMuted = VivoxService.Instance.IsOutputDeviceMuted;
                if (GUILayout.Button(outputMuted ? "M: Tüm sesleri aç" : "M: Tüm sesleri kapat"))
                {
                    ToggleAllIncomingVoice();
                }
            }

            for (int i = 0; i < participants.Count; i++)
            {
                VivoxParticipant participant = participants[i];
                if (participant.IsSelf) continue;

                GUILayout.BeginHorizontal();
                string speakingMarker = participant.SpeechDetected ? "● " : "○ ";
                GUILayout.Label(speakingMarker + participant.DisplayName, GUILayout.ExpandWidth(true));
                if (GUILayout.Button(participant.IsMuted ? "Sesi aç" : "Sustur", GUILayout.Width(72f)))
                {
                    ToggleParticipantMute(participant);
                }
                GUILayout.EndHorizontal();
            }

            GUILayout.EndArea();
        }

        void SetStatus(string value)
        {
            if (statusText == value) return;
            statusText = value;
            RaiseStateChanged();
        }

        void RaiseStateChanged()
        {
            StateChanged?.Invoke();
        }
    }
}
