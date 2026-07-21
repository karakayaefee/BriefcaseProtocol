using BriefcaseProtocol.Networking;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Modules
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class BombModuleSet : NetworkBehaviour
    {
        [SerializeField] private BombModuleController[] requiredModules;
        private bool reported;

        private void Update()
        {
            if (!IsServer || reported || requiredModules == null || requiredModules.Length < 2)
            {
                return;
            }

            foreach (var module in requiredModules)
            {
                if (module == null || !module.IsCompleted)
                {
                    return;
                }
            }

            reported = true;
            NetworkMatchManager.Instance?.FinishDefusal(0);
        }

        public override void OnNetworkSpawn()
        {
            if (IsServer && NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested += ResetSet;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested -= ResetSet;
            }
        }

        private void ResetSet()
        {
            reported = false;
        }
    }
}
