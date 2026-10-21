using MazeParty.Gameplay.Minigames;
using System;
using MazeParty.Gameplay.Minigames.Flight;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public struct FlightPlayerSnapshot : INetworkSerializable, IEquatable<FlightPlayerSnapshot>
    {
        public double Height, Velocity, Survival;
        public int Flaps;
        public uint Sequence;
        public byte Rank;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Height); s.SerializeValue(ref Velocity); s.SerializeValue(ref Survival);
            s.SerializeValue(ref Flaps); s.SerializeValue(ref Sequence); s.SerializeValue(ref Rank);
        }
        public bool Equals(FlightPlayerSnapshot o) => Height == o.Height && Velocity == o.Velocity &&
            Survival == o.Survival && Flaps == o.Flaps && Sequence == o.Sequence && Rank == o.Rank;
    }
    public struct FlightSnapshot : INetworkSerializable, IEquatable<FlightSnapshot>
    {
        public FlightPhase Phase;
        public byte AliveMask;
        public ulong Seed;
        public uint Epoch;
        public bool Paused;
        public double EndsAt, PausedRemaining, RunElapsed, SampledAt;
        public FlightPlayerSnapshot Player0, Player1, Player2, Player3;
        public FlightPlayerSnapshot Player(int slot) => slot == 0 ? Player0 : slot == 1 ? Player1 : slot == 2 ? Player2 : Player3;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Phase); s.SerializeValue(ref AliveMask); s.SerializeValue(ref Seed);
            s.SerializeValue(ref Epoch); s.SerializeValue(ref Paused); s.SerializeValue(ref EndsAt);
            s.SerializeValue(ref PausedRemaining); s.SerializeValue(ref RunElapsed); s.SerializeValue(ref SampledAt);
            s.SerializeValue(ref Player0); s.SerializeValue(ref Player1); s.SerializeValue(ref Player2); s.SerializeValue(ref Player3);
        }
        public bool Equals(FlightSnapshot o) => Phase == o.Phase && AliveMask == o.AliveMask && Seed == o.Seed &&
            Epoch == o.Epoch && Paused == o.Paused && EndsAt == o.EndsAt && PausedRemaining == o.PausedRemaining &&
            RunElapsed == o.RunElapsed && SampledAt == o.SampledAt && Player0.Equals(o.Player0) &&
            Player1.Equals(o.Player1) && Player2.Equals(o.Player2) && Player3.Equals(o.Player3);
    }
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkFlightState : NetworkBehaviour
    {
        public MinigameRulesConfig GameRules => MinigameRulesConfig.FromMatch(
            ScheduledMinigameId.Flight, NetworkMatchState.Instance?.GameSettings);
        private readonly NetworkVariable<FlightSnapshot> _snapshot = new NetworkVariable<FlightSnapshot>();
        private readonly uint[] _sequences = new uint[4];
        private FlightMatchState _simulation;
        private double _startedAt, _nextPublish;
        private bool _completionReported;
        public static NetworkFlightState Instance { get; private set; }
        public FlightPhase Phase => _snapshot.Value.Phase;
        public ulong Seed => _snapshot.Value.Seed;
        public uint InputEpoch => _snapshot.Value.Epoch;
        public bool IsPaused => _snapshot.Value.Paused;
        public double Remaining => Phase == FlightPhase.Inactive || Phase == FlightPhase.Complete ? 0 :
            Math.Max(0, IsPaused ? _snapshot.Value.PausedRemaining : _snapshot.Value.EndsAt - ServerNow);
        public double RunElapsed => Math.Min(GameRules.Seconds, _snapshot.Value.RunElapsed +
            (Phase == FlightPhase.Running && !IsPaused ? Math.Max(0, ServerNow - _snapshot.Value.SampledAt) : 0));
        private double ServerNow => NetworkManager != null && NetworkManager.IsListening ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;
        public override void OnNetworkSpawn() { Instance = this; }
        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; _simulation = null; }
        public bool IsAlive(int slot) => FlightRules.ValidSlot(slot) && (_snapshot.Value.AliveMask & (1 << slot)) != 0;
        public FlightPlayerSnapshot GetPlayer(int slot) => FlightRules.ValidSlot(slot) ? _snapshot.Value.Player(slot) : default;
        public int GetFinalRank(int slot) => Phase == FlightPhase.Complete && FlightRules.ValidSlot(slot) ? GetPlayer(slot).Rank : 0;
        public bool CanAcceptInputForSlot(int slot) => IsSpawned && IsAlive(slot) && !IsPaused && Phase == FlightPhase.Running && Remaining > 0;
        public void GetPresentation(int slot, out double y, out double velocity)
        {
            var player = GetPlayer(slot); y = player.Height; velocity = player.Velocity;
            if (Phase == FlightPhase.Running && !IsPaused && IsAlive(slot))
                FlightRules.Integrate(ref y, ref velocity, Math.Min(.15, Math.Max(0, ServerNow - _snapshot.Value.SampledAt)));
        }
        public void BeginMatchOnServer(ulong seed)
        {
            if (!IsSpawned || !IsServer) return;
            _simulation = new FlightMatchState(seed, rules: MinigameRulesConfig.FromMatch(ScheduledMinigameId.Flight, NetworkMatchState.Instance?.GameSettings)); _startedAt = ServerNow; _completionReported = false;
            Publish(false, true); FreezeAvatars();
        }
        private void Update()
        {
            if (!IsSpawned || !IsServer || _simulation == null || IsPaused || Phase == FlightPhase.Inactive) return;
            Advance(ServerNow); FreezeAvatars();
            if (Phase == FlightPhase.Complete && !_completionReported && NetworkMatchState.Instance != null)
            {
                var ranks = new int[4]; for (var i = 0; i < 4; i++) ranks[i] = _simulation.Rank(i);
                _completionReported = NetworkMatchState.Instance.TryCompleteFlightOnServer(ranks);
            }
        }
        private void Advance(double now)
        {
            var phase = _simulation.Phase; var alive = AliveMask();
            _simulation.AdvanceTo(now - _startedAt);
            if (phase != _simulation.Phase || alive != AliveMask() || now >= _nextPublish)
                Publish(false, phase != _simulation.Phase);
        }
        private bool ValidateEnvelope(uint epoch, uint sequence, int slot) => IsSpawned && IsServer &&
            _simulation != null && CanAcceptInputForSlot(slot) && MinigameServerRules.IsCurrentInputEpoch(epoch, InputEpoch) && sequence > _sequences[slot];
        public bool TryFlapOnServer(NetworkPlayerAvatar avatar, uint epoch, uint sequence)
        {
            var slot = avatar != null ? avatar.AssignedSlot : -1; var match = NetworkMatchState.Instance;
            if (!IsSpawned || !IsServer || avatar == null || !avatar.IsSpawned || !FlightRules.ValidSlot(slot) ||
                match == null || !match.IsFlightPlaying || !ReferenceEquals(match.GetAvatarForSlot(slot), avatar) ||
                IsPaused || _simulation == null) return false;
            Advance(ServerNow);
            if (!ValidateEnvelope(epoch, sequence, slot)) return false;
            _sequences[slot] = sequence;
            var accepted = _simulation.TryFlap(slot);
            Publish(false, false);
            return accepted;
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
            FreezeAvatars(); _simulation = null; _snapshot.Value = new FlightSnapshot { Epoch = NextEpoch() };
        }
        private uint NextEpoch() => MinigameServerRules.NextInputEpoch(InputEpoch);
        private byte AliveMask()
        {
            byte mask = 0; for (var i = 0; i < 4; i++) if (_simulation.IsAlive(i)) mask |= (byte)(1 << i); return mask;
        }
        private FlightPlayerSnapshot Sample(int slot) => new FlightPlayerSnapshot {
            Height = _simulation.Height(slot), Velocity = _simulation.Velocity(slot), Survival = _simulation.Survival(slot),
            Flaps = _simulation.Flaps(slot), Sequence = _sequences[slot], Rank = (byte)_simulation.Rank(slot)
        };
        private void Publish(bool paused, bool newEpoch)
        {
            var epoch = newEpoch ? NextEpoch() : InputEpoch;
            if (newEpoch) Array.Clear(_sequences, 0, _sequences.Length);
            _snapshot.Value = new FlightSnapshot {
                Phase = _simulation.Phase, Seed = _simulation.Seed, Epoch = epoch, AliveMask = AliveMask(), Paused = paused,
                EndsAt = _startedAt + _simulation.PhaseEndsAt, PausedRemaining = _simulation.Remaining,
                RunElapsed = _simulation.RunElapsed, SampledAt = _startedAt + _simulation.Elapsed,
                Player0 = Sample(0), Player1 = Sample(1), Player2 = Sample(2), Player3 = Sample(3)
            };
            _nextPublish = ServerNow + .05;
        }
        private void FreezeAvatars() { for (var i = 0; i < 4; i++) NetworkMatchState.Instance?.GetAvatarForSlot(i)?.StopServerInputOnServer(); }
    }
}
