using BriefcaseProtocol.Core;
using NUnit.Framework;
using UnityEngine;

namespace BriefcaseProtocol.Tests
{
    public sealed class CoreDomainTests
    {
        [Test]
        public void BudgetNeverGoesNegativeOrAboveInitialValue()
        {
            var wallet = new BudgetWallet(100);
            Assert.That(wallet.TrySpend(70), Is.True);
            Assert.That(wallet.TrySpend(31), Is.False);
            Assert.That(wallet.Remaining, Is.EqualTo(30));
            Assert.That(wallet.TryRefund(71), Is.False);
            Assert.That(wallet.TryRefund(20), Is.True);
            Assert.That(wallet.Remaining, Is.EqualTo(50));
        }

        [Test]
        public void ColorCombinationIsDeterministicAndResolvable()
        {
            var first = CombinationResolver.Generate(CombinationRuleKind.ColorTag, 12345);
            var second = CombinationResolver.Generate(CombinationRuleKind.ColorTag, 12345);
            Assert.That(first.Code, Is.EqualTo(second.Code));
            Assert.That(CombinationResolver.Resolve(first.Clue), Is.EqualTo(first.Code));
            Assert.That(first.CodeText, Has.Length.EqualTo(3));
        }

        [Test]
        public void SerialRuleUsesMiddleDigitsAndLetterCount()
        {
            var clue = new CombinationClue(CombinationRuleKind.SerialNumber, "A-47-K2", System.Array.Empty<string>());
            Assert.That(CombinationResolver.Resolve(clue), Is.EqualTo(472));
        }

        [Test]
        public void RoleMappingRemainsStableWhenTeamChangesSide()
        {
            Assert.That(RoleRules.Resolve(RoleSlot.Operator, RoundSide.Builder), Is.EqualTo(GameplayRole.BombMaker));
            Assert.That(RoleRules.Resolve(RoleSlot.Operator, RoundSide.Solver), Is.EqualTo(GameplayRole.FieldAgent));
            Assert.That(RoleRules.Resolve(RoleSlot.Support, RoundSide.Builder), Is.EqualTo(GameplayRole.Trapper));
            Assert.That(RoleRules.Resolve(RoleSlot.Support, RoundSide.Solver), Is.EqualTo(GameplayRole.Analyst));
        }

        [Test]
        public void StateMachineRunsTwoRoundsAndSwapsBuilderTeam()
        {
            var balance = ScriptableObject.CreateInstance<GameBalanceConfig>();
            var machine = new MatchStateMachine();
            machine.StartMatch(TeamId.Red, 0d, balance);
            Assert.That(machine.Phase, Is.EqualTo(MatchPhase.RoleReveal));
            Assert.That(machine.BuilderTeam, Is.EqualTo(TeamId.Red));

            var now = 0d;
            for (var i = 0; i < 5; i++)
            {
                now = machine.PhaseEndsAt;
                machine.Tick(now, balance);
            }

            Assert.That(machine.RoundIndex, Is.EqualTo(1));
            Assert.That(machine.BuilderTeam, Is.EqualTo(TeamId.Blue));
            Assert.That(machine.Phase, Is.EqualTo(MatchPhase.RoleReveal));

            for (var i = 0; i < 5; i++)
            {
                now = machine.PhaseEndsAt;
                machine.Tick(now, balance);
            }

            Assert.That(machine.Phase, Is.EqualTo(MatchPhase.Results));
            Object.DestroyImmediate(balance);
        }

        [Test]
        public void TieBreakUsesSuccessfulSolveTime()
        {
            var first = new RoundStatistics
            {
                Winner = TeamId.Red,
                EndReason = RoundEndReason.Defused,
                SuccessfulSolveTime = 150f
            };
            var second = new RoundStatistics
            {
                Winner = TeamId.Blue,
                EndReason = RoundEndReason.Defused,
                SuccessfulSolveTime = 170f
            };
            Assert.That(MatchResultCalculator.CalculateWinner(first, second), Is.EqualTo(TeamId.Red));
        }

        [Test]
        public void TwoFailedSolverRoundsProduceDraw()
        {
            var first = new RoundStatistics { Winner = TeamId.Red, EndReason = RoundEndReason.TimeExpired };
            var second = new RoundStatistics { Winner = TeamId.Blue, EndReason = RoundEndReason.TimeExpired };
            Assert.That(MatchResultCalculator.CalculateWinner(first, second), Is.EqualTo(TeamId.None));
        }
    }
}
