using BriefcaseProtocol.Core;
using BriefcaseProtocol.Networking;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Traps
{
    [RequireComponent(typeof(NetworkObject))]
    public abstract class RemoteTrapController : NetworkBehaviour
    {
        protected readonly NetworkVariable<int> charges = new();
        protected readonly NetworkVariable<double> cooldownEndsAt = new();

        public int Charges => charges.Value;
        public double CooldownEndsAt => cooldownEndsAt.Value;
        protected abstract TrapKind Kind { get; }
        protected abstract int InitialCharges { get; }
        protected abstract float Cooldown { get; }

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                ResetTrap();
                if (NetworkMatchManager.Instance != null)
                {
                    NetworkMatchManager.Instance.ServerRoundResetRequested += ResetTrap;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested -= ResetTrap;
            }
        }

        public void RequestTrigger()
        {
            TriggerServerRpc();
        }

        [ServerRpc(InvokePermission = RpcInvokePermission.Everyone)]
        private void TriggerServerRpc(ServerRpcParams rpc = default)
        {
            var match = NetworkMatchManager.Instance;
            if (match == null || match.State.Phase != MatchPhase.Operation || charges.Value <= 0 ||
                match.ServerNow < cooldownEndsAt.Value || !match.IsRoleAllowed(rpc.Receive.SenderClientId, GameplayRole.Trapper) ||
                !CanActivate())
            {
                return;
            }

            charges.Value--;
            cooldownEndsAt.Value = match.ServerNow + Cooldown;
            Activate();
            match.Publish(MatchEventType.TrapTriggered, rpc.Receive.SenderClientId, Kind.ToString(), charges.Value);
        }

        protected abstract bool CanActivate();
        protected abstract void Activate();

        protected virtual void ResetTrap()
        {
            charges.Value = InitialCharges;
            cooldownEndsAt.Value = 0d;
        }
    }
}
