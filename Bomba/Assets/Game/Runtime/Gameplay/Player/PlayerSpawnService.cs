using System.Collections;
using Unity.Netcode;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Player
{
    public sealed class PlayerSpawnService : MonoBehaviour
    {
        [SerializeField] private Transform[] spawnPoints;

        private IEnumerator Start()
        {
            yield return null;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer || spawnPoints == null || spawnPoints.Length == 0)
            {
                yield break;
            }

            var index = 0;
            foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
            {
                if (client.PlayerObject == null) continue;
                var spawn = spawnPoints[index % spawnPoints.Length];
                client.PlayerObject.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
                index++;
            }
        }
    }
}
