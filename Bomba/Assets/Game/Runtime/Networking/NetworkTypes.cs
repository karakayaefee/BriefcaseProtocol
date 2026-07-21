using System;
using BriefcaseProtocol.Core;
using Unity.Collections;
using Unity.Netcode;

namespace BriefcaseProtocol.Networking
{
    public struct NetworkPlayerState : INetworkSerializable, IEquatable<NetworkPlayerState>
    {
        public ulong ClientId;
        public FixedString64Bytes DisplayName;
        public TeamId Team;
        public RoleSlot Slot;
        public bool Ready;
        public bool Connected;

        public GameplayRole RoleFor(TeamId builderTeam)
        {
            var side = Team == builderTeam ? RoundSide.Builder : RoundSide.Solver;
            return RoleRules.Resolve(Slot, side);
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref ClientId);
            serializer.SerializeValue(ref DisplayName);
            serializer.SerializeValue(ref Team);
            serializer.SerializeValue(ref Slot);
            serializer.SerializeValue(ref Ready);
            serializer.SerializeValue(ref Connected);
        }

        public bool Equals(NetworkPlayerState other)
        {
            return ClientId == other.ClientId && DisplayName.Equals(other.DisplayName) && Team == other.Team &&
                Slot == other.Slot && Ready == other.Ready && Connected == other.Connected;
        }
    }

    public struct NetworkMatchState : INetworkSerializable, IEquatable<NetworkMatchState>
    {
        public MatchPhase Phase;
        public int RoundIndex;
        public TeamId BuilderTeam;
        public double PhaseEndsAt;
        public int Strikes;
        public RoundEndReason LastEndReason;
        public TeamId LastRoundWinner;
        public TeamId MatchWinner;

        public TeamId SolverTeam => RoleRules.Opponent(BuilderTeam);

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Phase);
            serializer.SerializeValue(ref RoundIndex);
            serializer.SerializeValue(ref BuilderTeam);
            serializer.SerializeValue(ref PhaseEndsAt);
            serializer.SerializeValue(ref Strikes);
            serializer.SerializeValue(ref LastEndReason);
            serializer.SerializeValue(ref LastRoundWinner);
            serializer.SerializeValue(ref MatchWinner);
        }

        public bool Equals(NetworkMatchState other)
        {
            return Phase == other.Phase && RoundIndex == other.RoundIndex && BuilderTeam == other.BuilderTeam &&
                Math.Abs(PhaseEndsAt - other.PhaseEndsAt) < 0.001d && Strikes == other.Strikes &&
                LastEndReason == other.LastEndReason && LastRoundWinner == other.LastRoundWinner &&
                MatchWinner == other.MatchWinner;
        }
    }

    public struct MatchEventData : INetworkSerializable
    {
        public uint Sequence;
        public MatchEventType Type;
        public ulong ActorClientId;
        public FixedString64Bytes Subject;
        public int Value;
        public double ServerTime;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Sequence);
            serializer.SerializeValue(ref Type);
            serializer.SerializeValue(ref ActorClientId);
            serializer.SerializeValue(ref Subject);
            serializer.SerializeValue(ref Value);
            serializer.SerializeValue(ref ServerTime);
        }
    }
}
