using System;

namespace BriefcaseProtocol.Core
{
    [Serializable]
    public struct RoundStatistics
    {
        public TeamId BuilderTeam;
        public TeamId Winner;
        public RoundEndReason EndReason;
        public float OperationDuration;
        public float SuccessfulSolveTime;
        public float FakeEngagementTime;
        public int WrongCodes;
        public int ModuleErrors;
        public int TrapsTriggered;
        public int Strikes;
    }

    public static class MatchResultCalculator
    {
        public static TeamId CalculateWinner(RoundStatistics first, RoundStatistics second)
        {
            if (first.Winner == second.Winner)
            {
                return first.Winner;
            }

            var redWins = (first.Winner == TeamId.Red ? 1 : 0) + (second.Winner == TeamId.Red ? 1 : 0);
            var blueWins = (first.Winner == TeamId.Blue ? 1 : 0) + (second.Winner == TeamId.Blue ? 1 : 0);
            if (redWins != blueWins)
            {
                return redWins > blueWins ? TeamId.Red : TeamId.Blue;
            }

            var redSolve = SolveTimeFor(TeamId.Red, first, second);
            var blueSolve = SolveTimeFor(TeamId.Blue, first, second);
            if (float.IsPositiveInfinity(redSolve) && float.IsPositiveInfinity(blueSolve))
            {
                return TeamId.None;
            }

            if (Math.Abs(redSolve - blueSolve) < 0.01f)
            {
                return TeamId.None;
            }

            return redSolve < blueSolve ? TeamId.Red : TeamId.Blue;
        }

        private static float SolveTimeFor(TeamId team, RoundStatistics first, RoundStatistics second)
        {
            if (first.Winner == team && first.EndReason == RoundEndReason.Defused)
            {
                return first.SuccessfulSolveTime;
            }

            if (second.Winner == team && second.EndReason == RoundEndReason.Defused)
            {
                return second.SuccessfulSolveTime;
            }

            return float.PositiveInfinity;
        }
    }
}
