using System;
using System.Globalization;
using MazeParty.Gameplay;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkVariable<int> _tileAmplifierTurn = new();
        private readonly NetworkVariable<byte> _tileAmplifierUsers = new();

        private bool HasTileAmplifierForTurn(int turn) => IsMagicMaze &&
            _tileAmplifierTurn.Value == turn && _tileAmplifierUsers.Value != 0;
        // Derive the periodic bonus from the authoritative turn. It survives
        // reconnects/checkpoint restores without an extra saved flag or item user.
        private int LandingEffectMultiplier => IsMagicMaze
            ? TileAmplifierRules.Multiplier(
                HasTileAmplifierForTurn(Turn) ? _tileAmplifierUsers.Value : (byte)0,
                TileAmplifierRules.IsAutomaticTurn(Turn)) : 1;
        public int TileAmplifierPresentationMultiplier => GameplayEnabled &&
            FlowState <= BoardFlowState.LandingEffectResolve ? LandingEffectMultiplier : 1;
        public bool HasUsedTileAmplifier(int slot) => slot >= 0 && slot < MultiplayerConstants.MaxPlayers &&
            HasTileAmplifierForTurn(Turn) && (_tileAmplifierUsers.Value & (1 << slot)) != 0;
        public bool ShowTileAmplifierOutline(int slot) => GameplayEnabled &&
            FlowState <= BoardFlowState.LandingEffectResolve && HasUsedTileAmplifier(slot);

        private void RegisterTileAmplifierOnServer(int slot)
        {
            if (!IsServer || !IsMagicMaze || slot < 0 || slot >= MultiplayerConstants.MaxPlayers) return;
            if (_tileAmplifierTurn.Value != Turn)
            {
                _tileAmplifierTurn.Value = Turn;
                _tileAmplifierUsers.Value = 0;
            }
            _tileAmplifierUsers.Value |= (byte)(1 << slot);
            _flow?.TryRequestMagicMazeShuffle();
        }

        private void ResetTileAmplifierOnServer()
        {
            if (!IsServer) return;
            _tileAmplifierTurn.Value = 0;
            _tileAmplifierUsers.Value = 0;
        }

        private int GetLandingItemRewardSeed(int slot)
        {
            if (!IsMagicMaze) return unchecked(BoardEffectSeed ^ (Turn * 486187739) ^ (slot * 16777619));
            var controller = OnlineSessionController.Instance;
            var key = controller != null && controller.TryGetMatchPlayerIdForSlot(slot, out var playerId)
                ? MatchRecoveryFingerprint.CreatePlayerKey(playerId)
                : "local-slot:" + slot.ToString(CultureInfo.InvariantCulture);
            return TileAmplifierRules.RewardSeed(BoardEffectSeed, Turn, key);
        }

        private void RestoreTileAmplifier(MatchRecoverySnapshot snapshot, MatchRecoveryPlayerSnapshot[] playersByCurrentSlot)
        {
            ResetTileAmplifierOnServer();
            _tileAmplifierTurn.Value = snapshot.tileAmplifierTurn;
            for (var slot = 0; slot < playersByCurrentSlot.Length; slot++)
                if (playersByCurrentSlot[slot].usedTileAmplifier)
                    _tileAmplifierUsers.Value |= (byte)(1 << slot);
            // Stable recovery checkpoints are before item use or after ALL landing rewards and shuffling.
            // A reconnect keeps the live NetworkVariables/flow; a process restart restores the whole checkpoint.
        }

        public static bool AreTileAmplifierSnapshotsValid(MatchRecoverySnapshot snapshot)
        {
            if (snapshot.players == null) return false;
            var used = false;
            foreach (var player in snapshot.players)
            {
                if (player == null) return false;
                used |= player.usedTileAmplifier;
            }
            if (!used) return snapshot.tileAmplifierTurn == 0;
            return snapshot.boardMapId == MagicMazeRules.MapId &&
                snapshot.checkpoint != MatchRecoveryCheckpoint.TurnOverview &&
                snapshot.tileAmplifierTurn == snapshot.turn && snapshot.turn > 0 &&
                (snapshot.turn == snapshot.scheduleTurnCount || snapshot.magicMazeShuffledTurn == snapshot.turn);
        }
    }
}
