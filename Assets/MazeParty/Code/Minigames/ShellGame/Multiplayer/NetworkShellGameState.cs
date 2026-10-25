using MazeParty.Gameplay.Minigames;
using System;
using MazeParty.Gameplay.Minigames.ShellGame;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    // One variable makes phase, all revealed poses and scores arrive atomically.
    // Pending choices are never included in this public snapshot.
    public struct ShellGamePublicSnapshot : INetworkSerializable, IEquatable<ShellGamePublicSnapshot>
    {
        public ShellGamePhase Phase;
        public byte Round, RevealedChoices;
        public ulong Seed;
        public ShellGameItem TargetItem;
        public int Score0, Score1, Score2, Score3;
        public uint Epoch;
        public bool Paused;
        public double EndsAt, PausedRemaining;
        public int GetScore(int slot) => slot == 0 ? Score0 : slot == 1 ? Score1 : slot == 2 ? Score2 : slot == 3 ? Score3 : 0;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Phase); s.SerializeValue(ref Round); s.SerializeValue(ref RevealedChoices);
            s.SerializeValue(ref Seed); s.SerializeValue(ref TargetItem); s.SerializeValue(ref Score0); s.SerializeValue(ref Score1);
            s.SerializeValue(ref Score2); s.SerializeValue(ref Score3); s.SerializeValue(ref Epoch);
            s.SerializeValue(ref Paused); s.SerializeValue(ref EndsAt); s.SerializeValue(ref PausedRemaining);
        }
        public bool Equals(ShellGamePublicSnapshot o) => Phase == o.Phase && Round == o.Round &&
            RevealedChoices == o.RevealedChoices && Seed == o.Seed && TargetItem == o.TargetItem && Score0 == o.Score0 && Score1 == o.Score1 &&
            Score2 == o.Score2 && Score3 == o.Score3 && Epoch == o.Epoch && Paused == o.Paused &&
            EndsAt == o.EndsAt && PausedRemaining == o.PausedRemaining;
    }

    public struct ShellGamePrivateChoice : INetworkSerializable, IEquatable<ShellGamePrivateChoice>
    {
        public byte Round;
        public uint Epoch, Sequence;
        public int Box;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Round); s.SerializeValue(ref Epoch);
            s.SerializeValue(ref Sequence); s.SerializeValue(ref Box);
        }
        public bool Equals(ShellGamePrivateChoice o) => Round == o.Round && Epoch == o.Epoch && Sequence == o.Sequence && Box == o.Box;
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkShellGameState : NetworkBehaviour
    {
        public MinigameRulesConfig GameRules => MinigameRulesConfig.FromMatch(
            ScheduledMinigameId.ShellGame, NetworkMatchState.Instance?.GameSettings);
        private readonly NetworkVariable<ShellGamePublicSnapshot> _snapshot = new NetworkVariable<ShellGamePublicSnapshot>();
        private readonly uint[] _sequences = new uint[4];
        private ShellGameMatchState _simulation;
        private double _startedAt;
        private bool _completionReported;
        public static NetworkShellGameState Instance { get; private set; }
        public ShellGamePhase Phase => _snapshot.Value.Phase;
        public int RoundNumber => _snapshot.Value.Round;
        public uint InputEpoch => _snapshot.Value.Epoch;
        public bool IsPaused => _snapshot.Value.Paused;
        public ulong Seed => _snapshot.Value.Seed;
        public ShellGameItem TargetItem => Phase == ShellGamePhase.Choosing || Phase == ShellGamePhase.Reveal ||
            Phase == ShellGamePhase.Complete ? _snapshot.Value.TargetItem : ShellGameItem.None;
        public double Remaining => Phase == ShellGamePhase.Inactive || Phase == ShellGamePhase.Complete ? 0d :
            Math.Max(0d, IsPaused ? _snapshot.Value.PausedRemaining : _snapshot.Value.EndsAt - ServerNow);
        private double ServerNow => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
        public override void OnNetworkSpawn() { Instance = this; }
        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; _simulation = null; }
        public int GetScore(int slot) => _snapshot.Value.GetScore(slot);
        public int GetFinalRank(int slot)
        {
            if (Phase != ShellGamePhase.Complete || !ShellGameMatchState.IsValidSlot(slot)) return 0;
            var rank = 1;
            for (var i = 0; i < 4; i++) if (GetScore(i) > GetScore(slot)) rank++;
            return rank;
        }
        public int GetVisibleChoice(int slot, int viewerSlot)
        {
            if (!ShellGameMatchState.IsValidSlot(slot)) return -1;
            if (Phase == ShellGamePhase.Reveal || Phase == ShellGamePhase.Complete)
                return ((_snapshot.Value.RevealedChoices >> (slot * 2)) & 3) - 1;
            var avatar = slot == viewerSlot ? NetworkMatchState.Instance?.GetAvatarForSlot(slot) : null;
            return Phase == ShellGamePhase.Choosing && avatar != null && avatar.IsOwner
                ? avatar.GetLocalShellGameChoice((byte)RoundNumber, InputEpoch) : -1;
        }
        public bool CanAcceptInputForSlot(int slot) => IsSpawned && ShellGameMatchState.IsValidSlot(slot) &&
            !IsPaused && Phase == ShellGamePhase.Choosing && Remaining > 0d;
        public void BeginMatchOnServer(ulong seed)
        {
            if (!IsSpawned || !IsServer) return;
            _simulation = new ShellGameMatchState(seed, rules: MinigameRulesConfig.FromMatch(ScheduledMinigameId.ShellGame, NetworkMatchState.Instance?.GameSettings)); _completionReported = false; _startedAt = ServerNow;
            Publish(false, true); FreezeAvatars();
        }
        private void Update()
        {
            if (!IsSpawned || !IsServer || _simulation == null || IsPaused || Phase == ShellGamePhase.Inactive) return;
            Advance(ServerNow); FreezeAvatars();
            if (Phase == ShellGamePhase.Complete && !_completionReported && NetworkMatchState.Instance != null)
            {
                var ranks = new int[4];
                for (var i = 0; i < 4; i++) ranks[i] = _simulation.GetRank(i);
                _completionReported = NetworkMatchState.Instance.TryCompleteShellGameOnServer(ranks);
            }
        }
        private void Advance(double now)
        {
            var phase = _simulation.Phase; var round = _simulation.RoundNumber;
            var elapsed = now - _startedAt;
            // Subtraction after a pause can round just below the published absolute deadline.
            if (now >= _startedAt + _simulation.PhaseEndsAt)
                elapsed = Math.Max(elapsed, _simulation.PhaseEndsAt);
            _simulation.AdvanceTo(elapsed);
            if (phase != _simulation.Phase || round != _simulation.RoundNumber) Publish(false, true);
        }
        private bool ValidateEnvelope(int round, uint epoch, uint sequence, int slot) =>
            IsSpawned && IsServer && _simulation != null && CanAcceptInputForSlot(slot) &&
            round == RoundNumber && MinigameServerRules.IsCurrentInputEpoch(epoch, InputEpoch) && sequence > _sequences[slot];
        public bool TrySetChoiceOnServer(NetworkPlayerAvatar avatar, int box, byte round, uint epoch, uint sequence)
        {
            if (!TryResolveSlot(avatar, out var slot) || IsPaused || _simulation == null) return false;
            Advance(ServerNow);
            if (!ValidateEnvelope(round, epoch, sequence, slot)) return false;
            if (!_simulation.TryChoose(slot, box)) return false;
            _sequences[slot] = sequence;
            Publish(false, false);
            return true;
        }
        private bool TryResolveSlot(NetworkPlayerAvatar avatar, out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            var match = NetworkMatchState.Instance;
            return IsSpawned && IsServer && avatar != null && avatar.IsSpawned && ShellGameMatchState.IsValidSlot(slot) &&
                match != null && match.IsShellGamePlaying && ReferenceEquals(match.GetAvatarForSlot(slot), avatar);
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
            if (!IsServer || _simulation == null || avatar == null || !ShellGameMatchState.IsValidSlot(avatar.AssignedSlot)) return;
            if (!ReferenceEquals(NetworkMatchState.Instance?.GetAvatarForSlot(avatar.AssignedSlot), avatar)) return;
            SyncPrivateChoice(avatar, avatar.AssignedSlot); avatar.StopServerInputOnServer();
        }
        public void EndMatchOnServer()
        {
            if (!IsServer) return;
            FreezeAvatars(); _simulation = null;
            _snapshot.Value = new ShellGamePublicSnapshot { Epoch = NextEpoch() };
        }
        private uint NextEpoch() => MinigameServerRules.NextInputEpoch(InputEpoch);
        private void Publish(bool paused, bool newEpoch)
        {
            var epoch = newEpoch ? NextEpoch() : InputEpoch;
            if (newEpoch) Array.Clear(_sequences, 0, _sequences.Length);
            _snapshot.Value = new ShellGamePublicSnapshot {
                Phase = _simulation.Phase, Round = (byte)_simulation.RoundNumber, Epoch = epoch,
                RevealedChoices = _simulation.RevealedChoices, Seed = _simulation.Seed, TargetItem = _simulation.VisibleTargetItem,
                Score0 = _simulation.GetScore(0), Score1 = _simulation.GetScore(1), Score2 = _simulation.GetScore(2), Score3 = _simulation.GetScore(3),
                Paused = paused, EndsAt = _startedAt + _simulation.PhaseEndsAt, PausedRemaining = _simulation.Remaining };
            for (var i = 0; i < 4; i++) SyncPrivateChoice(NetworkMatchState.Instance?.GetAvatarForSlot(i), i);
        }
        private void SyncPrivateChoice(NetworkPlayerAvatar avatar, int slot)
        {
            if (avatar != null && avatar.IsSpawned) avatar.SetShellGameChoiceOnServer(new ShellGamePrivateChoice {
                Round = (byte)RoundNumber, Epoch = InputEpoch, Sequence = _sequences[slot], Box = _simulation.GetChoice(slot) });
        }
        private void FreezeAvatars()
        {
            for (var i = 0; i < 4; i++) NetworkMatchState.Instance?.GetAvatarForSlot(i)?.StopServerInputOnServer();
        }
    }
}
