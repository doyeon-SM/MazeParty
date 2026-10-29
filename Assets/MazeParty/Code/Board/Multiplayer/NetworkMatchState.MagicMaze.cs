using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkList<MagicMazeEffectSnapshot> _magicMazeEffects = new NetworkList<MagicMazeEffectSnapshot>();
        private readonly NetworkVariable<int> _magicMazeWallGeneration = new NetworkVariable<int>();
        private int _magicMazeShuffledTurn;
        public bool IsMagicMaze => BoardMapId == MagicMazeRules.MapId;
        public bool IsMagicMazeShuffle => IsMagicMaze && FlowState == BoardFlowState.MagicMazeChange &&
            MagicMazeRules.ShufflesTiles(Turn, TotalTurns, HasTileAmplifierForTurn(Turn));
        public double MagicMazeElapsed => IsMagicMazeShuffle ? Math.Max(0d, MagicMazeRules.ShuffleDuration - StateRemaining) : 0d;
        public int MagicMazePreviewStep => IsMagicMazeShuffle ? MagicMazeRules.PreviewStep(MagicMazeElapsed) : -1;
        public int MagicMazeWallGeneration => _magicMazeWallGeneration.Value;

        private void InitializeMagicMazeReplication() => _magicMazeEffects.OnListChanged += OnMagicMazeEffectsChanged;
        private void OnMagicMazeEffectsChanged(NetworkListEvent<MagicMazeEffectSnapshot> change) => _cachedBoardEffectRevision = -1;
        private void InitializeMagicMazeOnServer()
        {
            if (!IsServer) return;
            _magicMazeEffects.Clear(); _magicMazeWallGeneration.Value = 0; _magicMazeShuffledTurn = 0;
            if (!IsMagicMaze) return;
            PublishMagicMazeEffects(BoardLandingEffectLayout.Create(_boardTopology.Tiles, BoardEffectSeed).Effects);
            RefreshMagicMazeWalls();
        }
        private void RefreshMagicMazeWalls()
        {
            if (!IsMagicMaze || BoardEffectRevision <= 0 || ActiveBoardMapRoot == null) return;
            ActiveBoardMapRoot.GetComponent<MagicMazeMap>()?.Apply(BoardEffectSeed, _magicMazeWallGeneration.Value);
        }
        private void BeginMagicMazeChangeOnServer()
        {
            if (!IsServer || !IsMagicMaze || _flow == null || _flow.State != BoardFlowState.MagicMazeChange) return;
            ResolveAllRemainingLandingEffectsOnServer();
            StopAllAvatarInputOnServer();
            if (MagicMazeRules.ChangesWalls(_flow.CurrentTurn, TotalTurns))
            {
                _magicMazeWallGeneration.Value++;
                RefreshMagicMazeWalls();
            }
        }
        private void AdvanceMagicMazeOnServer(double now)
        {
            if (!IsServer || !IsMagicMaze || _flow == null || _flow.IsPaused ||
                _flow.State != BoardFlowState.MagicMazeChange ||
                !MagicMazeRules.ShufflesTiles(_flow.CurrentTurn, TotalTurns, HasTileAmplifierForTurn(_flow.CurrentTurn)) ||
                _magicMazeShuffledTurn == _flow.CurrentTurn ||
                _flow.ToFlowTime(now) - _flow.StateStartedAt < MagicMazeRules.FinalIconAt) return;
            CommitMagicMazeShuffleOnServer();
        }
        private void CommitMagicMazeShuffleOnServer()
        {
            if (!IsServer || !IsMagicMaze || _flow == null ||
                !MagicMazeRules.ShufflesTiles(_flow.CurrentTurn, TotalTurns, HasTileAmplifierForTurn(_flow.CurrentTurn)) ||
                _magicMazeShuffledTurn == _flow.CurrentTurn || !EnsureBoardLandingEffectLayout()) return;
            var fixedTiles = GetMagicMazeFixedTiles();
            PublishMagicMazeEffects(MagicMazeRules.ShuffleEffects(_boardEffectLayout.Effects, fixedTiles,
                unchecked(BoardEffectSeed ^ (_flow.CurrentTurn * 486187739))));
            _magicMazeShuffledTurn = _flow.CurrentTurn;
        }
        private HashSet<Vector2Int> GetMagicMazeFixedTiles()
        {
            var fixedTiles = new HashSet<Vector2Int>();
            foreach (var tile in _boardTopology.Tiles)
                if (tile != null && (tile.TileType == BoardTileType.Respawn || tile.TileType == BoardTileType.KeyShop))
                    fixedTiles.Add(tile.Coordinate);
            if (KeyShopHasLocation) fixedTiles.Add(KeyShopLocation);
            for (var i = 0; i < ItemShopRules.ShopCount; i++)
            { var shop = GetItemShopSnapshot(i); if (shop.Active) fixedTiles.Add(shop.Location); }
            return fixedTiles;
        }
        private void PublishMagicMazeEffects(IReadOnlyDictionary<Vector2Int, BoardLandingEffectType> effects)
        {
            if (!IsServer) return;
            var coordinates = new List<Vector2Int>(effects.Keys);
            coordinates.Sort((a,b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
            _magicMazeEffects.Clear();
            foreach (var coordinate in coordinates) _magicMazeEffects.Add(new MagicMazeEffectSnapshot(coordinate, effects[coordinate]));
            _boardEffectRevision.Value++; _cachedBoardEffectRevision = -1;
        }
        private Dictionary<Vector2Int, BoardLandingEffectType> ReadMagicMazeEffects()
        {
            var effects = new Dictionary<Vector2Int, BoardLandingEffectType>();
            for (var i = 0; i < _magicMazeEffects.Count; i++)
                effects.Add(_magicMazeEffects[i].Coordinate, (BoardLandingEffectType)_magicMazeEffects[i].Effect);
            return effects;
        }
        public bool TryGetPresentedBoardLandingEffect(Vector2Int coordinate, out BoardLandingEffectType effect)
        {
            if (!TryGetBoardLandingEffect(coordinate, out effect)) return false;
            var step = MagicMazePreviewStep;
            if (step < 0 || GetMagicMazeFixedTiles().Contains(coordinate)) return true;
            // Visual roulette only; authoritative landing effects are never resolved here.
            var hash = unchecked((uint)(BoardEffectSeed ^ coordinate.x * 73856093 ^ coordinate.y * 19349663));
            effect = (BoardLandingEffectType)(1 + (hash + (uint)step) % 8);
            return true;
        }
        private MagicMazeEffectSnapshot[] CaptureMagicMazeEffects()
        {
            var result = new MagicMazeEffectSnapshot[_magicMazeEffects.Count];
            for (var i = 0; i < result.Length; i++) result[i] = _magicMazeEffects[i];
            return result;
        }
        private void RestoreMagicMaze(MatchRecoverySnapshot snapshot)
        {
            _magicMazeEffects.Clear();
            foreach (var effect in snapshot.magicMazeEffects) _magicMazeEffects.Add(effect);
            _magicMazeWallGeneration.Value = snapshot.magicMazeWallGeneration;
            _magicMazeShuffledTurn = snapshot.magicMazeShuffledTurn;
            _cachedBoardEffectRevision = -1;
        }
        public static bool AreMagicMazeSnapshotsValid(MatchRecoverySnapshot snapshot, BoardTopology topology = null)
        {
            if (snapshot.magicMazeEffects == null) return false;
            if (snapshot.boardMapId != MagicMazeRules.MapId)
                return snapshot.magicMazeEffects.Length == 0 && snapshot.magicMazeWallGeneration == 0 && snapshot.magicMazeShuffledTurn == 0;
            var completedBoardTurn = snapshot.checkpoint == MatchRecoveryCheckpoint.TurnOverview ? snapshot.turn - 1 : snapshot.turn;
            completedBoardTurn = Math.Min(completedBoardTurn, snapshot.scheduleTurnCount - 1);
            if (snapshot.magicMazeShuffledTurn < completedBoardTurn / 3 * 3 ||
                snapshot.magicMazeShuffledTurn > completedBoardTurn ||
                snapshot.magicMazeWallGeneration != completedBoardTurn / 5 ||
                snapshot.magicMazeEffects.Length < 1 || snapshot.magicMazeEffects.Length > 1024) return false;
            var effects = new Dictionary<Vector2Int, BoardLandingEffectType>();
            foreach (var effect in snapshot.magicMazeEffects)
                if (effect.Effect < 1 || effect.Effect > 8 || !effects.TryAdd(effect.Coordinate, (BoardLandingEffectType)effect.Effect)) return false;
            if (topology == null) return true;
            try { BoardLandingEffectLayout.Create(topology.Tiles, snapshot.boardEffectSeed).WithEffects(effects); }
            catch (ArgumentException) { return false; }
            return true;
        }
    }
}
