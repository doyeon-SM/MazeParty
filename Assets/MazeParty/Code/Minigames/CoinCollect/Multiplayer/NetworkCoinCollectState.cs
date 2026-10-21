using MazeParty.Gameplay.Minigames;
using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.CoinCollect;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum NetworkCoinCollectPhase : byte { Inactive, Countdown, Running, RoundResult, Complete }

    public struct CoinCollectPlayerNetworkSnapshot : INetworkSerializable, IEquatable<CoinCollectPlayerNetworkSnapshot>
    {
        public Vector2 Position, Facing;
        public int Score;
        public float StunRemaining, CastRemaining, CooldownRemaining;
        public byte Rank;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Position); s.SerializeValue(ref Facing); s.SerializeValue(ref Score);
            s.SerializeValue(ref StunRemaining); s.SerializeValue(ref CastRemaining);
            s.SerializeValue(ref CooldownRemaining); s.SerializeValue(ref Rank);
        }
        public bool Equals(CoinCollectPlayerNetworkSnapshot other) => Position == other.Position && Facing == other.Facing &&
            Score == other.Score && StunRemaining == other.StunRemaining && CastRemaining == other.CastRemaining &&
            CooldownRemaining == other.CooldownRemaining && Rank == other.Rank;
        public CoinCollectPlayerSnapshot ToPresentation() => new CoinCollectPlayerSnapshot(Position, Facing, Score,
            StunRemaining, CastRemaining, CooldownRemaining);
    }
    public struct CoinCollectItemNetworkSnapshot : INetworkSerializable, IEquatable<CoinCollectItemNetworkSnapshot>
    {
        public Vector2 Position;
        public double BornAt, PickupUnlockAt;
        public float InitialHeight;
        public byte Kind;
        public bool Active;
        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref Position); s.SerializeValue(ref BornAt); s.SerializeValue(ref InitialHeight);
            s.SerializeValue(ref PickupUnlockAt);
            s.SerializeValue(ref Kind); s.SerializeValue(ref Active);
        }
        public bool Equals(CoinCollectItemNetworkSnapshot other) => Position == other.Position && BornAt == other.BornAt &&
            InitialHeight == other.InitialHeight && PickupUnlockAt == other.PickupUnlockAt &&
            Kind == other.Kind && Active == other.Active;
        public CoinCollectItemSnapshot ToPresentation() => new CoinCollectItemSnapshot((CoinCollectItemKind)Kind,
            Position, BornAt, InitialHeight, Active, PickupUnlockAt);
    }

    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkCoinCollectState : NetworkBehaviour
    {
        public MinigameRulesConfig GameRules => MinigameRulesConfig.FromMatch(
            ScheduledMinigameId.CoinCollect, NetworkMatchState.Instance?.GameSettings);
        public const double CountdownSeconds = 3d, RoundResultSeconds = 4d;
        private readonly NetworkVariable<byte> _phase = new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _roundNumber = new NetworkVariable<byte>();
        private readonly NetworkVariable<uint> _inputEpoch = new NetworkVariable<uint>();
        private readonly NetworkVariable<double> _phaseEndsAt = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedPhaseRemaining = new NetworkVariable<double>();
        private readonly NetworkVariable<double> _roundElapsed = new NetworkVariable<double>();
        private readonly NetworkVariable<bool> _paused = new NetworkVariable<bool>();
        private readonly NetworkList<CoinCollectPlayerNetworkSnapshot> _players = new NetworkList<CoinCollectPlayerNetworkSnapshot>();
        private readonly NetworkList<CoinCollectItemNetworkSnapshot> _items = new NetworkList<CoinCollectItemNetworkSnapshot>();
        private CoinCollectMatchState _simulation;
        private double _runningStartedAt;
        private bool _completionReported;
        public static NetworkCoinCollectState Instance { get; private set; }
        public event Action<int> PushPresentationRequested;
        public NetworkCoinCollectPhase Phase => (NetworkCoinCollectPhase)_phase.Value;
        public int RoundNumber => _roundNumber.Value;
        public uint InputEpoch => _inputEpoch.Value;
        public bool IsPaused => _paused.Value;
        public int ItemCount => _items.Count;
        public double Elapsed => Phase == NetworkCoinCollectPhase.Running && !_paused.Value
            ? Math.Min(GameRules.Seconds, Math.Max(0d, GameRules.Seconds - Remaining))
            : _roundElapsed.Value;
        public double Remaining => Phase == NetworkCoinCollectPhase.Inactive || Phase == NetworkCoinCollectPhase.Complete
            ? 0d : Math.Max(0d, _paused.Value ? _pausedPhaseRemaining.Value : _phaseEndsAt.Value - ServerNow);
        private double ServerNow => NetworkManager != null && NetworkManager.IsListening
            ? NetworkManager.ServerTime.Time : Time.unscaledTimeAsDouble;

        public override void OnNetworkSpawn() { Instance = this; }
        public override void OnNetworkDespawn() { if (Instance == this) Instance = null; _simulation = null; }
        public CoinCollectPlayerSnapshot GetPlayer(int slot) => slot >= 0 && slot < _players.Count
            ? _players[slot].ToPresentation() : new CoinCollectPlayerSnapshot(CoinCollectRules.StartPosition(slot), Vector2.up, 0, 0, 0, 0);
        public CoinCollectItemSnapshot GetItem(int index) => _items[index].ToPresentation();
        public int GetScore(int slot) => GetPlayer(slot).Score;
        public int GetFinalRank(int slot) => slot >= 0 && slot < _players.Count ? _players[slot].Rank : 0;
        public bool CanAcceptInputForSlot(int slot) => CoinCollectRules.IsValidSlot(slot) && IsSpawned &&
            !_paused.Value && Phase == NetworkCoinCollectPhase.Running && Remaining > 0d &&
            GetPlayer(slot).StunRemaining <= 0d && GetPlayer(slot).CastRemaining <= 0d;

        public void BeginMatchOnServer(ulong seed)
        {
            if (!IsSpawned || !IsServer) return;
            _simulation = new CoinCollectMatchState(seed, rules: MinigameRulesConfig.FromMatch(ScheduledMinigameId.CoinCollect, NetworkMatchState.Instance?.GameSettings));
            _simulation.ItemCollected += OnItemCollected;
            _simulation.PushStarted += OnPushStarted;
            _paused.Value = false; _completionReported = false;
            BeginCountdown(ServerNow);
        }
        private void Update()
        {
            if (!IsSpawned || !IsServer || _simulation == null || _paused.Value || Phase == NetworkCoinCollectPhase.Inactive) return;
            FreezeBoardAvatars();
            var now = ServerNow;
            if (Phase == NetworkCoinCollectPhase.Running) SimulateTo(now);
            if (Phase == NetworkCoinCollectPhase.Complete)
            {
                ReportCompletion();
                return;
            }
            if (now < _phaseEndsAt.Value) return;
            switch (Phase)
            {
                case NetworkCoinCollectPhase.Countdown:
                    _runningStartedAt = now;
                    _phase.Value = (byte)NetworkCoinCollectPhase.Running;
                    _phaseEndsAt.Value = now + GameRules.Seconds;
                    AdvanceEpoch();
                    _simulation.AdvanceTo(0d); Sync();
                    break;
                case NetworkCoinCollectPhase.RoundResult:
                    if (_simulation.BeginNextRound()) BeginCountdown(now);
                    else
                    {
                        _phase.Value = (byte)NetworkCoinCollectPhase.Complete;
                        AdvanceEpoch(); Sync(); ReportCompletion();
                    }
                    break;
            }
        }
        private void BeginCountdown(double now)
        {
            _roundNumber.Value = (byte)_simulation.RoundNumber;
            _phase.Value = (byte)NetworkCoinCollectPhase.Countdown;
            _phaseEndsAt.Value = now + CountdownSeconds;
            _pausedPhaseRemaining.Value = 0d;
            _items.Clear();
            _simulation.InterruptInputs(); AdvanceEpoch(); Sync(); FreezeBoardAvatars();
        }
        private void SimulateTo(double now)
        {
            if (_simulation == null || Phase != NetworkCoinCollectPhase.Running) return;
            _simulation.AdvanceTo(Math.Max(_simulation.Elapsed, now - _runningStartedAt));
            if (_simulation.IsRoundComplete)
            {
                _phase.Value = (byte)NetworkCoinCollectPhase.RoundResult;
                _phaseEndsAt.Value = now + RoundResultSeconds;
                AdvanceEpoch();
            }
            Sync();
        }
        public bool ReceiveInputOnServer(NetworkPlayerAvatar avatar, Vector2 input, int round, uint epoch)
        {
            if (!TryResolveSlot(avatar, out var slot) || !ValidateEnvelope(round, epoch) ||
                !CoinCollectRules.IsFinite(input.x) || !CoinCollectRules.IsFinite(input.y)) return false;
            SimulateTo(ServerNow);
            if (Phase != NetworkCoinCollectPhase.Running) return false;
            avatar.StopServerInputOnServer();
            return _simulation.SetMovementInput(slot, input);
        }
        public bool TryPrimaryActionOnServer(NetworkPlayerAvatar avatar, int round, uint epoch)
        {
            if (!TryResolveSlot(avatar, out var slot) || !ValidateEnvelope(round, epoch)) return false;
            SimulateTo(ServerNow);
            if (Phase != NetworkCoinCollectPhase.Running) return false;
            avatar.StopServerInputOnServer();
            var result = _simulation.TryPush(slot); Sync(); return result;
        }
        private bool ValidateEnvelope(int round, uint epoch) => IsSpawned && IsServer && _simulation != null &&
            !_paused.Value && Phase == NetworkCoinCollectPhase.Running && Remaining > 0d &&
            round == RoundNumber && MinigameServerRules.IsCurrentInputEpoch(epoch, InputEpoch);
        private bool TryResolveSlot(NetworkPlayerAvatar avatar, out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            return IsSpawned && IsServer && avatar != null && avatar.IsSpawned && CoinCollectRules.IsValidSlot(slot) &&
                NetworkMatchState.Instance != null && ReferenceEquals(NetworkMatchState.Instance.GetAvatarForSlot(slot), avatar);
        }
        public void PauseOnServer(double now)
        {
            if (!IsSpawned || !IsServer || _simulation == null || _paused.Value || !CoinCollectRules.IsFinite(now)) return;
            SimulateTo(now);
            _pausedPhaseRemaining.Value = Math.Max(0d, _phaseEndsAt.Value - now);
            _paused.Value = true;
            _simulation.InterruptInputs(); AdvanceEpoch(); Sync(); FreezeBoardAvatars();
        }
        public void ResumeOnServer(double now)
        {
            if (!IsSpawned || !IsServer || _simulation == null || !_paused.Value || !CoinCollectRules.IsFinite(now)) return;
            _phaseEndsAt.Value = now + _pausedPhaseRemaining.Value;
            _runningStartedAt = now - _simulation.Elapsed;
            _paused.Value = false; _pausedPhaseRemaining.Value = 0d;
            _simulation.InterruptInputs(); AdvanceEpoch(); Sync(); FreezeBoardAvatars();
        }
        public void RestoreAvatarForReconnectOnServer(NetworkPlayerAvatar avatar)
        {
            if (!TryResolveSlot(avatar, out var slot)) return;
            _simulation?.SetMovementInput(slot, Vector2.zero);
            avatar.StopServerInputOnServer();
        }
        public void EndMatchOnServer()
        {
            if (!IsServer) return;
            FreezeBoardAvatars(); _simulation = null;
            _phase.Value = (byte)NetworkCoinCollectPhase.Inactive; _roundNumber.Value = 0;
            _paused.Value = false; _phaseEndsAt.Value = 0d; _roundElapsed.Value = 0d;
            _items.Clear(); _players.Clear(); AdvanceEpoch();
        }
        private void ReportCompletion()
        {
            if (_completionReported || NetworkMatchState.Instance == null) return;
            var ranks = new int[4];
            for (var slot = 0; slot < 4; slot++) ranks[slot] = _simulation.GetRank(slot);
            _completionReported = NetworkMatchState.Instance.TryCompleteCoinCollectOnServer(ranks);
        }
        private void FreezeBoardAvatars()
        {
            var match = NetworkMatchState.Instance;
            for (var slot = 0; slot < 4; slot++) match?.GetAvatarForSlot(slot)?.StopServerInputOnServer();
        }
        private void AdvanceEpoch() { _inputEpoch.Value = MinigameServerRules.NextInputEpoch(_inputEpoch.Value); }
        private void Sync()
        {
            _roundElapsed.Value = _simulation.Elapsed;
            while (_players.Count < 4) _players.Add(default);
            for (var slot = 0; slot < 4; slot++)
            {
                var p = _simulation.GetPlayer(slot);
                var value = new CoinCollectPlayerNetworkSnapshot { Position = p.Position, Facing = p.Facing,
                    Score = p.Score, StunRemaining = (float)p.StunRemaining, CastRemaining = (float)p.CastRemaining,
                    CooldownRemaining = (float)p.CooldownRemaining, Rank = (byte)_simulation.GetRank(slot) };
                if (!_players[slot].Equals(value)) _players[slot] = value;
            }
            for (var i = 0; i < _simulation.ItemCount; i++)
            {
                var item = _simulation.GetItem(i);
                var value = new CoinCollectItemNetworkSnapshot { Position = item.Position, BornAt = item.BornAt,
                    InitialHeight = item.InitialHeight, PickupUnlockAt = item.PickupUnlockAt,
                    Kind = (byte)item.Kind, Active = item.Active };
                if (i >= _items.Count) _items.Add(value);
                else if (!_items[i].Equals(value)) _items[i] = value;
            }
        }
        private void OnPushStarted(int slot) { PlayPushRpc((byte)slot); }
        [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Reliable)]
        private void PlayPushRpc(byte slot)
        {
            if (CoinCollectRules.IsValidSlot(slot)) PushPresentationRequested?.Invoke(slot);
        }
        private void OnItemCollected(CoinCollectItemKind kind, int slot) { PlayCollectionRpc((byte)kind); }
        [Rpc(SendTo.ClientsAndHost, Delivery = RpcDelivery.Reliable)]
        private void PlayCollectionRpc(byte kind) { PlayCollectionSound((CoinCollectItemKind)kind); }
        public static void PlayCollectionSound(CoinCollectItemKind kind)
        {
            GameSound.Play(kind == CoinCollectItemKind.Bomb ? SoundKeys.MinigameCoinBomb :
                kind == CoinCollectItemKind.Money ? SoundKeys.MinigameMoneyPickup : SoundKeys.MinigameCoinPickup);
        }
    }
}
