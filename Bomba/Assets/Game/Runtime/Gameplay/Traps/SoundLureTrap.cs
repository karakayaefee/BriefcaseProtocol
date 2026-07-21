using System.Collections;
using BriefcaseProtocol.Core;
using BriefcaseProtocol.Networking;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Traps
{
    public sealed class SoundLureTrap : RemoteTrapController
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Light indicator;
        private readonly NetworkVariable<bool> active = new();

        protected override TrapKind Kind => TrapKind.SoundLure;
        protected override int InitialCharges => NetworkMatchManager.Instance != null
            ? NetworkMatchManager.Instance.Balance.soundLureCharges : 3;
        protected override float Cooldown => NetworkMatchManager.Instance != null
            ? NetworkMatchManager.Instance.Balance.soundLureCooldown : 20f;

        protected override bool CanActivate() => !active.Value;

        protected override void Activate()
        {
            StartCoroutine(ActiveRoutine());
        }

        private IEnumerator ActiveRoutine()
        {
            active.Value = true;
            SetPresentationClientRpc(true);
            var duration = NetworkMatchManager.Instance != null
                ? NetworkMatchManager.Instance.Balance.soundLureDuration : 4f;
            yield return new WaitForSecondsRealtime(duration);
            active.Value = false;
            SetPresentationClientRpc(false);
        }

        [ClientRpc]
        private void SetPresentationClientRpc(bool enabled)
        {
            if (indicator != null) indicator.enabled = enabled;
            if (audioSource == null) return;
            if (enabled) audioSource.Play();
            else audioSource.Stop();
        }
    }
}
