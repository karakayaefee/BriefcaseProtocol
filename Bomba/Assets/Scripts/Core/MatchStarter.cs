using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Core
{
    /// <summary>
    /// Game sahnesinde durur. Sahne yüklenip oyuncu state'leri spawn olduktan sonra
    /// server tarafında maçı başlatır. Lobby'deki Başlat butonu yalnız sahneyi yükler;
    /// maçın kendisi burada başlar.
    /// </summary>
    public class MatchStarter : NetworkBehaviour
    {
        [Tooltip("Maçın başlaması için gereken oyuncu sayısı. Test için düşürülebilir.")]
        [SerializeField] int requiredPlayers = 1;

        [Tooltip("Slot seçim arayüzü gelene kadar takım/slot otomatik dağıtılsın mı.")]
        [SerializeField] bool autoAssignSlots = true;

        [Tooltip("Oyuncu state'lerinin spawn olmasını beklemek için gecikme (saniye).")]
        [SerializeField] float startDelay = 1f;

        float timer;
        bool done;

        public override void OnNetworkSpawn()
        {
            timer = 0f;
            done = false;
        }

        void Update()
        {
            if (done || !IsSpawned || !IsServer) return;

            timer += Time.deltaTime;
            if (timer < startDelay) return;

            var match = MatchManager.Instance;
            if (match == null)
            {
                Debug.LogError("[MatchStarter] MatchManager yok. MatchSystems bu sahnede mi?");
                done = true;
                return;
            }

            if (match.MatchInProgress.Value)
            {
                done = true;
                return;
            }

            match.ServerStartMatch(autoAssignSlots, requiredPlayers);

            // Basarisiz olduysa bir sonraki karede tekrar denenmesin, log spam olur.
            done = true;
        }
    }
}
