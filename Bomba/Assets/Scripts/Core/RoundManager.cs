using System;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Core
{
    /// <summary>
    /// Round içindeki aşama akışını ve server timer'ını yürütür.
    /// Timer mantığı: server yalnızca aşamanın BİTİŞ ZAMANINI yazar, geri sayımı her client
    /// kendi tarafında NetworkManager.ServerTime üzerinden hesaplar. Böylece saniye başına
    /// senkron paketi gitmez ve istemciler arası fark ağ gecikmesinden bağımsız kalır.
    /// </summary>
    public class RoundManager : NetworkBehaviour
    {
        public static RoundManager Instance { get; private set; }

        public readonly NetworkVariable<MatchPhase> Phase =
            new NetworkVariable<MatchPhase>(
                MatchPhase.Lobby,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        /// <summary>Aşamanın bitmesi gereken server zamanı. 0 ise aşama süresizdir.</summary>
        public readonly NetworkVariable<double> PhaseEndServerTime =
            new NetworkVariable<double>(
                0d,
                NetworkVariableReadPermission.Everyone,
                NetworkVariableWritePermission.Server);

        /// <summary>Aşama değiştiğinde tetiklenir. UI bunu dinler.</summary>
        public event Action<MatchPhase, MatchPhase> OnPhaseChanged;

        /// <summary>Aşamanın bitmesine kalan saniye. Süresiz aşamalarda 0 döner.</summary>
        public float RemainingSeconds
        {
            get
            {
                if (PhaseEndServerTime.Value <= 0d) return 0f;
                if (NetworkManager == null) return 0f;

                double remaining = PhaseEndServerTime.Value - NetworkManager.ServerTime.Time;
                return remaining <= 0d ? 0f : (float)remaining;
            }
        }

        public bool HasTimer => PhaseEndServerTime.Value > 0d;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            Phase.OnValueChanged += HandlePhaseChanged;
        }

        public override void OnNetworkDespawn()
        {
            Phase.OnValueChanged -= HandlePhaseChanged;
            if (Instance == this) Instance = null;
        }

        void HandlePhaseChanged(MatchPhase previous, MatchPhase current)
        {
            OnPhaseChanged?.Invoke(previous, current);
        }

        void Update()
        {
            if (!IsSpawned || !IsServer || NetworkManager == null) return;

            // Süresiz aşamalar dışarıdan tetiklenir (Lobby, MatchResults).
            if (PhaseEndServerTime.Value <= 0d) return;

            if (NetworkManager.ServerTime.Time >= PhaseEndServerTime.Value)
            {
                ServerAdvancePhase();
            }
        }

        // ---------------------------------------------------------------
        // Server API
        // ---------------------------------------------------------------

        /// <summary>Yeni round'u ilk aşamasından başlatır.</summary>
        public void ServerBeginRound()
        {
            if (!IsServer) return;
            ServerSetPhase(MatchPhase.RoleReveal);
        }

        /// <summary>Aşamayı doğrudan ayarlar ve timer'ı kurar.</summary>
        public void ServerSetPhase(MatchPhase phase)
        {
            if (!IsServer) return;

            Phase.Value = phase;

            float duration = MatchRules.GetPhaseDuration(phase);
            PhaseEndServerTime.Value = duration > 0f
                ? NetworkManager.ServerTime.Time + duration
                : 0d;
        }

        /// <summary>
        /// Aşamayı süresi dolmadan bitirir. Örnek: çözücüler bombayı erken etkisiz hale getirdi,
        /// Operation'ı beklemeden Reveal'e geç.
        /// </summary>
        public void ServerForceAdvance()
        {
            if (!IsServer) return;
            ServerAdvancePhase();
        }

        void ServerAdvancePhase()
        {
            MatchPhase current = Phase.Value;
            MatchPhase next = MatchRules.GetNextPhase(current);

            if (next != MatchPhase.None)
            {
                ServerSetPhase(next);
                return;
            }

            // Doğrusal akışın sonu. RoundResults bittiyse karar maç seviyesine ait.
            if (current == MatchPhase.RoundResults && MatchManager.Instance != null)
            {
                MatchManager.Instance.ServerAdvanceAfterRoundResults();
            }
        }
    }
}
