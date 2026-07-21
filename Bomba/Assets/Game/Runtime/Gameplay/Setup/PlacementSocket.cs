using BriefcaseProtocol.Core;
using BriefcaseProtocol.Networking;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Setup
{
    public enum PlacementCategory : byte
    {
        Briefcase,
        Module,
        Trap
    }

    [RequireComponent(typeof(Collider))]
    public sealed class PlacementSocket : NetworkBehaviour
    {
        [SerializeField] private PlacementCategory category;
        [SerializeField] private bool spawnOrCriticalRoute;
        private readonly NetworkVariable<ulong> occupantNetworkId = new(ulong.MaxValue);

        public bool IsOccupied => occupantNetworkId.Value != ulong.MaxValue;
        public PlacementCategory Category => category;

        public bool TryPlace(NetworkObject target, PlacementCategory requestedCategory)
        {
            if (!IsServer || target == null || requestedCategory != category || IsOccupied ||
                (spawnOrCriticalRoute && category == PlacementCategory.Trap))
            {
                return false;
            }

            target.transform.SetPositionAndRotation(transform.position, transform.rotation);
            occupantNetworkId.Value = target.NetworkObjectId;
            return true;
        }

        public void Clear()
        {
            if (IsServer) occupantNetworkId.Value = ulong.MaxValue;
        }
    }
}
