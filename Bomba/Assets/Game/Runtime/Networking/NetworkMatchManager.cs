using System;
using System.Collections.Generic;
using System.Linq;
using BriefcaseProtocol.Core;
using BriefcaseProtocol.Gameplay.Setup;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BriefcaseProtocol.Networking
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkMatchManager : NetworkBehaviour
    {
        public static NetworkMatchManager Instance { get; private set; }

        [SerializeField] private GameBalanceConfig balance;

        private readonly NetworkVariable<NetworkMatchState> state = new(
            new NetworkMatchState { Phase = MatchPhase.Lobby },
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        private readonly NetworkList<NetworkPlayerState> players = new();
        private readonly MatchStateMachine machine = new();
        private readonly RoundStatistics[] completedRounds = new RoundStatistics[2];
        private uint eventSequence;
        private double operationStartedAt;
        private bool lastSixtySent;

        public NetworkMatchState State => state.Value;
        public NetworkList<NetworkPlayerState> Players => players;
        public GameBalanceConfig Balance => balance;
        public double ServerNow => NetworkManager != null && NetworkManager.IsListening
            ? NetworkManager.ServerTime.Time
            : Time.unscaledTimeAsDouble;

        public event Action<NetworkMatchState, NetworkMatchState> StateChanged;
        public event Action<MatchEventData> MatchEventReceived;
        public event Action ServerRoundResetRequested;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (balance == null)
            {
                balance = ScriptableObject.CreateInstance<GameBalanceConfig>();
                balance.name = "Runtime Game Balance";
            }
        }

        public override void OnNetworkSpawn()
        {
            state.OnValueChanged += HandleStateChanged;
            if (!IsServer)
            {
                return;
            }

            NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            NetworkManager.OnClientDisconnectCallback += HandleClientDisconnected;

            foreach (var clientId in NetworkManager.ConnectedClientsIds)
            {
                HandleClientConnected(clientId);
            }
        }

        public override void OnNetworkDespawn()
        {
            state.OnValueChanged -= HandleStateChanged;
            if (NetworkManager != null && IsServer)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
                NetworkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            }
        }

        private void Update()
        {
            if (!IsServer || machine.Phase == MatchPhase.Lobby)
            {
                return;
            }

            var now = ServerNow;
            if (machine.Phase == MatchPhase.Operation && !lastSixtySent && machine.PhaseEndsAt - now <= 60d)
            {
                lastSixtySent = true;
                Publish(MatchEventType.LastSixtySeconds, 0, "operation", 60);
            }

            if (machine.Phase == MatchPhase.Setup && now >= machine.PhaseEndsAt &&
                SetupValidator.Instance != null && !SetupValidator.Instance.IsValid(machine.BuilderTeam))
            {
                machine.DelayCurrentPhase(1d);
                SynchronizeMachine(false);
                return;
            }

            if (machine.Phase == MatchPhase.Operation && now >= machine.PhaseEndsAt)
            {
                CompleteRound(machine.BuilderTeam, RoundEndReason.TimeExpired);
                return;
            }

            if (!machine.Tick(now, balance))
            {
                return;
            }

            if (machine.Phase == MatchPhase.RoleReveal && machine.RoundIndex == 1)
            {
                ResetRoundState();
            }

            SynchronizeMachine();
        }

        [ServerRpc(InvokePermission = RpcInvokePermission.Everyone)]
        public void SelectLobbySlotServerRpc(TeamId team, RoleSlot slot, FixedString64Bytes displayName,
            ServerRpcParams rpc = default)
        {
            if (state.Value.Phase != MatchPhase.Lobby || team == TeamId.None || slot == RoleSlot.None)
            {
                return;
            }

            var clientId = rpc.Receive.SenderClientId;
            for (var i = 0; i < players.Count; i++)
            {
                var other = players[i];
                if (other.ClientId != clientId && other.Connected && other.Team == team && other.Slot == slot)
                {
                    return;
                }
            }

            var index = IndexOf(clientId);
            if (index < 0)
            {
                return;
            }

            var player = players[index];
            player.Team = team;
            player.Slot = slot;
            player.DisplayName = SanitizeName(displayName, clientId);
            player.Ready = false;
            players[index] = player;
        }

        [ServerRpc(InvokePermission = RpcInvokePermission.Everyone)]
        public void SetReadyServerRpc(bool ready, ServerRpcParams rpc = default)
        {
            if (state.Value.Phase != MatchPhase.Lobby)
            {
                return;
            }

            var index = IndexOf(rpc.Receive.SenderClientId);
            if (index < 0 || players[index].Team == TeamId.None || players[index].Slot == RoleSlot.None)
            {
                return;
            }

            var player = players[index];
            player.Ready = ready;
            players[index] = player;

            if (CanStartMatch())
            {
                StartMatch();
            }
        }

        public bool IsRoleAllowed(ulong clientId, params GameplayRole[] roles)
        {
            var index = IndexOf(clientId);
            if (index < 0 || !players[index].Connected)
            {
                return false;
            }

            var player = players[index];
            var role = player.RoleFor(state.Value.BuilderTeam);
            return roles.Contains(role);
        }

        public void ApplyOperationPenalty(int seconds, ulong actorClientId, string subject)
        {
            if (!IsServer || state.Value.Phase != MatchPhase.Operation || seconds <= 0)
            {
                return;
            }

            machine.ApplyTimePenalty(seconds, ServerNow);
            var value = state.Value;
            value.PhaseEndsAt = machine.PhaseEndsAt;
            state.Value = value;
            Publish(MatchEventType.WrongCode, actorClientId, subject, seconds);
        }

        public void RegisterStrike(ulong actorClientId, string subject)
        {
            if (!IsServer || state.Value.Phase != MatchPhase.Operation)
            {
                return;
            }

            var value = state.Value;
            value.Strikes++;
            state.Value = value;
            if (value.Strikes >= balance.strikeLimit)
            {
                CompleteRound(value.BuilderTeam, RoundEndReason.StrikeLimit);
            }
        }

        public void FinishDefusal(ulong actorClientId)
        {
            if (!IsServer || state.Value.Phase != MatchPhase.Operation)
            {
                return;
            }

            Publish(MatchEventType.ModuleCompleted, actorClientId, "all", 2);
            CompleteRound(state.Value.SolverTeam, RoundEndReason.Defused);
        }

        public void Publish(MatchEventType type, ulong actorClientId, string subject, int value = 0)
        {
            if (!IsServer)
            {
                return;
            }

            var data = new MatchEventData
            {
                Sequence = ++eventSequence,
                Type = type,
                ActorClientId = actorClientId,
                Subject = new FixedString64Bytes(subject ?? string.Empty),
                Value = value,
                ServerTime = ServerNow
            };
            ReceiveEventClientRpc(data);
        }

        private void StartMatch()
        {
            var firstBuilder = UnityEngine.Random.Range(0, 2) == 0 ? TeamId.Red : TeamId.Blue;
            Array.Clear(completedRounds, 0, completedRounds.Length);
            machine.StartMatch(firstBuilder, ServerNow, balance);
            ResetRoundState();
            SynchronizeMachine();
            if (NetworkManager.SceneManager != null && SceneManager.GetActiveScene().name != "Game")
            {
                NetworkManager.SceneManager.LoadScene("Game", LoadSceneMode.Single);
            }
        }

        private void CompleteRound(TeamId winner, RoundEndReason reason)
        {
            var round = Math.Clamp(machine.RoundIndex, 0, 1);
            completedRounds[round] = new RoundStatistics
            {
                BuilderTeam = state.Value.BuilderTeam,
                Winner = winner,
                EndReason = reason,
                OperationDuration = (float)Math.Max(0d, ServerNow - operationStartedAt),
                SuccessfulSolveTime = reason == RoundEndReason.Defused
                    ? (float)Math.Max(0d, ServerNow - operationStartedAt)
                    : 0f,
                Strikes = state.Value.Strikes
            };

            var previousBuilder = state.Value.BuilderTeam;
            machine.FinishOperation(ServerNow, balance);
            var value = state.Value;
            value.Phase = machine.Phase;
            value.PhaseEndsAt = machine.PhaseEndsAt;
            value.LastEndReason = reason;
            value.LastRoundWinner = winner;
            if (round == 1)
            {
                value.MatchWinner = MatchResultCalculator.CalculateWinner(completedRounds[0], completedRounds[1]);
            }

            state.Value = value;
            Publish(MatchEventType.RoundEnded, 0, previousBuilder.ToString(), (int)winner);
        }

        private void SynchronizeMachine(bool publishEvent = true)
        {
            var value = state.Value;
            value.Phase = machine.Phase;
            value.RoundIndex = machine.RoundIndex;
            value.BuilderTeam = machine.BuilderTeam;
            value.PhaseEndsAt = machine.PhaseEndsAt;
            if (machine.Phase == MatchPhase.Operation)
            {
                operationStartedAt = ServerNow;
                lastSixtySent = false;
            }

            state.Value = value;
            if (publishEvent)
            {
                Publish(MatchEventType.PhaseChanged, 0, machine.Phase.ToString(), machine.RoundIndex);
            }
        }

        private void ResetRoundState()
        {
            var value = state.Value;
            value.Strikes = 0;
            value.LastEndReason = RoundEndReason.None;
            value.LastRoundWinner = TeamId.None;
            state.Value = value;
            lastSixtySent = false;
            ServerRoundResetRequested?.Invoke();
        }

        private bool CanStartMatch()
        {
            var connected = new List<NetworkPlayerState>(4);
            for (var i = 0; i < players.Count; i++)
            {
                if (players[i].Connected)
                {
                    connected.Add(players[i]);
                }
            }

            if (connected.Count != 4 || connected.Any(p => !p.Ready || p.Team == TeamId.None || p.Slot == RoleSlot.None))
            {
                return false;
            }

            return Enum.GetValues(typeof(TeamId)).Cast<TeamId>().Where(t => t != TeamId.None)
                .All(team => connected.Count(p => p.Team == team) == 2 &&
                    connected.Count(p => p.Team == team && p.Slot == RoleSlot.Operator) == 1 &&
                    connected.Count(p => p.Team == team && p.Slot == RoleSlot.Support) == 1);
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (IndexOf(clientId) >= 0)
            {
                return;
            }

            players.Add(new NetworkPlayerState
            {
                ClientId = clientId,
                DisplayName = new FixedString64Bytes($"Player {clientId + 1}"),
                Team = TeamId.None,
                Slot = RoleSlot.None,
                Ready = false,
                Connected = true
            });
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            var index = IndexOf(clientId);
            if (index >= 0)
            {
                var player = players[index];
                player.Connected = false;
                player.Ready = false;
                players[index] = player;
            }

            Publish(MatchEventType.PlayerDisconnected, clientId, "disconnect");
            if (state.Value.Phase != MatchPhase.Lobby)
            {
                machine.ResetToLobby();
                var value = state.Value;
                value.Phase = MatchPhase.Lobby;
                value.LastEndReason = RoundEndReason.PlayerDisconnected;
                value.PhaseEndsAt = 0d;
                state.Value = value;
                foreach (var playerIndex in Enumerable.Range(0, players.Count))
                {
                    var player = players[playerIndex];
                    player.Ready = false;
                    players[playerIndex] = player;
                }
            }
        }

        private int IndexOf(ulong clientId)
        {
            for (var i = 0; i < players.Count; i++)
            {
                if (players[i].ClientId == clientId)
                {
                    return i;
                }
            }

            return -1;
        }

        private static FixedString64Bytes SanitizeName(FixedString64Bytes value, ulong clientId)
        {
            var trimmed = value.ToString().Trim();
            if (trimmed.Length == 0)
            {
                trimmed = $"Player {clientId + 1}";
            }

            if (trimmed.Length > 24)
            {
                trimmed = trimmed[..24];
            }

            return new FixedString64Bytes(trimmed);
        }

        private void HandleStateChanged(NetworkMatchState previous, NetworkMatchState current)
        {
            StateChanged?.Invoke(previous, current);
        }

        [ClientRpc]
        private void ReceiveEventClientRpc(MatchEventData data)
        {
            MatchEventReceived?.Invoke(data);
        }
    }
}
