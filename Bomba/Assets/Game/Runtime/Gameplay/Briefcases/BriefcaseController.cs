using System;
using BriefcaseProtocol.Core;
using BriefcaseProtocol.Gameplay.Interactions;
using BriefcaseProtocol.Networking;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Briefcases
{
    [RequireComponent(typeof(NetworkObject), typeof(Collider))]
    public sealed class BriefcaseController : NetworkBehaviour, IInteractable
    {
        [SerializeField] private BriefcaseKind kind = BriefcaseKind.Real;
        [SerializeField] private CombinationRuleKind combinationRule = CombinationRuleKind.ColorTag;
        [SerializeField] private int seedOffset;
        [SerializeField] private Animator animator;

        private readonly NetworkVariable<BriefcaseStatus> status = new(BriefcaseStatus.Hidden);
        private readonly NetworkVariable<FixedString64Bytes> clue = new();
        private readonly NetworkVariable<byte> label = new();
        private readonly NetworkVariable<int> wrongAttempts = new();
        private int correctCode;

        public static event Action<BriefcaseController> CodeEntryRequested;
        public BriefcaseStatus Status => status.Value;
        public BriefcaseKind Kind => kind;
        public string Label => label.Value == 0 ? "?" : ((char)label.Value).ToString();
        public string Clue => clue.Value.ToString();
        public string PromptKey => status.Value is BriefcaseStatus.Hidden or BriefcaseStatus.Discovered
            ? "interaction.use"
            : status.Value == BriefcaseStatus.Locked ? "interaction.use" : string.Empty;

        public override void OnNetworkSpawn()
        {
            status.OnValueChanged += OnStatusChanged;
            if (IsServer)
            {
                ConfigureForRound(NetworkMatchManager.Instance != null ? NetworkMatchManager.Instance.State.RoundIndex : 0);
                if (NetworkMatchManager.Instance != null)
                {
                    NetworkMatchManager.Instance.ServerRoundResetRequested += ResetForRound;
                }
            }

            OnStatusChanged(status.Value, status.Value);
        }

        public override void OnNetworkDespawn()
        {
            status.OnValueChanged -= OnStatusChanged;
            if (IsServer && NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested -= ResetForRound;
            }
        }

        public bool CanInteract(InteractionContext context)
        {
            return context.Role == GameplayRole.FieldAgent &&
                status.Value is BriefcaseStatus.Hidden or BriefcaseStatus.Discovered or BriefcaseStatus.Locked;
        }

        public void Interact(InteractionContext context)
        {
            if (!CanInteract(context))
            {
                return;
            }

            if (status.Value is BriefcaseStatus.Hidden or BriefcaseStatus.Discovered)
            {
                DiscoverServerRpc();
                return;
            }

            CodeEntryRequested?.Invoke(this);
        }

        public void SubmitCode(int code)
        {
            SubmitCodeServerRpc(Mathf.Clamp(code, 0, 999));
        }

        [ServerRpc(InvokePermission = RpcInvokePermission.Everyone)]
        private void DiscoverServerRpc(ServerRpcParams rpc = default)
        {
            var match = NetworkMatchManager.Instance;
            if (match == null || !match.IsRoleAllowed(rpc.Receive.SenderClientId, GameplayRole.FieldAgent))
            {
                return;
            }

            if (label.Value == 0)
            {
                var assigned = BriefcaseRegistry.Instance != null ? BriefcaseRegistry.Instance.AssignNext() : '?';
                label.Value = (byte)assigned;
                match.Publish(MatchEventType.BriefcaseDiscovered, rpc.Receive.SenderClientId, assigned.ToString());
            }

            status.Value = BriefcaseStatus.Locked;
        }

        [ServerRpc(InvokePermission = RpcInvokePermission.Everyone)]
        private void SubmitCodeServerRpc(int code, ServerRpcParams rpc = default)
        {
            var match = NetworkMatchManager.Instance;
            if (match == null || status.Value != BriefcaseStatus.Locked ||
                !match.IsRoleAllowed(rpc.Receive.SenderClientId, GameplayRole.FieldAgent))
            {
                return;
            }

            if (code == correctCode)
            {
                status.Value = BriefcaseStatus.Open;
                match.Publish(MatchEventType.BriefcaseOpened, rpc.Receive.SenderClientId, Label);
                status.Value = kind == BriefcaseKind.Real ? BriefcaseStatus.ConfirmedReal : BriefcaseStatus.ConfirmedFake;
                match.Publish(kind == BriefcaseKind.Real ? MatchEventType.RealConfirmed : MatchEventType.FakeConfirmed,
                    rpc.Receive.SenderClientId, Label);
                return;
            }

            var penalty = match.Balance.WrongCodePenalty(wrongAttempts.Value);
            wrongAttempts.Value++;
            match.ApplyOperationPenalty(penalty, rpc.Receive.SenderClientId, Label);
            match.RegisterStrike(rpc.Receive.SenderClientId, Label);
        }

        private void ResetForRound()
        {
            ConfigureForRound(NetworkMatchManager.Instance != null ? NetworkMatchManager.Instance.State.RoundIndex : 0);
        }

        private void ConfigureForRound(int roundIndex)
        {
            var generated = CombinationResolver.Generate(combinationRule, roundIndex * 7919 + seedOffset + 17);
            correctCode = generated.Code;
            clue.Value = new FixedString64Bytes(generated.Clue.DisplayValue);
            wrongAttempts.Value = 0;
            label.Value = 0;
            status.Value = BriefcaseStatus.Hidden;
        }

        private void OnStatusChanged(BriefcaseStatus previous, BriefcaseStatus current)
        {
            if (animator != null)
            {
                animator.SetBool("Open", current >= BriefcaseStatus.Open);
            }
        }
    }
}
