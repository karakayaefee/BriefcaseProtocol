using BriefcaseProtocol.Core;
using BriefcaseProtocol.Gameplay.Interactions;
using UnityEngine;

namespace BriefcaseProtocol.Gameplay.Modules
{
    public sealed class WireInteractionNode : MonoBehaviour, IInteractable
    {
        [SerializeField] private WireLogicModule module;
        [SerializeField] private int wireIndex;
        public string PromptKey => "interaction.use";

        public void Configure(WireLogicModule target, int index)
        {
            module = target;
            wireIndex = index;
        }

        public bool CanInteract(InteractionContext context)
        {
            return module != null && !module.IsCompleted && context.Role == GameplayRole.FieldAgent;
        }

        public void Interact(InteractionContext context)
        {
            if (CanInteract(context)) module.CutWire(wireIndex);
        }
    }

    public sealed class SequenceInteractionNode : MonoBehaviour, IInteractable
    {
        [SerializeField] private SequenceButtonModule module;
        [SerializeField] private int buttonIndex;
        public string PromptKey => "interaction.use";

        public void Configure(SequenceButtonModule target, int index)
        {
            module = target;
            buttonIndex = index;
        }

        public bool CanInteract(InteractionContext context)
        {
            return module != null && !module.IsCompleted && context.Role == GameplayRole.FieldAgent;
        }

        public void Interact(InteractionContext context)
        {
            if (CanInteract(context)) module.Press(buttonIndex);
        }
    }
}
