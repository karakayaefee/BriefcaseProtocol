using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;

namespace BriefcaseProtocol.Core
{
    /// <summary>
    /// Oyuncu başına bir tane spawn edilen kalıcı state.
    /// Bütün NetworkVariable'ların yazma yetkisi server'dadır; client yalnızca RPC ile talep eder.
    /// </summary>
    public class NetworkPlayerState : NetworkBehaviour
    {
        static readonly List<NetworkPlayerState> Registry = new List<NetworkPlayerState>();

        /// <summary>Spawn olmuş bütün oyuncu state'leri. Sıra garanti edilmez.</summary>
        public static IReadOnlyList<NetworkPlayerState> All => Registry;

        public readonly NetworkVariable<FixedString32Bytes> DisplayName =
            new NetworkVariable<FixedString32Bytes>(
                default,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<TeamId> Team =
            new NetworkVariable<TeamId>(
                TeamId.None,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<RoleSlot> Slot =
            new NetworkVariable<RoleSlot>(
                RoleSlot.None,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> IsReady =
            new NetworkVariable<bool>(
                false,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        public override void OnNetworkSpawn()
        {
            if (!Registry.Contains(this))
            {
                Registry.Add(this);
            }
        }

        public override void OnNetworkDespawn()
        {
            Registry.Remove(this);
        }

        // ---------------------------------------------------------------
        // Client -> Server talepleri
        // ---------------------------------------------------------------

        /// <summary>Takım ve slot talebi. Server doluluk kontrolü yapıp reddedebilir.</summary>
        [Rpc(SendTo.Server)]
        public void RequestSlotRpc(TeamId team, RoleSlot slot, RpcParams rpcParams = default)
        {
            // Başka bir client'ın adına komut göndermeyi engelle.
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

            if (team == TeamId.None || slot == RoleSlot.None) return;
            if (!IsSlotAvailable(team, slot, this)) return;

            Team.Value = team;
            Slot.Value = slot;

            // Slot değişince önceki onay geçersizdir.
            IsReady.Value = false;
        }

        [Rpc(SendTo.Server)]
        public void ClearSlotRpc(RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

            Team.Value = TeamId.None;
            Slot.Value = RoleSlot.None;
            IsReady.Value = false;
        }

        [Rpc(SendTo.Server)]
        public void SetReadyRpc(bool ready, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;

            // Slot seçmemiş oyuncu hazır olamaz.
            if (ready && (Team.Value == TeamId.None || Slot.Value == RoleSlot.None)) return;

            IsReady.Value = ready;
        }

        [Rpc(SendTo.Server)]
        public void SubmitNameRpc(FixedString32Bytes requestedName, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != OwnerClientId) return;
            if (requestedName.IsEmpty) return;

            DisplayName.Value = requestedName;
        }

        // ---------------------------------------------------------------
        // Server tarafı yardımcıları
        // ---------------------------------------------------------------

        /// <summary>Round başında MatchManager tarafından çağrılır.</summary>
        public void ServerResetForNewRound()
        {
            if (!IsServer) return;
            IsReady.Value = false;
        }

        /// <summary>
        /// Server tarafından doğrudan takım/slot ataması. Doluluk kontrolü yapmaz,
        /// çağıran tarafın benzersizliği garanti etmesi gerekir.
        /// </summary>
        public void ServerForceAssign(TeamId team, RoleSlot slot, bool ready)
        {
            if (!IsServer) return;

            Team.Value = team;
            Slot.Value = slot;
            IsReady.Value = ready;
        }


        /// <summary>Bu oyuncunun verilen taraftaki somut rolü.</summary>
        public RoundRole GetRole(TeamSide side)
        {
            return MatchRules.ResolveRole(side, Slot.Value);
        }

        // ---------------------------------------------------------------
        // Sorgular
        // ---------------------------------------------------------------

        public static bool IsSlotAvailable(TeamId team, RoleSlot slot, NetworkPlayerState requester = null)
        {
            for (int i = 0; i < Registry.Count; i++)
            {
                var player = Registry[i];
                if (player == null || player == requester) continue;
                if (player.Team.Value == team && player.Slot.Value == slot) return false;
            }

            return true;
        }

        public static int CountTeamMembers(TeamId team)
        {
            int count = 0;
            for (int i = 0; i < Registry.Count; i++)
            {
                if (Registry[i] != null && Registry[i].Team.Value == team) count++;
            }

            return count;
        }

        public static NetworkPlayerState Find(ulong clientId)
        {
            for (int i = 0; i < Registry.Count; i++)
            {
                if (Registry[i] != null && Registry[i].OwnerClientId == clientId) return Registry[i];
            }

            return null;
        }

        public static NetworkPlayerState Find(TeamId team, RoleSlot slot)
        {
            for (int i = 0; i < Registry.Count; i++)
            {
                var player = Registry[i];
                if (player != null && player.Team.Value == team && player.Slot.Value == slot) return player;
            }

            return null;
        }

        /// <summary>
        /// Maç başlatılabilir mi: dört oyuncu bağlı, her takımda ikisi, bütün slotlar
        /// benzersiz dolu ve herkes hazır.
        /// </summary>
        /// <summary>
        /// Maç başlatılabilir mi. requiredPlayers ile test sırasında daha az oyuncuya izin verilebilir.
        /// Her oyuncunun slotu olmalı, hepsi hazır olmalı ve takım/slot kombinasyonları benzersiz olmalı.
        /// </summary>
        public static bool CanStartMatch(int requiredPlayers = MatchRules.TotalPlayers)
        {
            if (requiredPlayers < 1) requiredPlayers = 1;
            if (Registry.Count < requiredPlayers) return false;

            int valid = 0;
            for (int i = 0; i < Registry.Count; i++)
            {
                var player = Registry[i];
                if (player == null) continue;
                if (player.Slot.Value == RoleSlot.None) return false;
                if (player.Team.Value == TeamId.None) return false;
                if (!player.IsReady.Value) return false;

                // Aynı takım/slot ikinci kez kullanılmamış olmalı.
                for (int j = i + 1; j < Registry.Count; j++)
                {
                    var other = Registry[j];
                    if (other == null) continue;
                    if (other.Team.Value == player.Team.Value && other.Slot.Value == player.Slot.Value) return false;
                }

                valid++;
            }

            return valid >= requiredPlayers;
        }

    }
}
