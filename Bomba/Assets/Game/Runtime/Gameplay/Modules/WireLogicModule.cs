using BriefcaseProtocol.Networking;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Modules
{
    public sealed class WireLogicModule : BombModuleController
    {
        [SerializeField, Range(4, 6)] private int wireCount = 5;
        [SerializeField] private int seedOffset = 101;
        private readonly NetworkVariable<int> correctWire = new();

        public int WireCount => wireCount;
        public int CorrectWireDebug => correctWire.Value;

        public override void OnNetworkSpawn()
        {
            if (IsServer)
            {
                Generate();
                if (NetworkMatchManager.Instance != null)
                {
                    NetworkMatchManager.Instance.ServerRoundResetRequested += ResetModule;
                }
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested -= ResetModule;
            }
        }

        public void CutWire(int index)
        {
            CutWireServerRpc(index);
        }

        [ServerRpc(InvokePermission = RpcInvokePermission.Everyone)]
        private void CutWireServerRpc(int index, ServerRpcParams rpc = default)
        {
            if (!CanOperate(rpc.Receive.SenderClientId) || index < 0 || index >= wireCount)
            {
                return;
            }

            if (index == correctWire.Value)
            {
                Complete(rpc.Receive.SenderClientId);
            }
            else
            {
                Fail(rpc.Receive.SenderClientId, NetworkMatchManager.Instance.Balance.wrongWirePenalty);
                Generate();
            }
        }

        public override void ResetModule()
        {
            base.ResetModule();
            if (IsServer) Generate();
        }

        private void Generate()
        {
            var round = NetworkMatchManager.Instance != null ? NetworkMatchManager.Instance.State.RoundIndex : 0;
            correctWire.Value = new System.Random(seedOffset + round * 3571).Next(0, wireCount);
        }
    }
}
