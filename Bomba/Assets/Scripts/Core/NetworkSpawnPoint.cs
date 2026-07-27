using UnityEngine;

namespace BriefcaseProtocol.Core
{
    /// <summary>
    /// Marks a safe scene position for an NGO player object. The player owner selects
    /// a deterministic point from its client id, so all clients use distinct starts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkSpawnPoint : MonoBehaviour
    {
        [SerializeField] private int order;

        public int Order => order;

        public static int Compare(NetworkSpawnPoint left, NetworkSpawnPoint right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left == null) return 1;
            if (right == null) return -1;

            int orderComparison = left.order.CompareTo(right.order);
            return orderComparison != 0
                ? orderComparison
                : string.CompareOrdinal(left.name, right.name);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.9f, 0.35f);
            Gizmos.DrawLine(
                transform.position + Vector3.up * 0.9f,
                transform.position + Vector3.up * 0.9f + transform.forward);
        }
#endif
    }
}
