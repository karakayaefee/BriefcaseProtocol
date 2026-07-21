using UnityEngine;

namespace BriefcaseProtocol.Core
{
    [CreateAssetMenu(fileName = "GameBalance", menuName = "Briefcase Protocol/Game Balance")]
    public sealed class GameBalanceConfig : ScriptableObject
    {
        [Header("Phase durations (seconds)")]
        [Min(1)] public int roleRevealSeconds = 10;
        [Min(1)] public int setupSeconds = 120;
        [Min(1)] public int preparationSeconds = 30;
        [Min(1)] public int operationSeconds = 300;
        [Min(1)] public int revealSeconds = 25;
        [Min(1)] public int resultsSeconds = 20;

        [Header("Budgets")]
        [Min(0)] public int bombBudget = 100;
        [Min(0)] public int decoyBudget = 100;
        [Min(0)] public int wireModuleCost = 20;
        [Min(0)] public int sequenceModuleCost = 25;
        [Min(0)] public int fakeBriefcaseCost = 30;
        [Min(0)] public int soundLureCost = 15;
        [Min(0)] public int controlledDoorCost = 15;

        [Header("Penalties")]
        [Min(1)] public int firstWrongCodePenalty = 5;
        [Min(1)] public int secondWrongCodePenalty = 10;
        [Min(1)] public int repeatedWrongCodePenalty = 15;
        [Min(1)] public int wrongWirePenalty = 10;
        [Min(1)] public int wrongSequencePenalty = 5;
        [Min(1)] public int strikeLimit = 5;

        [Header("Remote traps")]
        [Min(1)] public int soundLureCharges = 3;
        [Min(0.1f)] public float soundLureDuration = 4f;
        [Min(0.1f)] public float soundLureCooldown = 20f;
        [Min(1)] public int controlledDoorCharges = 2;
        [Min(0.1f)] public float controlledDoorWarning = 1f;
        [Min(0.1f)] public float controlledDoorDuration = 6f;
        [Min(0.1f)] public float controlledDoorCooldown = 30f;

        public double DurationFor(MatchPhase phase)
        {
            return phase switch
            {
                MatchPhase.RoleReveal => roleRevealSeconds,
                MatchPhase.Setup => setupSeconds,
                MatchPhase.Preparation => preparationSeconds,
                MatchPhase.Operation => operationSeconds,
                MatchPhase.Reveal => revealSeconds,
                MatchPhase.Results => resultsSeconds,
                _ => 0d
            };
        }

        public int WrongCodePenalty(int previousAttempts)
        {
            return previousAttempts switch
            {
                <= 0 => firstWrongCodePenalty,
                1 => secondWrongCodePenalty,
                _ => repeatedWrongCodePenalty
            };
        }
    }
}
