using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Core
{
    /// <summary>
    /// Maç seviyesini yönetir: round sayacı, skor ve hangi takımın kurucu olduğu.
    /// Aşama akışını RoundManager'a devreder.
    /// Bütün karar yetkisi server'da; client yalnızca okur.
    /// </summary>
    public class MatchManager : NetworkBehaviour
    {
        public static MatchManager Instance { get; private set; }

        public readonly NetworkVariable<int> CurrentRound =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Bu round'da bombayı kuran takım. Her round değişir.</summary>
        public readonly NetworkVariable<TeamId> BuilderTeam =
            new NetworkVariable<TeamId>(TeamId.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> ScoreTeamA =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> ScoreTeamB =
            new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> MatchInProgress =
            new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        /// <summary>Bu round'un kazananı. None ise henüz belirlenmedi.</summary>
        public readonly NetworkVariable<TeamId> RoundWinner =
            new NetworkVariable<TeamId>(TeamId.None, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            Instance = this;

            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnPhaseChanged += HandlePhaseChanged;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.OnPhaseChanged -= HandlePhaseChanged;
            }

            if (Instance == this) Instance = null;
        }

        // ---------------------------------------------------------------
        // Otomatik başlatma
        // ---------------------------------------------------------------

        /// <summary>
        /// Lobide dört oyuncu benzersiz slotlarla hazır olur olmaz maçı başlatır.
        /// Yalnız server çalıştırır; ayrı bir "Start" komutuna gerek kalmaz.
        /// </summary>



        // ---------------------------------------------------------------
        // Sorgular — herkes kullanabilir
        // ---------------------------------------------------------------

        public TeamSide GetSide(TeamId team)
        {
            if (team == TeamId.None || BuilderTeam.Value == TeamId.None) return TeamSide.None;
            return team == BuilderTeam.Value ? TeamSide.Builder : TeamSide.Solver;
        }

        public TeamId SolverTeam => MatchRules.Opponent(BuilderTeam.Value);

        public RoundRole GetRole(NetworkPlayerState player)
        {
            if (player == null) return RoundRole.None;
            return MatchRules.ResolveRole(GetSide(player.Team.Value), player.Slot.Value);
        }

        public int GetScore(TeamId team)
        {
            if (team == TeamId.TeamA) return ScoreTeamA.Value;
            if (team == TeamId.TeamB) return ScoreTeamB.Value;
            return 0;
        }

        public bool IsFinalRound => CurrentRound.Value >= MatchRules.RoundsPerMatch;

        // ---------------------------------------------------------------
        // Server API
        // ---------------------------------------------------------------

        /// <summary>Lobiden maça geçiş. Ön koşullar sağlanmazsa hiçbir şey yapmaz.</summary>
        /// <summary>
        /// Lobiden maça geçiş. Host'un Başlat komutuyla çağrılır.
        /// autoAssign true ise slotu olmayan oyunculara geçici olarak takım/slot atar;
        /// gerçek seçim arayüzü gelene kadar test için gerekli.
        /// </summary>
        /// <summary>
        /// Maçı başlatır. Game sahnesi yüklendikten sonra server tarafından çağrılır.
        /// autoAssign true ise slotu olmayan oyunculara geçici takım/slot atar.
        /// requiredPlayers test sırasında düşürülebilir.
        /// </summary>
        public void ServerStartMatch(bool autoAssign = false, int requiredPlayers = MatchRules.TotalPlayers)
        {
            if (!IsServer) return;
            if (MatchInProgress.Value) return;

            if (autoAssign) ServerAutoAssignSlots();

            if (!NetworkPlayerState.CanStartMatch(requiredPlayers))
            {
                Debug.LogWarning("[MatchManager] Maç başlatılamadı. Oyuncu state sayısı: " + NetworkPlayerState.All.Count + ", gereken: " + requiredPlayers);
                return;
            }

            ScoreTeamA.Value = 0;
            ScoreTeamB.Value = 0;
            RoundWinner.Value = TeamId.None;

            BuilderTeam.Value = Random.value < 0.5f ? TeamId.TeamA : TeamId.TeamB;

            CurrentRound.Value = 1;
            MatchInProgress.Value = true;

            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.ServerBeginRound();
                Debug.Log("[MatchManager] Maç başladı. Kurucu takım: " + BuilderTeam.Value);
            }
            else
            {
                Debug.LogError("[MatchManager] RoundManager bulunamadı, round başlatılamıyor.");
            }
        }


        /// <summary>
        /// GEÇİCİ: slot seçim arayüzü yazılana kadar oyunculara sırayla takım/slot ve ready atar.
        /// Gerçek lobi UI'ı geldiğinde silinecek.
        /// </summary>
        void ServerAutoAssignSlots()
        {
            if (!IsServer) return;

            TeamId[] teams = { TeamId.TeamA, TeamId.TeamA, TeamId.TeamB, TeamId.TeamB };
            RoleSlot[] slots = { RoleSlot.Operator, RoleSlot.Support, RoleSlot.Operator, RoleSlot.Support };

            var players = NetworkPlayerState.All;
            int index = 0;

            for (int i = 0; i < players.Count && index < teams.Length; i++)
            {
                var player = players[i];
                if (player == null) continue;

                player.ServerForceAssign(teams[index], slots[index], true);
                index++;
            }
        }


        /// <summary>
        /// Çözücü takımın round'u kazandığını bildirir. Bomba etkisiz hale getirildiğinde
        /// gameplay tarafından çağrılır.
        /// </summary>
        public void ServerReportSolved()
        {
            if (!IsServer || !MatchInProgress.Value) return;
            if (RoundWinner.Value != TeamId.None) return;

            RoundWinner.Value = SolverTeam;

            // Süre dolmasını beklemeden Reveal'e geç.
            if (RoundManager.Instance != null &&
                RoundManager.Instance.Phase.Value == MatchPhase.Operation)
            {
                RoundManager.Instance.ServerSetPhase(MatchPhase.Reveal);
            }
        }

        /// <summary>RoundResults aşaması bittiğinde RoundManager tarafından çağrılır.</summary>
        public void ServerAdvanceAfterRoundResults()
        {
            if (!IsServer) return;

            if (IsFinalRound)
            {
                MatchInProgress.Value = false;
                if (RoundManager.Instance != null)
                {
                    RoundManager.Instance.ServerSetPhase(MatchPhase.MatchResults);
                }

                return;
            }

            // Sonraki round: taraflar yer değişir, oyuncu slotları sabit kalır.
            CurrentRound.Value += 1;
            BuilderTeam.Value = MatchRules.Opponent(BuilderTeam.Value);
            RoundWinner.Value = TeamId.None;

            var players = NetworkPlayerState.All;
            for (int i = 0; i < players.Count; i++)
            {
                if (players[i] != null) players[i].ServerResetForNewRound();
            }

            if (RoundManager.Instance != null)
            {
                RoundManager.Instance.ServerBeginRound();
            }
        }

        // ---------------------------------------------------------------
        // Aşama tepkileri
        // ---------------------------------------------------------------

        void HandlePhaseChanged(MatchPhase previous, MatchPhase current)
        {
            if (!IsServer) return;

            // Reveal'e girildiğinde kazanan kesinleşir.
            // Kimse çözdüğünü bildirmediyse süre dolmuş demektir: kurucu takım kazanır.
            if (current == MatchPhase.Reveal)
            {
                if (RoundWinner.Value == TeamId.None)
                {
                    RoundWinner.Value = BuilderTeam.Value;
                }

                ServerAwardPoint(RoundWinner.Value);
            }
        }

        void ServerAwardPoint(TeamId team)
        {
            if (!IsServer) return;

            if (team == TeamId.TeamA) ScoreTeamA.Value += 1;
            else if (team == TeamId.TeamB) ScoreTeamB.Value += 1;
        }
    }
}
