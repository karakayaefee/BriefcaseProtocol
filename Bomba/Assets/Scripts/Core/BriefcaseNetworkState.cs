using System;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Core
{
    /// <summary>
    /// Server-authoritative state for the single shared briefcase in the Game scene.
    /// The visual briefcase stays local during inspection while its lock, dials and
    /// open/closed state are replicated to every connected player.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BriefcaseNetworkState : NetworkBehaviour
    {
        private const int DigitCount = 10;

        [Header("Combination")]
        [SerializeField, Range(0, DigitCount - 1)] private int correctDigit1 = 1;
        [SerializeField, Range(0, DigitCount - 1)] private int correctDigit2 = 2;
        [SerializeField, Range(0, DigitCount - 1)] private int correctDigit3 = 3;

        public static BriefcaseNetworkState Instance { get; private set; }
        public static event Action<BriefcaseNetworkState> InstanceChanged;

        public readonly NetworkVariable<bool> IsOpen = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<bool> IsUnlocked = new NetworkVariable<bool>(
            false,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> Digit1 = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> Digit2 = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public readonly NetworkVariable<int> Digit3 = new NetworkVariable<int>(
            0,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

        public event Action StateChanged;

        public bool HasCorrectCombination =>
            Digit1.Value == correctDigit1 &&
            Digit2.Value == correctDigit2 &&
            Digit3.Value == correctDigit3;

        public override void OnNetworkSpawn()
        {
            Instance = this;
            IsOpen.OnValueChanged += HandleBoolChanged;
            IsUnlocked.OnValueChanged += HandleBoolChanged;
            Digit1.OnValueChanged += HandleDigitChanged;
            Digit2.OnValueChanged += HandleDigitChanged;
            Digit3.OnValueChanged += HandleDigitChanged;

            InstanceChanged?.Invoke(this);
            StateChanged?.Invoke();
        }

        public override void OnNetworkDespawn()
        {
            IsOpen.OnValueChanged -= HandleBoolChanged;
            IsUnlocked.OnValueChanged -= HandleBoolChanged;
            Digit1.OnValueChanged -= HandleDigitChanged;
            Digit2.OnValueChanged -= HandleDigitChanged;
            Digit3.OnValueChanged -= HandleDigitChanged;

            if (Instance == this)
            {
                Instance = null;
                InstanceChanged?.Invoke(null);
            }
        }

        public void RequestToggleOpen()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                ServerToggleOpen();
            }
            else
            {
                RequestToggleOpenRpc();
            }
        }

        public void RequestDialStep(int dialIndex, int valueDelta)
        {
            if (!IsSpawned || dialIndex < 0 || dialIndex > 2 || valueDelta == 0)
            {
                return;
            }

            int normalizedDelta = valueDelta < 0 ? -1 : 1;
            if (IsServer)
            {
                ServerApplyDialStep(dialIndex, normalizedDelta);
            }
            else
            {
                RequestDialStepRpc(dialIndex, normalizedDelta);
            }
        }

        public void RequestUnlock()
        {
            if (!IsSpawned)
            {
                return;
            }

            if (IsServer)
            {
                ServerTryUnlock();
            }
            else
            {
                RequestUnlockRpc();
            }
        }

        [Rpc(SendTo.Server)]
        private void RequestToggleOpenRpc()
        {
            ServerToggleOpen();
        }

        [Rpc(SendTo.Server)]
        private void RequestDialStepRpc(int dialIndex, int valueDelta)
        {
            if (dialIndex < 0 || dialIndex > 2 || valueDelta == 0)
            {
                return;
            }

            ServerApplyDialStep(dialIndex, valueDelta < 0 ? -1 : 1);
        }

        [Rpc(SendTo.Server)]
        private void RequestUnlockRpc()
        {
            ServerTryUnlock();
        }

        private void ServerToggleOpen()
        {
            if (!IsServer || (!IsOpen.Value && !IsUnlocked.Value))
            {
                return;
            }

            IsOpen.Value = !IsOpen.Value;
        }

        private void ServerApplyDialStep(int dialIndex, int valueDelta)
        {
            if (!IsServer || IsUnlocked.Value)
            {
                return;
            }

            switch (dialIndex)
            {
                case 0:
                    Digit1.Value = WrapDigit(Digit1.Value + valueDelta);
                    break;
                case 1:
                    Digit2.Value = WrapDigit(Digit2.Value + valueDelta);
                    break;
                case 2:
                    Digit3.Value = WrapDigit(Digit3.Value + valueDelta);
                    break;
            }
        }

        private void ServerTryUnlock()
        {
            if (!IsServer || IsUnlocked.Value || !HasCorrectCombination)
            {
                return;
            }

            IsUnlocked.Value = true;
            IsOpen.Value = true;
        }

        private void HandleBoolChanged(bool previousValue, bool currentValue)
        {
            StateChanged?.Invoke();
        }

        private void HandleDigitChanged(int previousValue, int currentValue)
        {
            StateChanged?.Invoke();
        }

        private static int WrapDigit(int value)
        {
            return (value % DigitCount + DigitCount) % DigitCount;
        }
    }
}
