using System;
using System.Collections.Generic;
using BriefcaseProtocol.Core;
using BriefcaseProtocol.Networking;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Setup
{
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkBudgetManager : NetworkBehaviour
    {
        public static NetworkBudgetManager Instance { get; private set; }

        private readonly NetworkVariable<int> bombRemaining = new();
        private readonly NetworkVariable<int> decoyRemaining = new();
        private readonly Dictionary<ShopItemKind, int> purchases = new();

        public int BombRemaining => bombRemaining.Value;
        public int DecoyRemaining => decoyRemaining.Value;
        public event Action<ShopItemKind, bool> PurchaseResolved;

        private void Awake()
        {
            Instance = this;
        }

        public override void OnNetworkSpawn()
        {
            if (!IsServer)
            {
                return;
            }

            ResetBudgets();
            if (NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested += ResetBudgets;
            }
        }

        public override void OnNetworkDespawn()
        {
            if (IsServer && NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested -= ResetBudgets;
            }
        }

        public int Purchased(ShopItemKind item)
        {
            return purchases.TryGetValue(item, out var count) ? count : 0;
        }

        public void RequestPurchase(ShopItemKind item)
        {
            RequestPurchaseServerRpc(item);
        }

        [ServerRpc(InvokePermission = RpcInvokePermission.Everyone)]
        private void RequestPurchaseServerRpc(ShopItemKind item, ServerRpcParams rpc = default)
        {
            var match = NetworkMatchManager.Instance;
            if (match == null || match.State.Phase != MatchPhase.Setup)
            {
                PurchaseResultClientRpc(item, false);
                return;
            }

            var isBombItem = item is ShopItemKind.WireModule or ShopItemKind.SequenceModule;
            var expectedRole = isBombItem ? GameplayRole.BombMaker : GameplayRole.Trapper;
            if (!match.IsRoleAllowed(rpc.Receive.SenderClientId, expectedRole))
            {
                PurchaseResultClientRpc(item, false);
                return;
            }

            var cost = CostFor(item, match.Balance);
            var remaining = isBombItem ? bombRemaining.Value : decoyRemaining.Value;
            if (cost < 0 || remaining < cost || ReachedLimit(item))
            {
                PurchaseResultClientRpc(item, false);
                return;
            }

            if (isBombItem) bombRemaining.Value -= cost;
            else decoyRemaining.Value -= cost;
            purchases[item] = Purchased(item) + 1;
            PurchaseResultClientRpc(item, true);
        }

        public static int CostFor(ShopItemKind item, GameBalanceConfig balance)
        {
            return item switch
            {
                ShopItemKind.WireModule => balance.wireModuleCost,
                ShopItemKind.SequenceModule => balance.sequenceModuleCost,
                ShopItemKind.FakeBriefcase => balance.fakeBriefcaseCost,
                ShopItemKind.FakeWirePanel => 10,
                ShopItemKind.FakeKeypad => 15,
                ShopItemKind.FakeLed => 5,
                ShopItemKind.FakeTimer => 15,
                ShopItemKind.WeakSignal => 20,
                ShopItemKind.SoundLure => balance.soundLureCost,
                ShopItemKind.ControlledDoor => balance.controlledDoorCost,
                _ => -1
            };
        }

        private bool ReachedLimit(ShopItemKind item)
        {
            var maximum = item switch
            {
                ShopItemKind.WireModule => 1,
                ShopItemKind.SequenceModule => 1,
                ShopItemKind.FakeBriefcase => 1,
                ShopItemKind.SoundLure => 1,
                ShopItemKind.ControlledDoor => 1,
                _ => 3
            };
            return Purchased(item) >= maximum;
        }

        private void ResetBudgets()
        {
            var balance = NetworkMatchManager.Instance != null
                ? NetworkMatchManager.Instance.Balance
                : ScriptableObject.CreateInstance<GameBalanceConfig>();
            bombRemaining.Value = balance.bombBudget;
            decoyRemaining.Value = balance.decoyBudget;
            purchases.Clear();
        }

        [ClientRpc]
        private void PurchaseResultClientRpc(ShopItemKind item, bool succeeded)
        {
            PurchaseResolved?.Invoke(item, succeeded);
        }
    }
}
