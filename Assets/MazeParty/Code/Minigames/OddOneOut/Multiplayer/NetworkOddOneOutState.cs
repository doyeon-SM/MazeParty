using MazeParty.Gameplay.Minigames;
using System;
using MazeParty.Gameplay.Minigames.OddOneOut;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    // One variable makes phase, all revealed poses and scores arrive atomically.
    // Pending choices are never included in this public snapshot.
    public struct OddOneOutPublicSnapshot : INetworkSerializable, IEquatable<OddOneOutPublicSnapshot>
    {
        public OddOneOutPhase Phase;
        public byte Round, RevealedMask;
        public int Winner, Score0, Score1, Score2, Score3;
        public uint Epoch;
        public bool Paused;
        public double EndsAt, PausedRemaining;
        public int GetScore(int slot) => slot == 0 ? Score0 : slot == 1 ? Score1 : slot == 2 ? Score2 : slot == 3 ? Score3 : 0;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Phase); s.SerializeValue(ref Round); s.SerializeValue(ref RevealedMask);
            s.SerializeValue(ref Winner); s.SerializeValue(ref Score0); s.SerializeValue(ref Score1);
            s.SerializeValue(ref Score2); s.SerializeValue(ref Score3); s.SerializeValue(ref Epoch);
            s.SerializeValue(ref Paused); s.SerializeValue(ref EndsAt); s.SerializeValue(ref PausedRemaining);
        }
        public bool Equals(OddOneOutPublicSnapshot o) => Phase == o.Phase && Round == o.Round &&
            RevealedMask == o.RevealedMask && Winner == o.Winner && Score0 == o.Score0 && Score1 == o.Score1 &&
            Score2 == o.Score2 && Score3 == o.Score3 && Epoch == o.Epoch && Paused == o.Paused &&
            EndsAt == o.EndsAt && PausedRemaining == o.PausedRemaining;
    }

    public struct OddOneOutPrivateChoice : INetworkSerializable, IEquatable<OddOneOutPrivateChoice>
    {
        public byte Round;
        public uint Epoch, Sequence;
        public bool Sitting;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Round); s.SerializeValue(ref Epoch);
            s.SerializeValue(ref Sequence); s.SerializeValue(ref Sitting);
        }
        public bool Equals(OddOneOutPrivateChoice o) => Round == o.Round && Epoch == o.Epoch && Sequence == o.Sequence && Sitting == o.Sitting;
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkOddOneOutState : NetworkBehaviour
    {
        public MinigameRulesConfig GameRules => MinigameRulesConfig.FromMatch(
            ScheduledMinigameId.OddOneOut, NetworkMatchState.Instance?.GameSettings);
        private readonly NetworkVariable<OddOneOutPublicSnapshot> _snapshot = new NetworkVariable<OddOneOutPublicSnapshot>();
        private readonly uint[] _sequences = new uint[4];
        private OddOneOutMatchState _simulation;
        private double _startedAt;
        private bool _completionReported;
        public static NetworkOddOneOutState Instance { get; private set; }
        public OddOneOutPhase Phase => _snapshot.Value.Phase;
        public int RoundNumber => _snapshot.Value.Round;
        public uint InputEpoch => _snapshot.Value.Epoch;
        public bool IsPaused => _snapshot.Value.Paused;
        public int WinnerSlot => _snapshot.Value.Winner;
        public double Remaining => Phase == OddOneOutPhase.Inactive || Phase == OddOneOutPhase.Complete ? 0d :
            Math.Max(0d, IsPaused ? _snapshot.Value.PausedRemaining : _snapshot.Value.EndsAt - ServerNow);
        private double ServerNow => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
        public override void OnNetworkSpawn() { Instance = this; }
        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; _simulation = null; }
        public int GetScore(int slot) => _snapshot.Value.GetScore(slot);
        public int GetFinalRank(int slot)
        {
            if (Phase != OddOneOutPhase.Complete || !OddOneOutMatchState.IsValidSlot(slot)) return 0;
            var rank = 1;
            for (var i = 0; i < 4; i++) if (GetScore(i) > GetScore(slot)) rank++;
            return rank;
        }
        public bool GetVisibleChoice(int slot, int viewerSlot)
        {
            if (!OddOneOutMatchState.IsValidSlot(slot)) return false;
            if (Phase == OddOneOutPhase.Reveal || Phase == OddOneOutPhase.Complete)
                return (_snapshot.Value.RevealedMask & (1 << slot)) != 0;
            var avatar = slot == viewerSlot ? NetworkMatchState.Instance?.GetAvatarForSlot(slot) : null;
            return Phase == OddOneOutPhase.Choosing && avatar != null && avatar.IsOwner && avatar.GetLocalOddOneOutChoice((byte)RoundNumber, InputEpoch);
        }
        public bool CanAcceptInputForSlot(int slot) => IsSpawned && OddOneOutMatchState.IsValidSlot(slot) &&
            !IsPaused && Phase == OddOneOutPhase.Choosing && Remaining > 0d;
        public void BeginMatchOnServer(ulong seed)
        {
            if (!IsSpawned || !IsServer) return;
            _simulation = new OddOneOutMatchState(rules: MinigameRulesConfig.FromMatch(ScheduledMinigameId.OddOneOut, NetworkMatchState.Instance?.GameSettings)); _completionReported = false; _startedAt = ServerNow;
            Publish(false, true); FreezeAvatars();
        }
        private void Update()
        {
            if (!IsSpawned || !IsServer || _simulation == null || IsPaused || Phase == OddOneOutPhase.Inactive) return;
            Advance(ServerNow); FreezeAvatars();
            if (Phase == OddOneOutPhase.Complete && !_completionReported && NetworkMatchState.Instance != null)
            {
                var ranks = new int[4];
                for (var i = 0; i < 4; i++) ranks[i] = _simulation.GetRank(i);
                _completionReported = NetworkMatchState.Instance.TryCompleteOddOneOutOnServer(ranks);
            }
        }
        private void Advance(double now)
        {
            var phase = _simulation.Phase; var round = _simulation.RoundNumber;
            _simulation.AdvanceTo(now - _startedAt);
            if (phase != _simulation.Phase || round != _simulation.RoundNumber) Publish(false, true);
        }
        private bool ValidateEnvelope(int round, uint epoch, uint sequence, int slot) =>
            IsSpawned && IsServer && _simulation != null && CanAcceptInputForSlot(slot) &&
            round == RoundNumber && MinigameServerRules.IsCurrentInputEpoch(epoch, InputEpoch) && sequence > _sequences[slot];
        public bool TrySetChoiceOnServer(NetworkPlayerAvatar avatar, bool sitting, byte round, uint epoch, uint sequence)
        {
            if (!TryResolveSlot(avatar, out var slot) || IsPaused || _simulation == null) return false;
            Advance(ServerNow);
            if (!ValidateEnvelope(round, epoch, sequence, slot)) return false;
            if (!_simulation.TrySetChoice(slot, sitting)) return false;
            _sequences[slot] = sequence;
            SyncPrivateChoice(avatar, slot);
            return true;
        }
        private bool TryResolveSlot(NetworkPlayerAvatar avatar, out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            var match = NetworkMatchState.Instance;
            return IsSpawned && IsServer && avatar != null && avatar.IsSpawned && OddOneOutMatchState.IsValidSlot(slot) &&
                match != null && match.IsOddOneOutPlaying && ReferenceEquals(match.GetAvatarForSlot(slot), avatar);
        }
        public void PauseOnServer(double now)
        {
            if (!IsSpawned || !IsServer || _simulation == null || IsPaused || double.IsNaN(now) || double.IsInfinity(now)) return;
            Advance(now); Publish(true, true); FreezeAvatars();
        }
        public void ResumeOnServer(double now)
        {
            if (!IsSpawned || !IsServer || _simulation == null || !IsPaused || double.IsNaN(now) || double.IsInfinity(now)) return;
            _startedAt = now - _simulation.Elapsed;
            Publish(false, true); FreezeAvatars();
        }
        public void RestoreAvatarForReconnectOnServer(NetworkPlayerAvatar avatar)
        {
            if (!IsServer || _simulation == null || avatar == null || !OddOneOutMatchState.IsValidSlot(avatar.AssignedSlot)) return;
            if (!ReferenceEquals(NetworkMatchState.Instance?.GetAvatarForSlot(avatar.AssignedSlot), avatar)) return;
            SyncPrivateChoice(avatar, avatar.AssignedSlot); avatar.StopServerInputOnServer();
        }
        public void EndMatchOnServer()
        {
            if (!IsServer) return;
            FreezeAvatars(); _simulation = null;
            _snapshot.Value = new OddOneOutPublicSnapshot { Epoch = NextEpoch(), Winner = -1 };
        }
        private uint NextEpoch() => MinigameServerRules.NextInputEpoch(InputEpoch);
        private void Publish(bool paused, bool newEpoch)
        {
            var epoch = newEpoch ? NextEpoch() : InputEpoch;
            if (newEpoch) Array.Clear(_sequences, 0, _sequences.Length);
            _snapshot.Value = new OddOneOutPublicSnapshot {
                Phase = _simulation.Phase, Round = (byte)_simulation.RoundNumber, Epoch = epoch,
                RevealedMask = _simulation.RevealedMask, Winner = _simulation.WinnerSlot,
                Score0 = _simulation.GetScore(0), Score1 = _simulation.GetScore(1), Score2 = _simulation.GetScore(2), Score3 = _simulation.GetScore(3),
                Paused = paused, EndsAt = _startedAt + _simulation.PhaseEndsAt, PausedRemaining = _simulation.Remaining };
            for (var i = 0; i < 4; i++) SyncPrivateChoice(NetworkMatchState.Instance?.GetAvatarForSlot(i), i);
        }
        private void SyncPrivateChoice(NetworkPlayerAvatar avatar, int slot)
        {
            if (avatar != null && avatar.IsSpawned) avatar.SetOddOneOutChoiceOnServer(new OddOneOutPrivateChoice {
                Round = (byte)RoundNumber, Epoch = InputEpoch, Sequence = _sequences[slot], Sitting = _simulation.GetChoice(slot) });
        }
        private void FreezeAvatars()
        {
            for (var i = 0; i < 4; i++) NetworkMatchState.Instance?.GetAvatarForSlot(i)?.StopServerInputOnServer();
        }
    }
}
