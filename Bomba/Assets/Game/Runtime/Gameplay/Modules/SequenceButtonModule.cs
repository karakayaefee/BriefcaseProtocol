using BriefcaseProtocol.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Modules
{
    public sealed class SequenceButtonModule : BombModuleController
    {
        [SerializeField, Range(3, 5)] private int buttonCount = 4;
        [SerializeField] private int seedOffset = 211;
        private readonly NetworkVariable<FixedString32Bytes> sequence = new();
        private readonly NetworkVariable<int> progress = new();

        public string SequenceDebug => sequence.Value.ToString();
        public int Progress => progress.Value;

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

        public void Press(int buttonIndex)
        {
            PressServerRpc(buttonIndex);
        }

        [ServerRpc(InvokePermission = RpcInvokePermission.Everyone)]
        private void PressServerRpc(int buttonIndex, ServerRpcParams rpc = default)
        {
            if (!CanOperate(rpc.Receive.SenderClientId) || buttonIndex < 0 || buttonIndex >= buttonCount)
            {
                return;
            }

            var expected = sequence.Value[progress.Value] - '0';
            if (buttonIndex != expected)
            {
                progress.Value = 0;
                Fail(rpc.Receive.SenderClientId, NetworkMatchManager.Instance.Balance.wrongSequencePenalty);
                return;
            }

            progress.Value++;
            if (progress.Value >= sequence.Value.Length)
            {
                Complete(rpc.Receive.SenderClientId);
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
            var random = new System.Random(seedOffset + round * 4021);
            var value = new FixedString32Bytes();
            for (var i = 0; i < buttonCount; i++)
            {
                value.Append((char)('0' + random.Next(0, buttonCount)));
            }

            sequence.Value = value;
            progress.Value = 0;
        }
    }
}
