using BriefcaseProtocol.Core;

namespace BriefcaseProtocol.Gameplay.Interactions
{
    public readonly struct InteractionContext
    {
        public ulong ClientId { get; }
        public GameplayRole Role { get; }

        public InteractionContext(ulong clientId, GameplayRole role)
        {
            ClientId = clientId;
            Role = role;
        }
    }

    public interface IInteractable
    {
        string PromptKey { get; }
        bool CanInteract(InteractionContext context);
        void Interact(InteractionContext context);
    }
}
