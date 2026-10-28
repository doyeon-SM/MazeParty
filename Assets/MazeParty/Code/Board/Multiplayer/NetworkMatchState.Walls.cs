using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkList<BoardWallSnapshot> _boardWalls = new NetworkList<BoardWallSnapshot>();
        private readonly Dictionary<BoardGate, GameObject> _boardWallViews = new Dictionary<BoardGate, GameObject>();
        private readonly List<BoardGate> _removedWallGates = new List<BoardGate>();
        private BoardTopology _wallViewTopology;
        private bool _wallViewsDirty = true;
        private int _wallViewTurn = -1;
        public int PlacedWallCount => _boardWalls.Count;
        public BoardWallSnapshot GetPlacedWall(int index) => _boardWalls[index];

        private void InitializeBoardWalls()
        {
            _boardWalls.OnListChanged += OnBoardWallsChanged;
            _wallViewsDirty = true;
        }
        private void OnBoardWallsChanged(NetworkListEvent<BoardWallSnapshot> change) => _wallViewsDirty = true;

        public bool TryGetWallPlacement(NetworkPlayerAvatar avatar, Vector3 origin, Vector3 direction, out BoardGate gate)
        {
            gate = null;
            if (BoardMapId != BoardWallRules.MapId || _boardTopology == null || avatar == null ||
                !avatar.HasLogicalBoardTile || avatar.CurrentHealth <= 0 ||
                !_boardTopology.TryGetTile(avatar.LogicalBoardTileCoordinate, out var tile) ||
                !BoardWallRules.TryAim(_boardTopology, tile, origin, direction, out var aimed, out var point)) return false;
            var distance = Vector3.Distance(origin, point);
            if (BoardItemPhysics.Cast(origin, direction, Mathf.Max(0f, distance - .05f), avatar.gameObject, out _)) return false;
            if (!BoardWallRules.CanClose(_boardTopology, aimed) || IsWallSpaceOccupied(aimed)) return false;
            gate = aimed;
            return true;
        }

        private bool IsWallSpaceOccupied(BoardGate gate)
        {
            var inverse = Quaternion.Inverse(BoardWallRules.Rotation(gate));
            var basePosition = BoardWallRules.Position(gate);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var player = GetAvatarForSlot(slot);
                if (player == null || !player.IsBoardReady) continue;
                var controller = player.GetComponent<CharacterController>();
                var radius = controller != null ? BoardGate.GetMaximumPlanarCapsuleSupport(controller, Vector3.up) : .5f;
                var center = controller != null ? controller.transform.TransformPoint(controller.center)
                    : player.transform.position + Vector3.up * .75f;
                var height = controller != null ? Mathf.Max(radius * 2f, controller.height * controller.transform.lossyScale.y) : 1.5f;
                var local = inverse * (center - basePosition);
                if (Mathf.Abs(local.x) <= gate.GateWidth * .5f + radius + .05f &&
                    Mathf.Abs(local.z) <= BoardWallRules.Thickness * .5f + radius + .05f &&
                    local.y + height * .5f >= 0f && local.y - height * .5f <= BoardWallRules.Height) return true;
            }
            return false;
        }

        private void PlaceBoardWallOnServer(BoardGate gate)
        {
            _boardWalls.Add(new BoardWallSnapshot(gate.Source.Coordinate, gate.Destination.Coordinate,
                checked(Turn + BoardWallRules.LifetimeTurns)));
            _wallViewsDirty = true;
            RefreshBoardWallPresentation();
            Physics.SyncTransforms();
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                GetAvatarForSlot(slot)?.RefreshTravelAfterWallPlacementOnServer();
        }

        private void ExpireBoardWallsOnServer(int turn)
        {
            if (!IsServer) return;
            for (var i = _boardWalls.Count - 1; i >= 0; i--)
                if (_boardWalls[i].ExpiresAtTurn <= turn || FlowState == BoardFlowState.MatchComplete)
                { _boardWalls.RemoveAt(i); _wallViewsDirty = true; }
            RefreshBoardWallPresentation();
        }

        private void RefreshBoardWallPresentation()
        {
            var topology = _boardTopology;
            if (!_wallViewsDirty && _wallViewTurn == Turn && _wallViewTopology == topology) return;
            if (_wallViewTopology != topology) ClearBoardWallViews();
            _wallViewTopology = topology;
            if (topology == null) return;
            _wallViewsDirty = false; _wallViewTurn = Turn;
            topology.NotifyTraversalChanged();
            foreach (var gate in topology.Gates) if (gate != null) gate.SetTemporaryWallExpiry(0);
            for (var i = 0; i < _boardWalls.Count; i++)
            {
                var wall = _boardWalls[i];
                if (wall.ExpiresAtTurn <= Turn) continue;
                var gate = BoardWallRules.FindGate(topology, wall.From, wall.To);
                var reverse = BoardWallRules.Reverse(topology, gate);
                if (reverse == null) continue;
                gate.SetTemporaryWallExpiry(wall.ExpiresAtTurn);
                reverse.SetTemporaryWallExpiry(wall.ExpiresAtTurn);
                if (_boardWallViews.ContainsKey(gate)) continue;
                var view = Instantiate(PrototypeItemCatalog.Get(PrototypeItemId.Wall).WorldPrefab, gate.transform);
                view.transform.SetPositionAndRotation(BoardWallRules.Position(gate), BoardWallRules.Rotation(gate));
                view.transform.localScale = new Vector3(gate.GateWidth, 1f, 1f);
                _boardWallViews.Add(gate, view);
            }
            _removedWallGates.Clear();
            foreach (var pair in _boardWallViews)
                if (pair.Key == null || !pair.Key.IsTemporarilyBlocked) _removedWallGates.Add(pair.Key);
            foreach (var gate in _removedWallGates)
            {
                ReleaseWallView(_boardWallViews[gate]); _boardWallViews.Remove(gate);
            }
        }

        private static void ReleaseWallView(GameObject view)
        {
            if (view == null) return;
            view.SetActive(false);
            if (Application.isPlaying) Destroy(view); else DestroyImmediate(view);
        }

        private void ClearBoardWallViews()
        {
            foreach (var pair in _boardWallViews)
            {
                if (pair.Key != null) pair.Key.SetTemporaryWallExpiry(0);
                ReleaseWallView(pair.Value);
            }
            _boardWallViews.Clear();
            if (_wallViewTopology != null)
            {
                foreach (var gate in _wallViewTopology.Gates) if (gate != null) gate.SetTemporaryWallExpiry(0);
                _wallViewTopology.NotifyTraversalChanged();
            }
            _wallViewTopology = null; _wallViewTurn = -1; _wallViewsDirty = true;
        }

        private void ClearBoardWalls()
        {
            if (IsServer) _boardWalls.Clear();
            ClearBoardWallViews();
        }

        private BoardWallSnapshot[] CaptureBoardWalls(MatchRecoveryCheckpoint checkpoint)
        {
            if (checkpoint == MatchRecoveryCheckpoint.MatchComplete) return Array.Empty<BoardWallSnapshot>();
            var walls = new List<BoardWallSnapshot>();
            for (var i = 0; i < _boardWalls.Count; i++)
                if (_boardWalls[i].ExpiresAtTurn > Turn) walls.Add(_boardWalls[i]);
            return walls.ToArray();
        }

        private bool ValidateRecoveryWalls(MatchRecoverySnapshot snapshot)
        {
            if (!AreWallSnapshotsValid(snapshot.walls, snapshot.boardMapId, snapshot.turn, snapshot.checkpoint)) return false;
            if (snapshot.walls.Length == 0) return true;
            if (_boardTopology == null) return false;
            var blocked = new HashSet<BoardGate>();
            foreach (var wall in snapshot.walls)
            {
                var gate = BoardWallRules.FindGate(_boardTopology, wall.From, wall.To);
                var reverse = BoardWallRules.Reverse(_boardTopology, gate);
                if (reverse == null || !BoardWallRules.IsCanonical(gate) || !blocked.Add(gate) || !blocked.Add(reverse)) return false;
            }
            return BoardWallRules.RemainsConnected(_boardTopology, blocked);
        }

        public static bool AreWallSnapshotsValid(BoardWallSnapshot[] walls, string mapId, int turn, MatchRecoveryCheckpoint checkpoint)
        {
            if (walls == null || walls.Length > 128 || walls.Length > 0 &&
                (mapId != BoardWallRules.MapId || checkpoint == MatchRecoveryCheckpoint.MatchComplete)) return false;
            var keys = new HashSet<(Vector2Int, Vector2Int)>();
            foreach (var wall in walls)
                if (wall.ExpiresAtTurn <= turn || (long)wall.ExpiresAtTurn > (long)turn + BoardWallRules.LifetimeTurns ||
                    wall.From == wall.To || wall.From.x > wall.To.x || wall.From.x == wall.To.x && wall.From.y >= wall.To.y ||
                    !keys.Add((wall.From, wall.To))) return false;
            return true;
        }

        private void RestoreBoardWalls(BoardWallSnapshot[] walls)
        {
            _boardWalls.Clear();
            foreach (var wall in walls) _boardWalls.Add(wall);
            _wallViewsDirty = true;
        }
    }
}
