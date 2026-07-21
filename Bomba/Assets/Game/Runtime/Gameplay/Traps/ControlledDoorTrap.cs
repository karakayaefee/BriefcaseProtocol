using System.Collections;
using System.Collections.Generic;
using BriefcaseProtocol.Core;
using BriefcaseProtocol.Gameplay.Player;
using BriefcaseProtocol.Networking;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Traps
{
    public sealed class ControlledDoorTrap : RemoteTrapController
    {
        [SerializeField] private Transform doorPanel;
        [SerializeField] private Vector3 openLocalPosition = new(0f, 3f, 0f);
        [SerializeField] private Vector3 closedLocalPosition = Vector3.zero;
        [SerializeField] private Light warningLight;
        private readonly HashSet<NetworkPlayerController> doorwayOccupants = new();
        private readonly NetworkVariable<bool> closed = new();

        protected override TrapKind Kind => TrapKind.ControlledDoor;
        protected override int InitialCharges => NetworkMatchManager.Instance != null
            ? NetworkMatchManager.Instance.Balance.controlledDoorCharges : 2;
        protected override float Cooldown => NetworkMatchManager.Instance != null
            ? NetworkMatchManager.Instance.Balance.controlledDoorCooldown : 30f;

        private void Awake()
        {
            if (doorPanel == null) doorPanel = transform;
        }

        protected override bool CanActivate() => doorwayOccupants.Count == 0 && !closed.Value;

        protected override void Activate()
        {
            StartCoroutine(DoorRoutine());
        }

        private IEnumerator DoorRoutine()
        {
            SetWarningClientRpc(true);
            var balance = NetworkMatchManager.Instance != null ? NetworkMatchManager.Instance.Balance : null;
            yield return new WaitForSecondsRealtime(balance != null ? balance.controlledDoorWarning : 1f);
            if (doorwayOccupants.Count > 0)
            {
                SetWarningClientRpc(false);
                yield break;
            }

            closed.Value = true;
            SetDoorClientRpc(true);
            yield return new WaitForSecondsRealtime(balance != null ? balance.controlledDoorDuration : 6f);
            closed.Value = false;
            SetDoorClientRpc(false);
            SetWarningClientRpc(false);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<NetworkPlayerController>() is { } player)
            {
                doorwayOccupants.Add(player);
            }
        }

        private void OnTriggerExit(Collider other)
        {
            if (other.GetComponentInParent<NetworkPlayerController>() is { } player)
            {
                doorwayOccupants.Remove(player);
            }
        }

        [ClientRpc]
        private void SetWarningClientRpc(bool enabled)
        {
            if (warningLight != null) warningLight.enabled = enabled;
        }

        [ClientRpc]
        private void SetDoorClientRpc(bool isClosed)
        {
            if (doorPanel != null)
            {
                doorPanel.localPosition = isClosed ? closedLocalPosition : openLocalPosition;
            }
        }

        protected override void ResetTrap()
        {
            base.ResetTrap();
            closed.Value = false;
            doorwayOccupants.Clear();
            if (doorPanel != null) doorPanel.localPosition = openLocalPosition;
        }
    }
}
