using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BriefcaseProtocol.Core;
using Epic.OnlineServices;
using Epic.OnlineServices.Connect;
using Epic.OnlineServices.Lobby;
using Epic.OnlineServices.RTC;
using Epic.OnlineServices.RTCAudio;
using PlayEveryWare.EpicOnlineServices;
using Unity.Netcode;
using Unity.Services.Authentication;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace BriefcaseProtocol.Voice
{
    /// <summary>
    /// EOS Connect ve EOS Lobby RTC tabanli sesli sohbet yasam dongusu.
    /// Lobby sahnesinde ortak, Game sahnesinde takim basina ayri bir RTC odasi kullanir.
    /// Oyun trafigi Unity Netcode/Relay uzerinden devam eder; EOS yalnizca sesi tasir.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VoiceChatManager : MonoBehaviour
    {
        const string LobbySceneName = "Lobby";
        const string GameSceneName = "Game";
        const string VoiceLobbyBucket = "briefcase-protocol-voice";
        const string DisplayNameAttribute = "BP_NAME";
        const uint VoiceLobbyCapacity = 16;
        const float ReconcileIntervalSeconds = 0.5f;
        const float RetryDelaySeconds = 5f;
        const int OperationTimeoutMilliseconds = 15000;

        static VoiceChatManager instance;

        readonly Dictionary<string, VoiceParticipant> participants = new Dictionary<string, VoiceParticipant>();
        readonly HashSet<string> manuallyMutedParticipants = new HashSet<string>();
        readonly Dictionary<string, bool> requestedReceivingState = new Dictionary<string, bool>();

        bool lifecycleBusy;
        bool eosInitialized;
        bool shuttingDown;
        bool pushToTalkActive;
        bool allIncomingMuted;
        bool rtcConnected;
        bool? requestedSendingState;
        float nextReconcileTime;
        float retryAfterTime;
        string currentChannel;
        string currentChannelLabel;
        string currentVoiceLobbyId;
        string currentRtcRoomName;
        string publishedDisplayName;
        string statusText = "Ses: oturum bekleniyor";
        ProductUserId localProductUserId;

        ulong lobbyMemberUpdateNotification;
        ulong lobbyMemberStatusNotification;
        ulong rtcConnectionNotification;
        ulong rtcParticipantStatusNotification;
        ulong rtcParticipantAudioNotification;

        public static VoiceChatManager Instance => instance;
        public string CurrentChannel => currentChannel;
        public string CurrentChannelLabel => currentChannelLabel;
        public string StatusText => statusText;
        public bool IsPushToTalkActive => pushToTalkActive;
        public bool IsReady => eosInitialized && localProductUserId != null && localProductUserId.IsValid() &&
                               !string.IsNullOrEmpty(currentVoiceLobbyId) &&
                               !string.IsNullOrEmpty(currentRtcRoomName) && rtcConnected;

        public event Action StateChanged;

        sealed class VoiceParticipant
        {
            public ProductUserId ProductUserId;
            public string Id;
            public string DisplayName;
            public bool IsInRoom;
            public bool IsSpeaking;
        }

        readonly struct VoiceTarget
        {
            public VoiceTarget(string channelName, string channelLabel)
            {
                ChannelName = channelName;
                ChannelLabel = channelLabel;
                VoiceLobbyId = BuildDeterministicLobbyId(channelName);
            }

            public string ChannelName { get; }
            public string ChannelLabel { get; }
            public string VoiceLobbyId { get; }
        }

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
            ForceDisableSending();
            BeginLeaveWithoutWaiting();
        }

        void OnDestroy()
        {
            if (instance != this) return;

            SceneManager.sceneLoaded -= HandleSceneLoaded;
            UnsubscribeRtcNotifications();
            UnsubscribeLobbyNotifications();
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
                if (!TryGetTarget(out VoiceTarget target))
                {
                    await LeaveCurrentVoiceLobbyAsync();
                    SetStatus(IsVoiceScene() ? "Ses: oturum veya takim bekleniyor" : "Ses: kapali");
                    return;
                }

                if (!eosInitialized)
                {
                    if (Time.unscaledTime < retryAfterTime) return;
                    await InitializeEOSAsync();
                }

                if (!string.Equals(currentChannel, target.ChannelName, StringComparison.Ordinal))
                {
                    await LeaveCurrentVoiceLobbyAsync();
                    await JoinOrCreateVoiceLobbyAsync(target);
                }
                else
                {
                    currentChannelLabel = target.ChannelLabel;
                    string displayName = ResolveLocalDisplayName();
                    if (!string.Equals(displayName, publishedDisplayName, StringComparison.Ordinal))
                    {
                        await PublishDisplayNameAsync(displayName);
                    }

                    RefreshParticipants();
                    ApplyReceivingPreferences();
                }

                SetStatus(rtcConnected
                    ? "Ses: " + target.ChannelLabel
                    : "Ses: " + target.ChannelLabel + " RTC baglantisi bekleniyor");
            }
            catch (Exception exception)
            {
                retryAfterTime = Time.unscaledTime + RetryDelaySeconds;
                SetPushToTalk(false);
                SetStatus("Ses baglanti hatasi: " + FriendlyError(exception));
                Debug.LogWarning("[VoiceChat/EOS] " + exception);
            }
            finally
            {
                lifecycleBusy = false;
            }
        }

        async Task InitializeEOSAsync()
        {
            SetStatus("Ses: mikrofon izni bekleniyor");
            if (!await EnsureMicrophonePermissionAsync())
            {
                throw new InvalidOperationException("Mikrofon izni verilmedi");
            }

            SetStatus("Ses: EOS hazirlaniyor");
            EnsureEOSManagerExists();
            await Task.Yield();

            ConnectInterface connectInterface;
            try
            {
                connectInterface = EOSManager.Instance.GetEOSConnectInterface();
            }
            catch (Exception exception)
            {
                throw MissingConfigurationException(exception);
            }

            if (connectInterface == null)
            {
                throw MissingConfigurationException(null);
            }

            if (EOSManager.Instance.HasLoggedInWithConnect())
            {
                localProductUserId = EOSManager.Instance.GetProductUserId();
            }
            else
            {
                LoginCallbackInfo loginInfo = await LoginWithDeviceIdAsync();
                if (DeviceIdCredentialsAreMissing(loginInfo))
                {
                    await CreateDeviceIdAsync(connectInterface);
                    loginInfo = await LoginWithDeviceIdAsync();
                }

                if (loginInfo.ResultCode == Result.InvalidUser && loginInfo.ContinuanceToken != null)
                {
                    var createUserCompletion = new TaskCompletionSource<CreateUserCallbackInfo>();
                    EOSManager.Instance.CreateConnectUserWithContinuanceToken(
                        loginInfo.ContinuanceToken,
                        data => createUserCompletion.TrySetResult(data));

                    CreateUserCallbackInfo createUserInfo = await AwaitWithTimeout(
                        createUserCompletion.Task,
                        "EOS Connect kullanicisi olusturma");
                    if (createUserInfo.ResultCode != Result.Success)
                    {
                        throw new InvalidOperationException("EOS Connect kullanicisi olusturulamadi: " + createUserInfo.ResultCode);
                    }
                }
                else if (loginInfo.ResultCode != Result.Success)
                {
                    throw new InvalidOperationException("EOS Connect girisi basarisiz: " + loginInfo.ResultCode);
                }

                localProductUserId = EOSManager.Instance.GetProductUserId();
            }

            if (localProductUserId == null || !localProductUserId.IsValid())
            {
                throw new InvalidOperationException("EOS Connect gecerli bir Product User ID dondurmedi");
            }

            eosInitialized = true;
            SubscribeLobbyNotifications();
        }

        async Task<LoginCallbackInfo> LoginWithDeviceIdAsync()
        {
            var completion = new TaskCompletionSource<LoginCallbackInfo>();
            EOSManager.Instance.StartConnectLoginWithDeviceToken(
                ResolveLocalDisplayName(),
                data => completion.TrySetResult(data));
            return await AwaitWithTimeout(completion.Task, "EOS Connect girisi");
        }

        static bool DeviceIdCredentialsAreMissing(LoginCallbackInfo loginInfo)
        {
            return loginInfo.ResultCode == Result.NotFound ||
                   loginInfo.ResultCode == Result.InvalidAuth ||
                   (loginInfo.ResultCode == Result.InvalidUser && loginInfo.ContinuanceToken == null);
        }

        static async Task CreateDeviceIdAsync(ConnectInterface connectInterface)
        {
            var options = new CreateDeviceIdOptions
            {
                DeviceModel = LimitUtf8(SystemInfo.deviceModel, 64, "WindowsPC")
            };
            var completion = new TaskCompletionSource<Result>();
            connectInterface.CreateDeviceId(
                ref options,
                null,
                (ref CreateDeviceIdCallbackInfo data) => completion.TrySetResult(data.ResultCode));

            Result result = await AwaitWithTimeout(completion.Task, "EOS Device ID olusturma");
            if (result != Result.Success && result != Result.DuplicateNotAllowed)
            {
                throw new InvalidOperationException("EOS Device ID olusturulamadi: " + result);
            }
        }

        static void EnsureEOSManagerExists()
        {
            if (UnityEngine.Object.FindAnyObjectByType<EOSManager>() != null) return;

            try
            {
                var eosObject = new GameObject("EOSManager");
                eosObject.AddComponent<EOSManager>();
            }
            catch (Exception exception)
            {
                throw MissingConfigurationException(exception);
            }
        }

        static InvalidOperationException MissingConfigurationException(Exception innerException)
        {
            return new InvalidOperationException(
                "EOS ayarlari eksik. Unity'de EOS Plugin > EOS Configuration ekranini doldurun.",
                innerException);
        }

        async Task JoinOrCreateVoiceLobbyAsync(VoiceTarget target)
        {
            currentChannel = target.ChannelName;
            currentChannelLabel = target.ChannelLabel;

            SetStatus("Ses: " + target.ChannelLabel + " EOS lobisine baglaniyor");
            JoinLobbyByIdCallbackInfo joinInfo = await JoinVoiceLobbyByIdAsync(target.VoiceLobbyId);
            if (joinInfo.ResultCode == Result.Success)
            {
                await FinishVoiceLobbyJoinAsync((string)joinInfo.LobbyId, target);
                return;
            }

            if (joinInfo.ResultCode != Result.NotFound)
            {
                ClearCurrentChannelState();
                throw new InvalidOperationException("EOS ses lobisine katilinamadi: " + joinInfo.ResultCode);
            }

            CreateLobbyCallbackInfo createInfo = await CreateVoiceLobbyAsync(target.VoiceLobbyId);
            if (createInfo.ResultCode == Result.Success)
            {
                await FinishVoiceLobbyJoinAsync((string)createInfo.LobbyId, target);
                return;
            }

            if (createInfo.ResultCode == Result.LobbyLobbyAlreadyExists)
            {
                await Task.Delay(250);
                joinInfo = await JoinVoiceLobbyByIdAsync(target.VoiceLobbyId);
                if (joinInfo.ResultCode == Result.Success)
                {
                    await FinishVoiceLobbyJoinAsync((string)joinInfo.LobbyId, target);
                    return;
                }
            }

            ClearCurrentChannelState();
            throw new InvalidOperationException("EOS ses lobisi olusturulamadi: " + createInfo.ResultCode);
        }

        async Task<JoinLobbyByIdCallbackInfo> JoinVoiceLobbyByIdAsync(string lobbyId)
        {
            var options = new JoinLobbyByIdOptions
            {
                LobbyId = lobbyId,
                LocalUserId = localProductUserId,
                PresenceEnabled = false,
                CrossplayOptOut = false,
                RTCRoomJoinActionType = LobbyRTCRoomJoinActionType.AutomaticJoin
            };
            var completion = new TaskCompletionSource<JoinLobbyByIdCallbackInfo>();
            EOSManager.Instance.GetEOSLobbyInterface().JoinLobbyById(
                ref options,
                null,
                (ref JoinLobbyByIdCallbackInfo data) => completion.TrySetResult(data));
            return await AwaitWithTimeout(completion.Task, "EOS ses lobisine katilma");
        }

        async Task<CreateLobbyCallbackInfo> CreateVoiceLobbyAsync(string lobbyId)
        {
            var options = new CreateLobbyOptions
            {
                LocalUserId = localProductUserId,
                MaxLobbyMembers = VoiceLobbyCapacity,
                PermissionLevel = LobbyPermissionLevel.Publicadvertised,
                PresenceEnabled = false,
                AllowInvites = false,
                BucketId = VoiceLobbyBucket,
                DisableHostMigration = false,
                EnableRTCRoom = true,
                LobbyId = lobbyId,
                EnableJoinById = true,
                RejoinAfterKickRequiresInvite = false,
                CrossplayOptOut = false,
                RTCRoomJoinActionType = LobbyRTCRoomJoinActionType.AutomaticJoin
            };
            var completion = new TaskCompletionSource<CreateLobbyCallbackInfo>();
            EOSManager.Instance.GetEOSLobbyInterface().CreateLobby(
                ref options,
                null,
                (ref CreateLobbyCallbackInfo data) => completion.TrySetResult(data));
            return await AwaitWithTimeout(completion.Task, "EOS ses lobisi olusturma");
        }

        async Task FinishVoiceLobbyJoinAsync(string lobbyId, VoiceTarget target)
        {
            currentVoiceLobbyId = string.IsNullOrEmpty(lobbyId) ? target.VoiceLobbyId : lobbyId;
            currentChannel = target.ChannelName;
            currentChannelLabel = target.ChannelLabel;
            publishedDisplayName = null;
            requestedSendingState = null;
            requestedReceivingState.Clear();

            var roomOptions = new GetRTCRoomNameOptions
            {
                LobbyId = currentVoiceLobbyId,
                LocalUserId = localProductUserId
            };
            Result roomResult = EOSManager.Instance.GetEOSLobbyInterface().GetRTCRoomName(
                ref roomOptions,
                out Utf8String roomName);
            if (roomResult != Result.Success || string.IsNullOrEmpty((string)roomName))
            {
                throw new InvalidOperationException("EOS RTC oda adi alinamadi: " + roomResult);
            }

            currentRtcRoomName = roomName;
            SubscribeRtcNotifications();

            var connectionOptions = new IsRTCRoomConnectedOptions
            {
                LobbyId = currentVoiceLobbyId,
                LocalUserId = localProductUserId
            };
            Result connectionResult = EOSManager.Instance.GetEOSLobbyInterface().IsRTCRoomConnected(
                ref connectionOptions,
                out bool isConnected);
            rtcConnected = connectionResult == Result.Success && isConnected;

            ForceDisableSending();
            await PublishDisplayNameAsync(ResolveLocalDisplayName());
            RefreshParticipants();
            ApplyReceivingPreferences();
            RaiseStateChanged();

            Debug.Log("[VoiceChat/EOS] Kanala baglanildi: " + target.ChannelName +
                      " (EOS Lobby: " + currentVoiceLobbyId + ")");
        }

        async Task PublishDisplayNameAsync(string displayName)
        {
            if (string.IsNullOrEmpty(currentVoiceLobbyId)) return;

            var modificationOptions = new UpdateLobbyModificationOptions
            {
                LobbyId = currentVoiceLobbyId,
                LocalUserId = localProductUserId
            };
            Result result = EOSManager.Instance.GetEOSLobbyInterface().UpdateLobbyModification(
                ref modificationOptions,
                out LobbyModification modification);
            if (result != Result.Success || modification == null)
            {
                throw new InvalidOperationException("EOS ses oyuncu bilgisi hazirlanamadi: " + result);
            }

            var attributeData = new AttributeData
            {
                Key = DisplayNameAttribute,
                Value = LimitUtf8(displayName, 50, "Oyuncu")
            };
            var addAttributeOptions = new LobbyModificationAddMemberAttributeOptions
            {
                Attribute = attributeData,
                Visibility = LobbyAttributeVisibility.Public
            };
            result = modification.AddMemberAttribute(ref addAttributeOptions);
            if (result != Result.Success)
            {
                modification.Release();
                throw new InvalidOperationException("EOS ses oyuncu adi eklenemedi: " + result);
            }

            var updateOptions = new UpdateLobbyOptions { LobbyModificationHandle = modification };
            var completion = new TaskCompletionSource<Result>();
            EOSManager.Instance.GetEOSLobbyInterface().UpdateLobby(
                ref updateOptions,
                null,
                (ref UpdateLobbyCallbackInfo data) =>
                {
                    modification.Release();
                    completion.TrySetResult(data.ResultCode);
                });

            result = await AwaitWithTimeout(completion.Task, "EOS ses oyuncu adini yayinlama");
            if (result != Result.Success && result != Result.NoChange)
            {
                throw new InvalidOperationException("EOS ses oyuncu adi yayinlanamadi: " + result);
            }

            publishedDisplayName = displayName;
        }

        async Task LeaveCurrentVoiceLobbyAsync()
        {
            if (string.IsNullOrEmpty(currentVoiceLobbyId))
            {
                ClearCurrentChannelState();
                return;
            }

            string lobbyToLeave = currentVoiceLobbyId;
            ForceDisableSending();
            UnsubscribeRtcNotifications();

            var options = new LeaveLobbyOptions
            {
                LobbyId = lobbyToLeave,
                LocalUserId = localProductUserId
            };
            var completion = new TaskCompletionSource<Result>();
            EOSManager.Instance.GetEOSLobbyInterface().LeaveLobby(
                ref options,
                null,
                (ref LeaveLobbyCallbackInfo data) => completion.TrySetResult(data.ResultCode));

            Result result = await AwaitWithTimeout(completion.Task, "EOS ses lobisinden ayrilma");
            ClearCurrentChannelState();

            if (result != Result.Success && result != Result.NotFound)
            {
                Debug.LogWarning("[VoiceChat/EOS] Lobiden ayrilma sonucu: " + result);
            }
            else
            {
                Debug.Log("[VoiceChat/EOS] Kanaldan cikildi: " + lobbyToLeave);
            }
        }

        void BeginLeaveWithoutWaiting()
        {
            if (!eosInitialized || string.IsNullOrEmpty(currentVoiceLobbyId) || localProductUserId == null) return;

            try
            {
                var options = new LeaveLobbyOptions
                {
                    LobbyId = currentVoiceLobbyId,
                    LocalUserId = localProductUserId
                };
                EOSManager.Instance.GetEOSLobbyInterface().LeaveLobby(ref options, null, null);
            }
            catch
            {
                // Uygulama kapanirken EOS SDK daha once kapanmis olabilir.
            }
        }

        void ClearCurrentChannelState()
        {
            currentChannel = null;
            currentChannelLabel = null;
            currentVoiceLobbyId = null;
            currentRtcRoomName = null;
            publishedDisplayName = null;
            rtcConnected = false;
            pushToTalkActive = false;
            requestedSendingState = null;
            participants.Clear();
            manuallyMutedParticipants.Clear();
            requestedReceivingState.Clear();
            RaiseStateChanged();
        }

        void SubscribeLobbyNotifications()
        {
            LobbyInterface lobbyInterface = EOSManager.Instance.GetEOSLobbyInterface();

            if (lobbyMemberUpdateNotification == 0)
            {
                var options = new AddNotifyLobbyMemberUpdateReceivedOptions();
                lobbyMemberUpdateNotification = lobbyInterface.AddNotifyLobbyMemberUpdateReceived(
                    ref options,
                    null,
                    HandleLobbyMemberUpdated);
            }

            if (lobbyMemberStatusNotification == 0)
            {
                var options = new AddNotifyLobbyMemberStatusReceivedOptions();
                lobbyMemberStatusNotification = lobbyInterface.AddNotifyLobbyMemberStatusReceived(
                    ref options,
                    null,
                    HandleLobbyMemberStatusChanged);
            }
        }

        void UnsubscribeLobbyNotifications()
        {
            if (!eosInitialized) return;

            try
            {
                LobbyInterface lobbyInterface = EOSManager.Instance.GetEOSLobbyInterface();
                if (lobbyMemberUpdateNotification != 0)
                {
                    lobbyInterface.RemoveNotifyLobbyMemberUpdateReceived(lobbyMemberUpdateNotification);
                    lobbyMemberUpdateNotification = 0;
                }

                if (lobbyMemberStatusNotification != 0)
                {
                    lobbyInterface.RemoveNotifyLobbyMemberStatusReceived(lobbyMemberStatusNotification);
                    lobbyMemberStatusNotification = 0;
                }
            }
            catch
            {
                // Kapanis sirasinda EOS arayuzu artik mevcut olmayabilir.
            }
        }

        void SubscribeRtcNotifications()
        {
            UnsubscribeRtcNotifications();

            LobbyInterface lobbyInterface = EOSManager.Instance.GetEOSLobbyInterface();
            var connectionOptions = new AddNotifyRTCRoomConnectionChangedOptions();
            rtcConnectionNotification = lobbyInterface.AddNotifyRTCRoomConnectionChanged(
                ref connectionOptions,
                null,
                HandleRtcRoomConnectionChanged);

            Epic.OnlineServices.RTC.RTCInterface rtcInterface = EOSManager.Instance.GetEOSRTCInterface();
            var participantOptions = new AddNotifyParticipantStatusChangedOptions
            {
                LocalUserId = localProductUserId,
                RoomName = currentRtcRoomName
            };
            rtcParticipantStatusNotification = rtcInterface.AddNotifyParticipantStatusChanged(
                ref participantOptions,
                null,
                HandleRtcParticipantStatusChanged);

            RTCAudioInterface audioInterface = rtcInterface.GetAudioInterface();
            var audioOptions = new AddNotifyParticipantUpdatedOptions
            {
                LocalUserId = localProductUserId,
                RoomName = currentRtcRoomName
            };
            rtcParticipantAudioNotification = audioInterface.AddNotifyParticipantUpdated(
                ref audioOptions,
                null,
                HandleRtcParticipantAudioUpdated);
        }

        void UnsubscribeRtcNotifications()
        {
            if (!eosInitialized) return;

            try
            {
                if (rtcConnectionNotification != 0)
                {
                    EOSManager.Instance.GetEOSLobbyInterface().RemoveNotifyRTCRoomConnectionChanged(rtcConnectionNotification);
                    rtcConnectionNotification = 0;
                }

                if (rtcParticipantStatusNotification != 0)
                {
                    EOSManager.Instance.GetEOSRTCInterface().RemoveNotifyParticipantStatusChanged(rtcParticipantStatusNotification);
                    rtcParticipantStatusNotification = 0;
                }

                if (rtcParticipantAudioNotification != 0)
                {
                    EOSManager.Instance.GetEOSRTCInterface().GetAudioInterface()
                        .RemoveNotifyParticipantUpdated(rtcParticipantAudioNotification);
                    rtcParticipantAudioNotification = 0;
                }
            }
            catch
            {
                // Kapanis sirasinda EOS arayuzu artik mevcut olmayabilir.
            }
        }

        void HandleLobbyMemberUpdated(ref LobbyMemberUpdateReceivedCallbackInfo data)
        {
            if (!IsCurrentLobby((string)data.LobbyId)) return;
            RefreshParticipants();
            ApplyReceivingPreferences();
            RaiseStateChanged();
        }

        void HandleLobbyMemberStatusChanged(ref LobbyMemberStatusReceivedCallbackInfo data)
        {
            if (!IsCurrentLobby((string)data.LobbyId)) return;
            RefreshParticipants();
            ApplyReceivingPreferences();
            RaiseStateChanged();
        }

        void HandleRtcRoomConnectionChanged(ref RTCRoomConnectionChangedCallbackInfo data)
        {
            if (!IsCurrentLobby((string)data.LobbyId) || !IsLocalUser(data.LocalUserId)) return;

            rtcConnected = data.IsConnected;
            if (!rtcConnected)
            {
                pushToTalkActive = false;
                requestedSendingState = null;
            }
            else
            {
                ForceDisableSending();
                RefreshParticipants();
                ApplyReceivingPreferences();
            }

            SetStatus(rtcConnected
                ? "Ses: " + currentChannelLabel
                : "Ses RTC baglantisi kesildi: " + data.DisconnectReason);
            RaiseStateChanged();
        }

        void HandleRtcParticipantStatusChanged(ref ParticipantStatusChangedCallbackInfo data)
        {
            if (!IsCurrentRoom((string)data.RoomName)) return;

            string participantId = ProductUserIdToString(data.ParticipantId);
            if (data.ParticipantStatus == RTCParticipantStatus.Joined)
            {
                RefreshParticipants();
                if (participants.TryGetValue(participantId, out VoiceParticipant participant))
                {
                    participant.IsInRoom = true;
                }
            }
            else
            {
                participants.Remove(participantId);
                requestedReceivingState.Remove(participantId);
            }

            ApplyReceivingPreferences();
            RaiseStateChanged();
        }

        void HandleRtcParticipantAudioUpdated(ref ParticipantUpdatedCallbackInfo data)
        {
            if (!IsCurrentRoom((string)data.RoomName)) return;

            string participantId = ProductUserIdToString(data.ParticipantId);
            if (!participants.TryGetValue(participantId, out VoiceParticipant participant))
            {
                RefreshParticipants();
                participants.TryGetValue(participantId, out participant);
            }

            if (participant == null) return;
            participant.IsSpeaking = data.Speaking;
            participant.IsInRoom = true;
            RaiseStateChanged();
        }

        void RefreshParticipants()
        {
            if (string.IsNullOrEmpty(currentVoiceLobbyId) || localProductUserId == null) return;

            var copyOptions = new CopyLobbyDetailsHandleOptions
            {
                LobbyId = currentVoiceLobbyId,
                LocalUserId = localProductUserId
            };
            Result result = EOSManager.Instance.GetEOSLobbyInterface().CopyLobbyDetailsHandle(
                ref copyOptions,
                out LobbyDetails details);
            if (result != Result.Success || details == null) return;

            try
            {
                var countOptions = new LobbyDetailsGetMemberCountOptions();
                uint memberCount = details.GetMemberCount(ref countOptions);
                var currentIds = new HashSet<string>();

                for (uint index = 0; index < memberCount; index++)
                {
                    var memberOptions = new LobbyDetailsGetMemberByIndexOptions { MemberIndex = index };
                    ProductUserId memberId = details.GetMemberByIndex(ref memberOptions);
                    if (memberId == null || !memberId.IsValid()) continue;

                    string id = ProductUserIdToString(memberId);
                    currentIds.Add(id);
                    if (!participants.TryGetValue(id, out VoiceParticipant participant))
                    {
                        participant = new VoiceParticipant
                        {
                            ProductUserId = memberId,
                            Id = id,
                            IsInRoom = true
                        };
                        participants.Add(id, participant);
                    }
                    else
                    {
                        participant.ProductUserId = memberId;
                    }

                    string displayName = ReadMemberStringAttribute(details, memberId, DisplayNameAttribute);
                    participant.DisplayName = string.IsNullOrWhiteSpace(displayName)
                        ? BuildFallbackDisplayName(id)
                        : displayName;
                }

                var removedIds = new List<string>();
                foreach (string id in participants.Keys)
                {
                    if (!currentIds.Contains(id)) removedIds.Add(id);
                }

                for (int i = 0; i < removedIds.Count; i++)
                {
                    participants.Remove(removedIds[i]);
                    requestedReceivingState.Remove(removedIds[i]);
                    manuallyMutedParticipants.Remove(removedIds[i]);
                }
            }
            finally
            {
                details.Release();
            }
        }

        static string ReadMemberStringAttribute(LobbyDetails details, ProductUserId memberId, string key)
        {
            var options = new LobbyDetailsCopyMemberAttributeByKeyOptions
            {
                TargetUserId = memberId,
                AttrKey = key
            };
            Result result = details.CopyMemberAttributeByKey(
                ref options,
                out Epic.OnlineServices.Lobby.Attribute? attribute);
            if (result != Result.Success || !attribute.HasValue || !attribute.Value.Data.HasValue) return null;

            return attribute.Value.Data.Value.Value.AsUtf8;
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
            RequestSending(active && IsReady, false);
            RaiseStateChanged();
        }

        void ForceDisableSending()
        {
            pushToTalkActive = false;
            RequestSending(false, true);
            RaiseStateChanged();
        }

        void RequestSending(bool enabled, bool force)
        {
            if (!eosInitialized || localProductUserId == null || string.IsNullOrEmpty(currentRtcRoomName)) return;
            if (!force && requestedSendingState.HasValue && requestedSendingState.Value == enabled) return;

            requestedSendingState = enabled;
            var options = new UpdateSendingOptions
            {
                LocalUserId = localProductUserId,
                RoomName = currentRtcRoomName,
                AudioStatus = enabled ? RTCAudioStatus.Enabled : RTCAudioStatus.Disabled
            };
            EOSManager.Instance.GetEOSRTCInterface().GetAudioInterface().UpdateSending(
                ref options,
                null,
                (ref UpdateSendingCallbackInfo data) =>
                {
                    if (data.ResultCode != Result.Success && data.ResultCode != Result.NoChange)
                    {
                        Debug.LogWarning("[VoiceChat/EOS] Mikrofon durumu degistirilemedi: " + data.ResultCode);
                    }
                });
        }

        void UpdateOutputMuteShortcut()
        {
            if (!IsReady || Keyboard.current == null || !Keyboard.current.mKey.wasPressedThisFrame) return;
            ToggleAllIncomingVoice();
        }

        public void ToggleAllIncomingVoice()
        {
            if (!IsReady) return;
            allIncomingMuted = !allIncomingMuted;
            requestedReceivingState.Clear();
            ApplyReceivingPreferences();
            RaiseStateChanged();
        }

        void ToggleParticipantMute(VoiceParticipant participant)
        {
            if (participant == null || IsLocalUser(participant.ProductUserId)) return;

            if (!manuallyMutedParticipants.Add(participant.Id))
            {
                manuallyMutedParticipants.Remove(participant.Id);
            }

            requestedReceivingState.Remove(participant.Id);
            ApplyReceivingPreference(participant);
            RaiseStateChanged();
        }

        void ApplyReceivingPreferences()
        {
            foreach (VoiceParticipant participant in participants.Values)
            {
                ApplyReceivingPreference(participant);
            }
        }

        void ApplyReceivingPreference(VoiceParticipant participant)
        {
            if (!IsReady || participant == null || IsLocalUser(participant.ProductUserId)) return;

            bool shouldReceive = !allIncomingMuted && !manuallyMutedParticipants.Contains(participant.Id);
            if (requestedReceivingState.TryGetValue(participant.Id, out bool previous) && previous == shouldReceive) return;

            requestedReceivingState[participant.Id] = shouldReceive;
            var options = new UpdateReceivingOptions
            {
                LocalUserId = localProductUserId,
                RoomName = currentRtcRoomName,
                ParticipantId = participant.ProductUserId,
                AudioEnabled = shouldReceive
            };
            EOSManager.Instance.GetEOSRTCInterface().GetAudioInterface().UpdateReceiving(
                ref options,
                null,
                (ref UpdateReceivingCallbackInfo data) =>
                {
                    if (data.ResultCode != Result.Success && data.ResultCode != Result.NoChange &&
                        data.ResultCode != Result.NotFound)
                    {
                        Debug.LogWarning("[VoiceChat/EOS] Oyuncu ses durumu degistirilemedi: " + data.ResultCode);
                    }
                });
        }

        bool TryGetTarget(out VoiceTarget target)
        {
            target = default;

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
                target = new VoiceTarget(channelPrefix + "-lobby", "Lobi");
                return true;
            }

            NetworkPlayerState localPlayer = NetworkPlayerState.Find(networkManager.LocalClientId);
            if (localPlayer == null || localPlayer.Team.Value == TeamId.None) return false;

            if (localPlayer.Team.Value == TeamId.TeamA)
            {
                target = new VoiceTarget(channelPrefix + "-team-a", "Takim A");
                return true;
            }

            if (localPlayer.Team.Value == TeamId.TeamB)
            {
                target = new VoiceTarget(channelPrefix + "-team-b", "Takim B");
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
                Debug.LogWarning("[VoiceChat/EOS] Aktif oturum okunamadi: " + exception.Message);
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
                    if (!string.IsNullOrEmpty(networkName)) return LimitUtf8(networkName, 50, "Oyuncu");
                }
            }

            try
            {
                string playerId = AuthenticationService.Instance.PlayerId;
                if (!string.IsNullOrEmpty(playerId))
                {
                    string suffix = playerId.Length > 6 ? playerId.Substring(playerId.Length - 6) : playerId;
                    return "Oyuncu-" + suffix;
                }
            }
            catch
            {
                // Unity Authentication henuz hazir degilse EOS kimligi kullanilir.
            }

            string eosId = ProductUserIdToString(localProductUserId);
            return BuildFallbackDisplayName(eosId);
        }

        static string BuildFallbackDisplayName(string id)
        {
            if (string.IsNullOrEmpty(id)) return "Oyuncu";
            string suffix = id.Length > 6 ? id.Substring(id.Length - 6) : id;
            return "Oyuncu-" + suffix;
        }

        static string ProductUserIdToString(ProductUserId productUserId)
        {
            return productUserId == null ? string.Empty : productUserId.ToString();
        }

        bool IsLocalUser(ProductUserId productUserId)
        {
            return string.Equals(
                ProductUserIdToString(productUserId),
                ProductUserIdToString(localProductUserId),
                StringComparison.Ordinal);
        }

        bool IsCurrentLobby(string lobbyId)
        {
            return !string.IsNullOrEmpty(currentVoiceLobbyId) &&
                   string.Equals(currentVoiceLobbyId, lobbyId, StringComparison.Ordinal);
        }

        bool IsCurrentRoom(string roomName)
        {
            return !string.IsNullOrEmpty(currentRtcRoomName) &&
                   string.Equals(currentRtcRoomName, roomName, StringComparison.Ordinal);
        }

        static string BuildDeterministicLobbyId(string channelName)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(channelName));
                var builder = new StringBuilder("bpv-");
                for (int i = 0; i < 26; i++) builder.Append(hash[i].ToString("x2"));
                return builder.ToString();
            }
        }

        static string SanitizeChannelPart(string value)
        {
            var builder = new StringBuilder(Math.Min(value.Length, 80));
            for (int i = 0; i < value.Length && builder.Length < 80; i++)
            {
                char character = char.ToLowerInvariant(value[i]);
                builder.Append(char.IsLetterOrDigit(character) || character == '-' ? character : '-');
            }

            return builder.Length > 0 ? builder.ToString() : "session";
        }

        static string LimitUtf8(string value, int maxLength, string fallback)
        {
            if (string.IsNullOrWhiteSpace(value)) return fallback;
            string trimmed = value.Trim();
            return trimmed.Length <= maxLength ? trimmed : trimmed.Substring(0, maxLength);
        }

        static string FriendlyError(Exception exception)
        {
            string message = exception.GetBaseException().Message;
            if (string.IsNullOrWhiteSpace(message)) return exception.GetType().Name;
            return message.Length <= 140 ? message : message.Substring(0, 140) + "...";
        }

        static async Task<T> AwaitWithTimeout<T>(Task<T> task, string operationName)
        {
            Task completed = await Task.WhenAny(task, Task.Delay(OperationTimeoutMilliseconds));
            if (completed != task) throw new TimeoutException(operationName + " zaman asimina ugradi");
            return await task;
        }

        static async Task<bool> EnsureMicrophonePermissionAsync()
        {
            if (Application.HasUserAuthorization(UserAuthorization.Microphone)) return true;

            AsyncOperation request = Application.RequestUserAuthorization(UserAuthorization.Microphone);
            while (!request.isDone) await Task.Yield();
            return Application.HasUserAuthorization(UserAuthorization.Microphone);
        }

        bool IsVoiceScene()
        {
            string sceneName = SceneManager.GetActiveScene().name;
            return sceneName == LobbySceneName || sceneName == GameSceneName;
        }

        void DrawVoiceOverlay()
        {
            int remoteParticipantCount = 0;
            foreach (VoiceParticipant participant in participants.Values)
            {
                if (!IsLocalUser(participant.ProductUserId)) remoteParticipantCount++;
            }

            float panelHeight = 130f + remoteParticipantCount * 30f;
            var panelRect = new Rect(Screen.width - 330f, 12f, 318f, panelHeight);

            GUILayout.BeginArea(panelRect, GUIContent.none, GUI.skin.box);
            GUILayout.Label(statusText);

            if (IsReady)
            {
                GUILayout.Label(pushToTalkActive ? "V: KONUSUYORSUN" : "V basili tut: konus");
                if (GUILayout.Button(allIncomingMuted ? "M: Tum sesleri ac" : "M: Tum sesleri kapat"))
                {
                    ToggleAllIncomingVoice();
                }
            }

            foreach (VoiceParticipant participant in participants.Values)
            {
                if (IsLocalUser(participant.ProductUserId)) continue;

                GUILayout.BeginHorizontal();
                GUILayout.Label((participant.IsSpeaking ? "● " : "○ ") + participant.DisplayName,
                    GUILayout.ExpandWidth(true));
                bool isMuted = manuallyMutedParticipants.Contains(participant.Id);
                if (GUILayout.Button(isMuted ? "Sesi ac" : "Sustur", GUILayout.Width(72f)))
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
