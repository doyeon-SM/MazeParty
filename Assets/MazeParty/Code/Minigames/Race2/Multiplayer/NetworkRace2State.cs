using System;
using MazeParty.Gameplay.Minigames.Race2;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public struct Race2Snapshot : INetworkSerializable, IEquatable<Race2Snapshot>
    {
        public Race2Phase Phase;
        public byte Round, AliveMask, LaneMask;
        public ulong Seed;
        public uint Epoch, Scores, Sequence0, Sequence1, Sequence2, Sequence3;
        public bool Paused;
        public double EndsAt, PausedRemaining, RoundElapsed, SampledAt;
        public int Score(int slot) => (int)((Scores >> (slot * 8)) & 255);
        public uint Sequence(int slot) => slot == 0 ? Sequence0 : slot == 1 ? Sequence1 : slot == 2 ? Sequence2 : Sequence3;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Phase); s.SerializeValue(ref Round); s.SerializeValue(ref AliveMask); s.SerializeValue(ref LaneMask);
            s.SerializeValue(ref Seed); s.SerializeValue(ref Epoch); s.SerializeValue(ref Scores); s.SerializeValue(ref Paused);
            s.SerializeValue(ref EndsAt); s.SerializeValue(ref PausedRemaining); s.SerializeValue(ref RoundElapsed); s.SerializeValue(ref SampledAt);
            s.SerializeValue(ref Sequence0); s.SerializeValue(ref Sequence1); s.SerializeValue(ref Sequence2); s.SerializeValue(ref Sequence3);
        }
        public bool Equals(Race2Snapshot o) => Phase == o.Phase && Round == o.Round && AliveMask == o.AliveMask && LaneMask == o.LaneMask &&
            Seed == o.Seed && Epoch == o.Epoch && Scores == o.Scores && Paused == o.Paused && EndsAt == o.EndsAt &&
            PausedRemaining == o.PausedRemaining && RoundElapsed == o.RoundElapsed && SampledAt == o.SampledAt &&
            Sequence0 == o.Sequence0 && Sequence1 == o.Sequence1 && Sequence2 == o.Sequence2 && Sequence3 == o.Sequence3;
    }
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkRace2State : NetworkBehaviour
    {
        private readonly NetworkVariable<Race2Snapshot> _snapshot = new NetworkVariable<Race2Snapshot>();
        private readonly uint[] _sequences = new uint[4];
        private Race2MatchState _simulation;
        private double _startedAt;
        private bool _completionReported;
        public static NetworkRace2State Instance { get; private set; }
        public Race2Phase Phase => _snapshot.Value.Phase;
        public int RoundNumber => _snapshot.Value.Round;
        public ulong Seed => _snapshot.Value.Seed;
        public uint InputEpoch => _snapshot.Value.Epoch;
        public bool IsPaused => _snapshot.Value.Paused;
        public double Remaining => Phase == Race2Phase.Inactive || Phase == Race2Phase.Complete ? 0d :
            Math.Max(0d, IsPaused ? _snapshot.Value.PausedRemaining : _snapshot.Value.EndsAt - ServerNow);
        public double RoundElapsed => Math.Min(Race2Rules.RoundSeconds, _snapshot.Value.RoundElapsed +
            (Phase == Race2Phase.Running && !IsPaused ? Math.Max(0d, ServerNow - _snapshot.Value.SampledAt) : 0d));
        private double ServerNow => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
        public override void OnNetworkSpawn() { Instance = this; }
        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; _simulation = null; }
        public bool IsAlive(int slot) => Race2Rules.ValidSlot(slot) && (_snapshot.Value.AliveMask & (1 << slot)) != 0;
        public byte GetLane(int slot) => Race2Rules.ValidSlot(slot) ? (byte)((_snapshot.Value.LaneMask >> slot) & 1) : (byte)0;
        public uint GetSequence(int slot) => Race2Rules.ValidSlot(slot) ? _snapshot.Value.Sequence(slot) : 0;
        public int GetScore(int slot) => Race2Rules.ValidSlot(slot) ? _snapshot.Value.Score(slot) : 0;
        public int GetFinalRank(int slot)
        {
            if (Phase != Race2Phase.Complete || !Race2Rules.ValidSlot(slot)) return 0;
            var rank = 1; for (var i = 0; i < 4; i++) if (GetScore(i) > GetScore(slot)) rank++;
            return rank;
        }
        public bool CanAcceptInputForSlot(int slot) => IsSpawned && IsAlive(slot) && !IsPaused && Phase == Race2Phase.Running && Remaining > 0d;
        public void BeginMatchOnServer(ulong seed)
        {
            if (!IsSpawned || !IsServer) return;
            _simulation = new Race2MatchState(seed); _completionReported = false; _startedAt = ServerNow;
            Publish(false, true); FreezeAvatars();
        }
        private void Update()
        {
            if (!IsSpawned || !IsServer || _simulation == null || IsPaused || Phase == Race2Phase.Inactive) return;
            Advance(ServerNow); FreezeAvatars();
            if (Phase == Race2Phase.Complete && !_completionReported && NetworkMatchState.Instance != null)
            {
                var ranks = new int[4]; for (var i = 0; i < 4; i++) ranks[i] = _simulation.GetRank(i);
                _completionReported = NetworkMatchState.Instance.TryCompleteRace2OnServer(ranks);
            }
        }
        private void Advance(double now)
        {
            var phase = _simulation.Phase; var round = _simulation.RoundNumber;
            var before = AliveMask(); _simulation.AdvanceTo(now - _startedAt);
            var transition = phase != _simulation.Phase || round != _simulation.RoundNumber;
            if (transition || before != AliveMask()) Publish(false, transition);
        }
        private bool ValidateEnvelope(int round, uint epoch, uint sequence, int slot) => IsSpawned && IsServer &&
            _simulation != null && CanAcceptInputForSlot(slot) && round == RoundNumber && epoch != 0 && epoch == InputEpoch && sequence > _sequences[slot];
        public bool TrySetLaneOnServer(NetworkPlayerAvatar avatar, byte lane, byte round, uint epoch, uint sequence)
        {
            var slot = avatar != null ? avatar.AssignedSlot : -1; var match = NetworkMatchState.Instance;
            if (!IsSpawned || !IsServer || avatar == null || !avatar.IsSpawned || !Race2Rules.ValidSlot(slot) ||
                match == null || !match.IsRace2Playing || !ReferenceEquals(match.GetAvatarForSlot(slot), avatar) ||
                lane > 1 || IsPaused || _simulation == null) return false;
            Advance(ServerNow);
            if (!ValidateEnvelope(round, epoch, sequence, slot)) return false;
            _sequences[slot] = sequence; var phase = _simulation.Phase;
            var changed = _simulation.TrySetLane(slot, lane);
            Publish(false, phase != _simulation.Phase);
            return changed;
        }
        public void PauseOnServer(double now)
        {
            if (!IsSpawned || !IsServer || _simulation == null || IsPaused || double.IsNaN(now) || double.IsInfinity(now)) return;
            Advance(now); Publish(true, true); FreezeAvatars();
        }
        public void ResumeOnServer(double now)
        {
            if (!IsSpawned || !IsServer || _simulation == null || !IsPaused || double.IsNaN(now) || double.IsInfinity(now)) return;
            _startedAt = now - _simulation.Elapsed; Publish(false, true); FreezeAvatars();
        }
        public void RestoreAvatarForReconnectOnServer(NetworkPlayerAvatar avatar)
        {
            if (IsServer && _simulation != null && avatar != null &&
                ReferenceEquals(NetworkMatchState.Instance?.GetAvatarForSlot(avatar.AssignedSlot), avatar)) avatar.StopServerInputOnServer();
        }
        public void EndMatchOnServer()
        {
            if (!IsServer) return;
            FreezeAvatars(); _simulation = null; _snapshot.Value = new Race2Snapshot { Epoch = NextEpoch() };
        }
        private uint NextEpoch() => InputEpoch == uint.MaxValue ? 1U : InputEpoch + 1U;
        private byte AliveMask()
        {
            byte mask = 0; for (var i = 0; i < 4; i++) if (_simulation.IsAlive(i)) mask |= (byte)(1 << i); return mask;
        }
        private void Publish(bool paused, bool newEpoch)
        {
            var epoch = newEpoch ? NextEpoch() : InputEpoch;
            if (newEpoch) Array.Clear(_sequences, 0, _sequences.Length);
            byte lanes = 0; uint scores = 0;
            for (var i = 0; i < 4; i++) { lanes |= (byte)(_simulation.GetLane(i) << i); scores |= (uint)_simulation.GetScore(i) << (i * 8); }
            _snapshot.Value = new Race2Snapshot { Phase = _simulation.Phase, Round = (byte)_simulation.RoundNumber, Seed = _simulation.Seed,
                Epoch = epoch, AliveMask = AliveMask(), LaneMask = lanes, Scores = scores, Paused = paused,
                EndsAt = _startedAt + _simulation.PhaseEndsAt, PausedRemaining = _simulation.Remaining,
                RoundElapsed = _simulation.RoundElapsed, SampledAt = _startedAt + _simulation.Elapsed,
                Sequence0 = _sequences[0], Sequence1 = _sequences[1], Sequence2 = _sequences[2], Sequence3 = _sequences[3] };
        }
        private void FreezeAvatars() { for (var i = 0; i < 4; i++) NetworkMatchState.Instance?.GetAvatarForSlot(i)?.StopServerInputOnServer(); }
    }
}
