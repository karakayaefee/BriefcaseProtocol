namespace BriefcaseProtocol.Core
{
    public enum MatchPhase : byte
    {
        Lobby,
        RoleReveal,
        Setup,
        Preparation,
        Operation,
        Reveal,
        Results
    }

    public enum TeamId : byte
    {
        None,
        Red,
        Blue
    }

    public enum RoleSlot : byte
    {
        None,
        Operator,
        Support
    }

    public enum RoundSide : byte
    {
        None,
        Builder,
        Solver
    }

    public enum GameplayRole : byte
    {
        None,
        BombMaker,
        Trapper,
        FieldAgent,
        Analyst
    }

    public enum BriefcaseKind : byte
    {
        Real,
        Fake
    }

    public enum BriefcaseStatus : byte
    {
        Hidden,
        Discovered,
        Locked,
        Open,
        ConfirmedReal,
        ConfirmedFake,
        Defused
    }

    public enum CombinationRuleKind : byte
    {
        ColorTag,
        SerialNumber
    }

    public enum ModuleKind : byte
    {
        WireLogic,
        SequenceButton
    }

    public enum TrapKind : byte
    {
        SoundLure,
        ControlledDoor
    }

    public enum ShopItemKind : byte
    {
        WireModule,
        SequenceModule,
        FakeBriefcase,
        FakeWirePanel,
        FakeKeypad,
        FakeLed,
        FakeTimer,
        WeakSignal,
        SoundLure,
        ControlledDoor
    }

    public enum MatchEventType : byte
    {
        PhaseChanged,
        BriefcaseDiscovered,
        WrongCode,
        BriefcaseOpened,
        FakeConfirmed,
        RealConfirmed,
        ModuleCompleted,
        TrapTriggered,
        LastSixtySeconds,
        RoundEnded,
        PlayerDisconnected
    }

    public enum RoundEndReason : byte
    {
        None,
        Defused,
        TimeExpired,
        StrikeLimit,
        PlayerDisconnected,
        Aborted
    }

    public enum WebcamCorner : byte
    {
        None,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    public static class RoleRules
    {
        public static GameplayRole Resolve(RoleSlot slot, RoundSide side)
        {
            if (side == RoundSide.Builder)
            {
                return slot == RoleSlot.Operator ? GameplayRole.BombMaker :
                    slot == RoleSlot.Support ? GameplayRole.Trapper : GameplayRole.None;
            }

            if (side == RoundSide.Solver)
            {
                return slot == RoleSlot.Operator ? GameplayRole.FieldAgent :
                    slot == RoleSlot.Support ? GameplayRole.Analyst : GameplayRole.None;
            }

            return GameplayRole.None;
        }

        public static TeamId Opponent(TeamId team)
        {
            return team == TeamId.Red ? TeamId.Blue : team == TeamId.Blue ? TeamId.Red : TeamId.None;
        }
    }
}
