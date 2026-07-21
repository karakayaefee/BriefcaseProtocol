using System.Collections.Generic;
using BriefcaseProtocol.Networking;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Briefcases
{
    public sealed class BriefcaseRegistry : MonoBehaviour
    {
        public static BriefcaseRegistry Instance { get; private set; }
        private readonly HashSet<char> assigned = new();

        private void Awake()
        {
            Instance = this;
        }

        private void OnEnable()
        {
            if (NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested += ResetRegistry;
            }
        }

        private void Start()
        {
            if (NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested -= ResetRegistry;
                NetworkMatchManager.Instance.ServerRoundResetRequested += ResetRegistry;
            }
        }

        private void OnDisable()
        {
            if (NetworkMatchManager.Instance != null)
            {
                NetworkMatchManager.Instance.ServerRoundResetRequested -= ResetRegistry;
            }
        }

        public char AssignNext()
        {
            foreach (var label in new[] { 'A', 'B', 'C' })
            {
                if (assigned.Add(label))
                {
                    return label;
                }
            }

            return '?';
        }

        public void ResetRegistry()
        {
            assigned.Clear();
        }
    }
}
