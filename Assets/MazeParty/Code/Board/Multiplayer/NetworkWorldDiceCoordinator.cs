using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent, RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkWorldDiceCoordinator : NetworkBehaviour
    {
        [SerializeField] private NetworkWorldDie[] sceneDice = Array.Empty<NetworkWorldDie>();
        [SerializeField] private BoardTopology topology;
        [SerializeField] private bool autoPrepareAfterItemChoice = true;
        private readonly NetworkWorldDie[] _dice = new NetworkWorldDie[8];
        private readonly bool[] _started = new bool[8];
        private readonly bool[] _prepared = new bool[8];
        private readonly HashSet<NetworkWorldDie> _subscribed = new HashSet<NetworkWorldDie>();
        private int _turn = -1;
        private BoardFlowState _phase = (BoardFlowState)(-1);
        public static NetworkWorldDiceCoordinator Instance { get; private set; }
        public event Action<int, int> DieSettledOnServer;
        private static int Index(int slot, int dieIndex) => slot * 2 + dieIndex;

        public void ConfigureSceneDice(NetworkWorldDie[] dice, BoardTopology boardTopology)
        { sceneDice = dice; topology = boardTopology; }
        public override void OnNetworkSpawn() { Instance = this; ResolveDice(); }
        public override void OnNetworkDespawn()
        {
            foreach (var die in _subscribed) if (die != null)
            { die.RollStartedOnServer -= OnStarted; die.SettledOnServer -= OnSettled; }
            _subscribed.Clear();
            if (Instance == this) Instance = null;
        }

        private void ResolveDice()
        {
            if (sceneDice == null || sceneDice.Length == 0)
                sceneDice = FindObjectsByType<NetworkWorldDie>(FindObjectsInactive.Include);
            foreach (var die in sceneDice)
            {
                if (die == null || !die.IsSpawned || die.AssignedSlot < 0 || die.AssignedSlot >= 4) continue;
                var index = Index(die.AssignedSlot, die.DieIndex);
                if (_dice[index] != null && _dice[index] != die)
                { Debug.LogError("Duplicate board die slot/index.", this); continue; }
                _dice[index] = die;
                if (IsServer && _subscribed.Add(die))
                { die.RollStartedOnServer += OnStarted; die.SettledOnServer += OnSettled; }
            }
        }
        private void Update()
        {
            if (!IsSpawned || !IsServer) return;
            ResolveDice();
            var match = NetworkMatchState.Instance;
            if (match == null || !match.GameplayEnabled) return;
            if (_turn != match.Turn || _phase != match.FlowState)
            {
                if (match.IsActionPhase) HideAllDiceOnServer();
                else foreach (var die in _dice)
                    if (die != null && !WorldDieResultPresentationPolicy.ShouldPreserveAcrossActionExit(die.Phase)) die.HideOnServer();
                _turn = match.Turn; _phase = match.FlowState;
            }
            SetAllPausedOnServer(match.IsGlobalSimulationPaused);
            if (!autoPrepareAfterItemChoice || !match.CanAcceptActionInput) return;
            if (topology == null) topology = FindAnyObjectByType<BoardTopology>();
            if (topology == null) return;
            for (int slot = 0; slot < 4; slot++)
            {
                var avatar = match.GetAvatarForSlot(slot);
                if (avatar == null || !avatar.IsBoardReady || !avatar.HasResolvedItemChoice || avatar.HasRolled) continue;
                var tile = avatar.CurrentBoardTileOnServer ?? topology.FindContainingTile(avatar.transform.position, .25f);
                if (tile == null) continue;
                for (int n = 0; n < (avatar.UsesDoubleDice ? 2 : 1); n++)
                {
                    var index = Index(slot, n);
                    if (_prepared[index] || avatar.HasDieResultOnServer(n) || !TryGetDie(slot, n, out var die)) continue;
                    var lateral = avatar.UsesDoubleDice ? avatar.transform.right * (n == 0 ? -.65f : .65f) : Vector3.zero;
                    _prepared[index] = die.PrepareOnServer(tile, avatar.transform.position + lateral, avatar.transform.forward);
                }
            }
        }

        public void RelocatePendingDiceOnServer(NetworkPlayerAvatar avatar)
        {
            if (!IsServer || avatar == null || avatar.HasRolled || avatar.CurrentBoardTileOnServer == null) return;
            for (int n = 0; n < (avatar.UsesDoubleDice ? 2 : 1); n++)
            {
                if (avatar.HasDieResultOnServer(n) || !TryGetDie(avatar.AssignedSlot, n, out var die) || die.Phase == WorldDiePhase.Rolling) continue;
                die.HideOnServer();
                var lateral = avatar.UsesDoubleDice ? avatar.transform.right * (n == 0 ? -.65f : .65f) : Vector3.zero;
                _prepared[Index(avatar.AssignedSlot, n)] = die.PrepareOnServer(avatar.CurrentBoardTileOnServer,
                    avatar.transform.position + lateral, avatar.transform.forward);
            }
        }

        public bool TryGetDie(int slot, out NetworkWorldDie die) => TryGetDie(slot, 0, out die);
        public bool TryGetDie(int slot, int n, out NetworkWorldDie die)
        {
            ResolveDice();
            die = slot >= 0 && slot < 4 && n >= 0 && n < 2 ? _dice[Index(slot, n)] : null;
            return die != null && die.IsSpawned;
        }
        public bool PrepareDieForSlotOnServer(int slot, BoardTile tile) =>
            IsServer && TryGetDie(slot, out var die) && die.PrepareOnServer(tile);
        public bool HasInFlightRollOnServer()
        { for (int slot = 0; slot < 4; slot++) if (IsRollInFlightOnServer(slot)) return true; return false; }
        public bool IsRollInFlightOnServer(int slot)
        {
            if (!IsServer || slot < 0 || slot >= 4) return false;
            return _started[Index(slot, 0)] || _started[Index(slot, 1)];
        }
        public void HideAllDiceOnServer()
        {
            if (!IsServer) return;
            foreach (var die in _dice) if (die != null) die.HideOnServer();
            Array.Clear(_prepared, 0, 8); Array.Clear(_started, 0, 8);
        }
        public void SetAllPausedOnServer(bool paused)
        {
            if (!IsServer) return;
            foreach (var die in _dice) if (die != null) die.SetSimulationPausedOnServer(paused, ServerNow);
        }
        public WorldDieReconnectSnapshot[] CaptureSnapshotsOnServer()
        {
            var snapshots = new WorldDieReconnectSnapshot[8];
            if (IsServer) for (int i = 0; i < 8; i++) if (_dice[i] != null)
                snapshots[i] = _dice[i].CaptureReconnectSnapshotOnServer();
            return snapshots;
        }
        public bool RestoreSnapshotsOnServer(IReadOnlyList<WorldDieReconnectSnapshot> snapshots)
        {
            if (!IsServer || snapshots == null || snapshots.Count != 8) return false;
            ResolveDice();
            if (topology == null) topology = FindAnyObjectByType<BoardTopology>();
            if (topology == null) return false;
            // Validate the entire snapshot before applying any state.
            for (int i = 0; i < 8; i++)
            {
                var s = snapshots[i];
                if (!s.IsValid) continue;
                if (_dice[i] == null || s.Authority.Slot != i / 2 || s.DieIndex != i % 2 ||
                    (s.Authority.Phase != WorldDiePhase.Hidden && !topology.TryGetTile(s.Authority.TileCoordinate, out _))) return false;
            }
            for (int i = 0; i < 8; i++)
            {
                var s = snapshots[i];
                if (!s.IsValid) continue;
                topology.TryGetTile(s.Authority.TileCoordinate, out var tile);
                if (!_dice[i].RestoreReconnectSnapshotOnServer(s, tile)) return false;
                _started[i] = s.Authority.Phase == WorldDiePhase.Rolling;
                _prepared[i] = s.Authority.Phase != WorldDiePhase.Hidden;
            }
            return true;
        }
        private void OnStarted(NetworkWorldDie die)
        { if (IsServer) _started[Index(die.AssignedSlot, die.DieIndex)] = true; }
        private void OnSettled(NetworkWorldDie die, int face)
        {
            if (!IsServer || die == null || die.AssignedSlot < 0 || die.AssignedSlot >= 4) return;
            var index = Index(die.AssignedSlot, die.DieIndex);
            if (!_started[index] || _dice[index] != die || die.Phase != WorldDiePhase.Settled || die.PublicFace != face) return;
            try
            {
                var avatar = NetworkMatchState.Instance?.GetAvatarForSlot(die.AssignedSlot);
                int sum = avatar != null ? avatar.RecordDieResultOnServer(die.DieIndex, face) : 0;
                if (sum > 0) DieSettledOnServer?.Invoke(die.AssignedSlot, sum);
            }
            finally { _started[index] = false; }
        }
        private double ServerNow => NetworkManager != null && NetworkManager.IsListening
            ? NetworkManager.ServerTime.Time : Time.timeAsDouble;
    }
}
