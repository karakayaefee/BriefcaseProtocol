using System;
using BriefcaseProtocol.Core;
using BriefcaseProtocol.Gameplay.Briefcases;
using BriefcaseProtocol.Networking;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Modules
{
    [RequireComponent(typeof(NetworkObject))]
    public abstract class BombModuleController : NetworkBehaviour
    {
        [SerializeField] protected BriefcaseController parentBriefcase;
        protected readonly NetworkVariable<bool> completed = new();

        public bool IsCompleted => completed.Value;
        public event Action<BombModuleController> Completed;

        protected bool CanOperate(ulong clientId)
        {
            return IsServer && !completed.Value && parentBriefcase != null &&
                parentBriefcase.Status == BriefcaseStatus.ConfirmedReal &&
                NetworkMatchManager.Instance != null &&
                NetworkMatchManager.Instance.IsRoleAllowed(clientId, GameplayRole.FieldAgent);
        }

        protected void Complete(ulong clientId)
        {
            if (completed.Value)
            {
                return;
            }

            completed.Value = true;
            NetworkMatchManager.Instance?.Publish(MatchEventType.ModuleCompleted, clientId, GetType().Name);
            Completed?.Invoke(this);
        }

        protected void Fail(ulong clientId, int penalty)
        {
            var match = NetworkMatchManager.Instance;
            match?.ApplyOperationPenalty(penalty, clientId, GetType().Name);
            match?.RegisterStrike(clientId, GetType().Name);
        }

        public virtual void ResetModule()
        {
            if (IsServer)
            {
                completed.Value = false;
            }
        }
    }
}
