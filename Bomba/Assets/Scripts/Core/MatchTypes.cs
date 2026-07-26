namespace BriefcaseProtocol.Core
{
    /// <summary>
    /// Maçın server tarafında yürüyen aşamaları. Sıra GDD bölüm 5'teki maç yapısını takip eder.
    /// </summary>
    public enum MatchPhase : byte
    {
        None = 0,
        Lobby = 1,
        RoleReveal = 2,
        Setup = 3,
        SolverPrep = 4,
        Operation = 5,
        Reveal = 6,
        RoundResults = 7,
        MatchResults = 8
    }

    /// <summary>Takım kimliği. Maç boyunca sabit kalır.</summary>
    public enum TeamId : byte
    {
        None = 0,
        TeamA = 1,
        TeamB = 2
    }

    /// <summary>
    /// Oyuncunun lobide seçtiği ve maç boyunca değişmeyen slot.
    /// Somut rol bu slot ile o round'daki taraftan türetilir.
    /// </summary>
    public enum RoleSlot : byte
    {
        None = 0,
        Operator = 1,
        Support = 2
    }

    /// <summary>Bir round'da takımın hangi tarafta olduğu. Her round yer değişir.</summary>
    public enum TeamSide : byte
    {
        None = 0,
        Builder = 1,
        Solver = 2
    }

    /// <summary>Slot + taraf birleşiminden çıkan somut rol (GDD bölüm 6).</summary>
    public enum RoundRole : byte
    {
        None = 0,
        BombMaker = 1,   // Builder + Operator
        Trapper = 2,     // Builder + Support
        FieldAgent = 3,  // Solver + Operator
        Analyst = 4      // Solver + Support
    }

    /// <summary>
    /// Maç sabitleri ve saf (state'siz) geçiş kuralları.
    /// Buradaki hiçbir şey network'e bağlı değildir; EditMode testlerinden doğrudan çağrılabilir.
    /// </summary>
    public static class MatchRules
    {
        public const int PlayersPerTeam = 2;
        public const int TotalPlayers = 4;
        public const int RoundsPerMatch = 2;

        // GDD bölüm 5, önerilen süreler (saniye).
        public const float RoleRevealSeconds = 10f;
        public const float SetupSeconds = 120f;
        public const float SolverPrepSeconds = 30f;
        public const float OperationSeconds = 300f;
        public const float RevealSeconds = 25f;
        public const float ResultsSeconds = 20f;

        /// <summary>Aşamanın server timer süresi. 0 dönerse aşama süresizdir (dışarıdan tetiklenir).</summary>
        public static float GetPhaseDuration(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.RoleReveal: return RoleRevealSeconds;
                case MatchPhase.Setup: return SetupSeconds;
                case MatchPhase.SolverPrep: return SolverPrepSeconds;
                case MatchPhase.Operation: return OperationSeconds;
                case MatchPhase.Reveal: return RevealSeconds;
                case MatchPhase.RoundResults: return ResultsSeconds;
                default: return 0f; // Lobby ve MatchResults süresiz.
            }
        }

        /// <summary>
        /// Round içindeki doğrusal aşama geçişi.
        /// RoundResults sonrası None döner: sonraki round mu maç sonu mu olduğuna RoundManager karar verir.
        /// </summary>
        public static MatchPhase GetNextPhase(MatchPhase phase)
        {
            switch (phase)
            {
                case MatchPhase.Lobby: return MatchPhase.RoleReveal;
                case MatchPhase.RoleReveal: return MatchPhase.Setup;
                case MatchPhase.Setup: return MatchPhase.SolverPrep;
                case MatchPhase.SolverPrep: return MatchPhase.Operation;
                case MatchPhase.Operation: return MatchPhase.Reveal;
                case MatchPhase.Reveal: return MatchPhase.RoundResults;
                default: return MatchPhase.None;
            }
        }

        /// <summary>Oyuncuların sahada serbest hareket ettiği aşamalar.</summary>
        public static bool IsPlayablePhase(MatchPhase phase)
        {
            return phase == MatchPhase.Setup
                || phase == MatchPhase.SolverPrep
                || phase == MatchPhase.Operation;
        }

        public static TeamId Opponent(TeamId team)
        {
            switch (team)
            {
                case TeamId.TeamA: return TeamId.TeamB;
                case TeamId.TeamB: return TeamId.TeamA;
                default: return TeamId.None;
            }
        }

        public static TeamSide OppositeSide(TeamSide side)
        {
            switch (side)
            {
                case TeamSide.Builder: return TeamSide.Solver;
                case TeamSide.Solver: return TeamSide.Builder;
                default: return TeamSide.None;
            }
        }

        /// <summary>Slot sabit kalır, taraf her round değişir; rol bu ikisinden türer.</summary>
        public static RoundRole ResolveRole(TeamSide side, RoleSlot slot)
        {
            if (side == TeamSide.Builder)
            {
                if (slot == RoleSlot.Operator) return RoundRole.BombMaker;
                if (slot == RoleSlot.Support) return RoundRole.Trapper;
            }
            else if (side == TeamSide.Solver)
            {
                if (slot == RoleSlot.Operator) return RoundRole.FieldAgent;
                if (slot == RoleSlot.Support) return RoundRole.Analyst;
            }

            return RoundRole.None;
        }

        /// <summary>Kurucu roller sahada standart etkileşim yapamaz (GDD bölüm 6.1).</summary>
        public static bool IsBuilderRole(RoundRole role)
        {
            return role == RoundRole.BombMaker || role == RoundRole.Trapper;
        }
    }
}
