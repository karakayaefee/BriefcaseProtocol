using System;

namespace BriefcaseProtocol.Core
{
    public sealed class MatchStateMachine
    {
        public MatchPhase Phase { get; private set; } = MatchPhase.Lobby;
        public int RoundIndex { get; private set; }
        public TeamId BuilderTeam { get; private set; } = TeamId.None;
        public TeamId SolverTeam => RoleRules.Opponent(BuilderTeam);
        public double PhaseEndsAt { get; private set; }
        public bool IsComplete => Phase == MatchPhase.Results && RoundIndex >= 1;

        public void StartMatch(TeamId firstBuilder, double now, GameBalanceConfig balance)
        {
            if (firstBuilder == TeamId.None)
            {
                throw new ArgumentException("A builder team is required.", nameof(firstBuilder));
            }

            RoundIndex = 0;
            BuilderTeam = firstBuilder;
            Enter(MatchPhase.RoleReveal, now, balance);
        }

        public bool Tick(double now, GameBalanceConfig balance)
        {
            if (Phase is MatchPhase.Lobby or MatchPhase.Results || now < PhaseEndsAt)
            {
                return false;
            }

            Advance(now, balance);
            return true;
        }

        public void FinishOperation(double now, GameBalanceConfig balance)
        {
            if (Phase != MatchPhase.Operation)
            {
                throw new InvalidOperationException("Only an operation phase can be finished early.");
            }

            Enter(MatchPhase.Reveal, now, balance);
        }

        public void ApplyTimePenalty(double seconds, double now)
        {
            if (Phase != MatchPhase.Operation || seconds <= 0d)
            {
                return;
            }

            PhaseEndsAt = Math.Max(now, PhaseEndsAt - seconds);
        }

        public void DelayCurrentPhase(double seconds)
        {
            if (seconds > 0d)
            {
                PhaseEndsAt += seconds;
            }
        }

        public void ResetToLobby()
        {
            Phase = MatchPhase.Lobby;
            RoundIndex = 0;
            BuilderTeam = TeamId.None;
            PhaseEndsAt = 0d;
        }

        private void Advance(double now, GameBalanceConfig balance)
        {
            switch (Phase)
            {
                case MatchPhase.RoleReveal:
                    Enter(MatchPhase.Setup, now, balance);
                    break;
                case MatchPhase.Setup:
                    Enter(MatchPhase.Preparation, now, balance);
                    break;
                case MatchPhase.Preparation:
                    Enter(MatchPhase.Operation, now, balance);
                    break;
                case MatchPhase.Operation:
                    Enter(MatchPhase.Reveal, now, balance);
                    break;
                case MatchPhase.Reveal when RoundIndex == 0:
                    RoundIndex = 1;
                    BuilderTeam = RoleRules.Opponent(BuilderTeam);
                    Enter(MatchPhase.RoleReveal, now, balance);
                    break;
                case MatchPhase.Reveal:
                    Enter(MatchPhase.Results, now, balance);
                    break;
            }
        }

        private void Enter(MatchPhase phase, double now, GameBalanceConfig balance)
        {
            Phase = phase;
            PhaseEndsAt = now + balance.DurationFor(phase);
        }
    }
}
