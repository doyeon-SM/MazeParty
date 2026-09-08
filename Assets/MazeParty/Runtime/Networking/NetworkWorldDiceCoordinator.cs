using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Server-only orchestration for four scene-placed NetworkWorldDie objects.
    /// Dice stay server-owned and keyed by stable player slot, so reconnecting with a
    /// new client id does not transfer Rigidbody authority or replace the die.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkObject))]
    public sealed class NetworkWorldDiceCoordinator : NetworkBehaviour
    {
        [SerializeField] private NetworkWorldDie[] sceneDice =
            Array.Empty<NetworkWorldDie>();
        [SerializeField] private BoardTopology topology;
        [SerializeField] private bool autoPrepareAfterItemChoice = true;

        private readonly NetworkWorldDie[] _diceBySlot =
            new NetworkWorldDie[MultiplayerConstants.MaxPlayers];
        private readonly HashSet<NetworkWorldDie> _subscribedDice =
            new HashSet<NetworkWorldDie>();

        private int _observedTurn = -1;
        private BoardFlowState _observedFlowState = (BoardFlowState)(-1);

        public static NetworkWorldDiceCoordinator Instance { get; private set; }

        /// <summary>
        /// Server integration seam. NetworkMatchState should consume this result and
        /// atomically set the owner's private HUD roll, remaining moves and rolled mask.
        /// </summary>
        public event Action<int, int> DieSettledOnServer;

        public void ConfigureSceneDice(
            NetworkWorldDie[] dice,
            BoardTopology boardTopology)
        {
            sceneDice = dice != null
                ? (NetworkWorldDie[])dice.Clone()
                : Array.Empty<NetworkWorldDie>();
            topology = boardTopology;

            if (IsSpawned && IsServer)
            {
                ResolveDice();
            }
        }

        public override void OnNetworkSpawn()
        {
            Instance = this;
            if (IsServer)
            {
                ResolveTopology();
                ResolveDice();
            }
        }

        public override void OnNetworkDespawn()
        {
            foreach (var die in _subscribedDice)
            {
                if (die != null)
                {
                    die.SettledOnServer -= OnDieSettledOnServer;
                }
            }

            _subscribedDice.Clear();
            Array.Clear(_diceBySlot, 0, _diceBySlot.Length);
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Update()
        {
            if (!IsSpawned || !IsServer)
            {
                return;
            }

            ResolveDice();
            var match = NetworkMatchState.Instance;
            if (match == null || !match.GameplayEnabled)
            {
                return;
            }

            if (_observedTurn != match.Turn || _observedFlowState != match.FlowState)
            {
                var enteringAction = match.FlowState == BoardFlowState.Action &&
                                     (_observedFlowState != BoardFlowState.Action ||
                                      _observedTurn != match.Turn);
                if (enteringAction || match.FlowState != BoardFlowState.Action)
                {
                    HideAllDiceOnServer();
                }

                _observedTurn = match.Turn;
                _observedFlowState = match.FlowState;
            }

            SetAllPausedOnServer(match.IsReconnectPaused);
            if (autoPrepareAfterItemChoice &&
                match.FlowState == BoardFlowState.Action &&
                !match.IsReconnectPaused)
            {
                PrepareResolvedChoicesOnServer(match);
            }
        }

        public bool TryGetDie(int slot, out NetworkWorldDie die)
        {
            if (slot < 0 || slot >= _diceBySlot.Length)
            {
                die = null;
                return false;
            }

            die = _diceBySlot[slot];
            return die != null && die.IsSpawned;
        }

        public bool PrepareDieForSlotOnServer(int slot, BoardTile currentTile)
        {
            return IsServer &&
                   TryGetDie(slot, out var die) &&
                   die.PrepareOnServer(currentTile);
        }

        public void HideAllDiceOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            for (var slot = 0; slot < _diceBySlot.Length; slot++)
            {
                if (_diceBySlot[slot] != null)
                {
                    _diceBySlot[slot].HideOnServer();
                }
            }
        }

        public void SetAllPausedOnServer(bool paused)
        {
            if (!IsServer)
            {
                return;
            }

            var now = ServerNow;
            for (var slot = 0; slot < _diceBySlot.Length; slot++)
            {
                if (_diceBySlot[slot] != null)
                {
                    _diceBySlot[slot].SetSimulationPausedOnServer(paused, now);
                }
            }
        }

        public WorldDieReconnectSnapshot[] CaptureSnapshotsOnServer()
        {
            var snapshots =
                new WorldDieReconnectSnapshot[MultiplayerConstants.MaxPlayers];
            if (!IsServer)
            {
                return snapshots;
            }

            for (var slot = 0; slot < _diceBySlot.Length; slot++)
            {
                if (_diceBySlot[slot] != null)
                {
                    snapshots[slot] =
                        _diceBySlot[slot].CaptureReconnectSnapshotOnServer();
                }
            }

            return snapshots;
        }

        public bool RestoreSnapshotsOnServer(
            IReadOnlyList<WorldDieReconnectSnapshot> snapshots)
        {
            if (!IsServer ||
                snapshots == null ||
                snapshots.Count != MultiplayerConstants.MaxPlayers)
            {
                return false;
            }

            ResolveTopology();
            if (topology == null)
            {
                return false;
            }

            for (var slot = 0; slot < snapshots.Count; slot++)
            {
                var snapshot = snapshots[slot];
                if (!snapshot.IsValid)
                {
                    continue;
                }

                if (!TryGetDie(slot, out var die) ||
                    snapshot.Authority.Slot != slot)
                {
                    return false;
                }

                BoardTile tile = null;
                if (snapshot.Authority.Phase != WorldDiePhase.Hidden &&
                    (!topology.TryGetTile(
                         snapshot.Authority.TileCoordinate,
                         out tile) ||
                     tile == null))
                {
                    return false;
                }

                if (!die.RestoreReconnectSnapshotOnServer(snapshot, tile))
                {
                    return false;
                }
            }

            return true;
        }

        private void PrepareResolvedChoicesOnServer(NetworkMatchState match)
        {
            ResolveTopology();
            if (topology == null)
            {
                return;
            }

            var avatars = FindObjectsByType<NetworkPlayerAvatar>(
                FindObjectsInactive.Exclude);
            for (var i = 0; i < avatars.Length; i++)
            {
                var avatar = avatars[i];
                if (avatar == null ||
                    !avatar.IsSpawned ||
                    !avatar.IsBoardReady ||
                    !avatar.HasResolvedItemChoice ||
                    avatar.HasRolled ||
                    match.HasRolled(avatar.AssignedSlot) ||
                    !TryGetDie(avatar.AssignedSlot, out var die) ||
                    die.Phase != WorldDiePhase.Hidden)
                {
                    continue;
                }

                var tile = avatar.CurrentBoardTileOnServer ??
                           topology.FindContainingTile(
                               avatar.transform.position,
                               0.25f);
                if (tile != null)
                {
                    die.PrepareOnServer(
                        tile,
                        avatar.transform.position,
                        avatar.transform.forward);
                }
            }
        }

        private void ResolveDice()
        {
            if (!IsServer)
            {
                return;
            }

            if (sceneDice == null || sceneDice.Length == 0)
            {
                sceneDice = FindObjectsByType<NetworkWorldDie>(
                    FindObjectsInactive.Include);
            }

            Array.Clear(_diceBySlot, 0, _diceBySlot.Length);
            for (var i = 0; i < sceneDice.Length; i++)
            {
                var die = sceneDice[i];
                if (die == null || !die.IsSpawned)
                {
                    continue;
                }

                var slot = die.AssignedSlot;
                if (slot < 0 || slot >= _diceBySlot.Length)
                {
                    continue;
                }

                if (_diceBySlot[slot] != null && _diceBySlot[slot] != die)
                {
                    Debug.LogError(
                        "Multiple world dice are configured for slot " + slot + ".",
                        this);
                    continue;
                }

                _diceBySlot[slot] = die;
                if (_subscribedDice.Add(die))
                {
                    die.SettledOnServer += OnDieSettledOnServer;
                }
            }
        }

        private void ResolveTopology()
        {
            if (topology == null)
            {
                topology = FindAnyObjectByType<BoardTopology>();
            }
        }

        private void OnDieSettledOnServer(NetworkWorldDie die, int face)
        {
            if (!IsServer ||
                die == null ||
                face < WorldDieAuthorityModel.MinimumFace ||
                face > WorldDieAuthorityModel.MaximumFace)
            {
                return;
            }

            DieSettledOnServer?.Invoke(die.AssignedSlot, face);
        }

        private double ServerNow =>
            NetworkManager != null && NetworkManager.IsListening
                ? NetworkManager.ServerTime.Time
                : Time.timeAsDouble;
    }
}
