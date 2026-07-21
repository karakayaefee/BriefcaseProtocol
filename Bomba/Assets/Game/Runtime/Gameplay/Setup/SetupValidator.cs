using BriefcaseProtocol.Core;
using BriefcaseProtocol.Gameplay.Briefcases;
using BriefcaseProtocol.Gameplay.Modules;
using BriefcaseProtocol.Gameplay.Traps;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Setup
{
    public sealed class SetupValidator : MonoBehaviour
    {
        public static SetupValidator Instance { get; private set; }

        [SerializeField] private BriefcaseController realBriefcase;
        [SerializeField] private BriefcaseController fakeBriefcase;
        [SerializeField] private WireLogicModule wireModule;
        [SerializeField] private SequenceButtonModule sequenceModule;
        [SerializeField] private SoundLureTrap soundLure;
        [SerializeField] private ControlledDoorTrap controlledDoor;

        private void Awake()
        {
            Instance = this;
        }

        public bool IsValid(TeamId builderTeam)
        {
            if (builderTeam == TeamId.None || realBriefcase == null || fakeBriefcase == null || wireModule == null ||
                sequenceModule == null || soundLure == null || controlledDoor == null)
            {
                return false;
            }

            var budget = NetworkBudgetManager.Instance;
            if (budget == null)
            {
                return true;
            }

            return budget.Purchased(ShopItemKind.WireModule) == 1 &&
                   budget.Purchased(ShopItemKind.SequenceModule) == 1 &&
                   budget.Purchased(ShopItemKind.FakeBriefcase) == 1 &&
                   budget.Purchased(ShopItemKind.SoundLure) == 1 &&
                   budget.Purchased(ShopItemKind.ControlledDoor) == 1;
        }
    }
}
