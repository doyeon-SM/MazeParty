using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private bool TryRestoreSavedMatchOnServer(double now, out string error)
        {
            error = string.Empty;
            if (!IsServer || _minigameSchedule == null ||
                _minigameScheduleSession == null)
            {
                error = GameText.T("The saved match could not be prepared for recovery.");
                return false;
            }

            EnsureKeyShopRuntime();
            if (_boardTopology == null)
            {
                error = GameText.T("The Board topology is not available for recovery.");
                return false;
            }

            var controller = OnlineSessionController.Instance;
            var rosterFingerprint = controller != null
                ? controller.GetMatchRosterKey()
                : string.Empty;
            if (string.IsNullOrWhiteSpace(rosterFingerprint))
            {
                error = GameText.T("The current four-player roster is not ready for recovery.");
                return false;
            }

            string contentFingerprint;
            string legacyContentFingerprint;
            try
            {
                contentFingerprint = MatchRecoveryFingerprint.CreateContentFingerprint(
                    _boardTopology,
                    _minigameSchedule);
                legacyContentFingerprint =
                    MatchRecoveryFingerprint.CreateContentFingerprint(
                        _boardTopology,
                        _minigameSchedule,
                        MatchRecoverySnapshot.
                            LegacyRecoveryVersionWithoutMines);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not create the match recovery content fingerprint: " +
                    exception.Message);
                error = GameText.T("The saved match is incompatible with this game version.");
                return false;
            }

            MatchRecoveryLoadStatus status;
            MatchRecoverySnapshot snapshot;
            try
            {
                status = _minigameScheduleSession.TryLoadRecovery(
                    rosterFingerprint,
                    contentFingerprint,
                    legacyContentFingerprint,
                    out snapshot);
            }
            catch (Exception exception)
            {
                Debug.LogWarning(
                    "Could not read the match recovery journal: " + exception.Message);
                status = MatchRecoveryLoadStatus.Corrupt;
                snapshot = null;
            }

            ServerEventTrace.Record(
                status == MatchRecoveryLoadStatus.Loaded
                    ? ServerEventCode.PersistenceRead
                    : ServerEventCode.PersistenceRejected,
                state: (int)status);
            if (status != MatchRecoveryLoadStatus.Loaded || snapshot == null)
            {
                error = GetRecoveryLoadFailure(status);
                return false;
            }

            if (!TryValidateRecoverySnapshot(
                    snapshot,
                    controller,
                    out var playerSnapshots,
                    out var avatars,
                    out var mineOwnerSlots,
                    out var currentMinigameSeed,
                    out error))
            {
                ServerEventTrace.Record(
                    ServerEventCode.PersistenceRejected,
                    snapshot.turn,
                    (int)snapshot.checkpoint);
                return false;
            }

            try
            {
                ResetTransientMatchStateForRecovery();
                RestoreBoardEffects(snapshot);
                if (!RestoreKeyShop(snapshot.keyShop) ||
                    !RestoreItemShops(snapshot.itemShops))
                {
                    error = GameText.T("The saved shop state is no longer valid.");
                    return false;
                }

                RestoreTombstones(snapshot);
                for (var slot = 0; slot < avatars.Length; slot++)
                {
                    if (!avatars[slot].RestoreMatchRecoverySnapshotOnServer(
                            playerSnapshots[slot]))
                    {
                        error = GameText.T("A saved player position is no longer valid.");
                        return false;
                    }
                }
                RestoreBoardMines(snapshot.mines, mineOwnerSlots);

                _settledMinigameTurn = snapshot.settledMinigameTurn;
                _remainingMinigameSlots.Value = snapshot.remainingMinigameSlots;
                _currentMinigame.Value = (byte)snapshot.currentMinigame;
                _currentMinigameSeed.Value = currentMinigameSeed;
                _selectedMinigameNetworkLoadCompleted = false;
                _flow.RestoreCheckpoint(
                    ToBoardFlowState(snapshot.checkpoint),
                    snapshot.turn,
                    now);
                _gameplayEnabled.Value = true;
                _snapshotRestoredMask.Value = (byte)AllPlayersMask;
                RefreshPresentMask();
                SyncKeyShopSnapshot();

                switch (snapshot.checkpoint)
                {
                    case MatchRecoveryCheckpoint.TurnOverview:
                        ForEachAvatar(avatar => avatar.PrepareForOverviewOnServer());
                        break;
                    case MatchRecoveryCheckpoint.MinigameIntroReady:
                        _minigameRevealRevision.Value++;
                        _scheduledSkipAt = CurrentMinigame == ScheduledMinigameId.Skip
                            ? now + SkipRevealSeconds
                            : 0d;
                        StopAllAvatarInputOnServer();
                        break;
                    case MatchRecoveryCheckpoint.MatchComplete:
                        _remainingMinigameSlots.Value = 0;
                        StopAllAvatarInputOnServer();
                        break;
                }

                SyncFlowSnapshot(now);
                if (snapshot.checkpoint == MatchRecoveryCheckpoint.MatchComplete)
                {
                    // MatchComplete is journaled before the first bonus key is
                    // awarded, so the ceremony can safely restart from phase one.
                    BeginAwardCeremonyOnServer(now);
                }

                ServerEventTrace.Record(
                    ServerEventCode.PersistenceRead,
                    snapshot.turn,
                    (int)snapshot.checkpoint,
                    value0: snapshot.boardEffectRevision);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                error = GameText.T("The saved match could not be rebuilt safely.");
                ServerEventTrace.Record(
                    ServerEventCode.PersistenceRejected,
                    snapshot.turn,
                    (int)snapshot.checkpoint);
                return false;
            }
        }

        private bool TryValidateRecoverySnapshot(
            MatchRecoverySnapshot snapshot,
            OnlineSessionController controller,
            out MatchRecoveryPlayerSnapshot[] playerSnapshots,
            out NetworkPlayerAvatar[] avatars,
            out int[] mineOwnerSlots,
            out ulong currentMinigameSeed,
            out string error)
        {
            playerSnapshots = null;
            avatars = null;
            mineOwnerSlots = Array.Empty<int>();
            currentMinigameSeed = 0UL;
            error = string.Empty;

            if (snapshot == null || controller == null ||
                snapshot.scheduleSeed != _minigameSchedule.Seed ||
                snapshot.scheduleTurnCount != _minigameSchedule.TurnCount ||
                snapshot.catalogEntryCount !=
                _minigameSchedule.RegisteredGameCountAtCreation ||
                !MinigameCatalog.IsRecoveryFingerprintCompatible(
                    snapshot.catalogEntryCount,
                    snapshot.catalogFingerprint) ||
                snapshot.scheduleEntries == null ||
                snapshot.scheduleEntries.Length != _minigameSchedule.TurnCount)
            {
                error = GameText.T("The saved minigame schedule is incompatible.");
                return false;
            }

            for (var turn = 1; turn <= _minigameSchedule.TurnCount; turn++)
            {
                if (snapshot.scheduleEntries[turn - 1] !=
                    (int)_minigameSchedule.GetMinigameForTurn(turn))
                {
                    error = GameText.T("The saved minigame schedule does not match this match.");
                    return false;
                }
            }

            var checkpointState = ToBoardFlowState(snapshot.checkpoint);
            if (snapshot.turn < 1 || snapshot.turn > _minigameSchedule.TurnCount ||
                checkpointState == BoardFlowState.MatchComplete &&
                snapshot.turn != _minigameSchedule.TurnCount ||
                snapshot.settledMinigameTurn < -1 ||
                snapshot.settledMinigameTurn > snapshot.turn ||
                !MatchRecoverySnapshotCodec.TryDecodeMinigameSeed(
                    snapshot.currentMinigameSeed,
                    out currentMinigameSeed))
            {
                error = GameText.T("The saved match checkpoint is invalid.");
                return false;
            }

            var expectedMinigame = checkpointState == BoardFlowState.TurnOverview
                ? ScheduledMinigameId.Skip
                : _minigameSchedule.GetMinigameForTurn(snapshot.turn);
            var expectedSeed = checkpointState == BoardFlowState.TurnOverview
                ? 0UL
                : DeriveMinigameSeed(_minigameSchedule.Seed, snapshot.turn);
            var expectedRemaining = checkpointState == BoardFlowState.MatchComplete
                ? 0
                : _minigameSchedule.TurnCount - snapshot.turn + 1;
            if (snapshot.currentMinigame != (int)expectedMinigame ||
                currentMinigameSeed != expectedSeed ||
                snapshot.remainingMinigameSlots != expectedRemaining)
            {
                error = GameText.T("The saved match checkpoint does not match its schedule.");
                return false;
            }

            if (!ValidateRecoveryShopCoordinates(snapshot))
            {
                error = GameText.T("A saved shop location is no longer available.");
                return false;
            }
            if (!ValidateRecoveryMines(snapshot))
            {
                error = GameText.T("A saved mine location is no longer valid.");
                return false;
            }

            if (snapshot.players == null ||
                snapshot.players.Length != MultiplayerConstants.MaxPlayers)
            {
                error = GameText.T("The saved player roster is incomplete.");
                return false;
            }

            var savedByPlayerKey = new Dictionary<string, MatchRecoveryPlayerSnapshot>(
                StringComparer.Ordinal);
            for (var index = 0; index < snapshot.players.Length; index++)
            {
                var saved = snapshot.players[index];
                if (saved == null || string.IsNullOrWhiteSpace(saved.playerKey) ||
                    !savedByPlayerKey.TryAdd(saved.playerKey, saved))
                {
                    error = GameText.T("The saved player roster is invalid.");
                    return false;
                }
            }

            playerSnapshots = new MatchRecoveryPlayerSnapshot[
                MultiplayerConstants.MaxPlayers];
            avatars = new NetworkPlayerAvatar[MultiplayerConstants.MaxPlayers];
            var matchedPlayerKeys = new HashSet<string>(StringComparer.Ordinal);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if (!controller.TryGetMatchPlayerIdForSlot(slot, out var playerId))
                {
                    error = GameText.T("Every player must have a server-assigned seat.");
                    return false;
                }

                var playerKey = MatchRecoveryFingerprint.CreatePlayerKey(playerId);
                var avatar = GetAvatarForSlot(slot);
                if (avatar == null ||
                    !savedByPlayerKey.TryGetValue(playerKey, out var saved) ||
                    !matchedPlayerKeys.Add(playerKey))
                {
                    error = GameText.T("The saved match belongs to a different four-player roster.");
                    return false;
                }
                if (!HasValidRecoveryTraversal(saved))
                {
                    error = GameText.T("A saved player position is no longer valid.");
                    return false;
                }

                avatars[slot] = avatar;
                playerSnapshots[slot] = saved;
            }

            if (!MatchRecoveryMineOwnership.TryMapToCurrentSlots(
                    snapshot.mines,
                    snapshot.players,
                    playerSnapshots,
                    out mineOwnerSlots))
            {
                error = GameText.T(
                    "A saved mine owner is no longer part of this match.");
                return false;
            }

            return true;
        }

        private bool HasValidRecoveryTraversal(
            MatchRecoveryPlayerSnapshot snapshot)
        {
            if (snapshot == null || snapshot.traversalHistory == null ||
                snapshot.traversalHistory.Length == 0 ||
                snapshot.traversalHistory[
                    snapshot.traversalHistory.Length - 1] !=
                snapshot.logicalTile)
            {
                return false;
            }

            for (var index = 0;
                 index < snapshot.traversalHistory.Length;
                 index++)
            {
                if (!_boardTopology.TryGetTile(
                        snapshot.traversalHistory[index],
                        out var tile) || tile == null)
                {
                    return false;
                }
            }

            return true;
        }

        private bool ValidateRecoveryShopCoordinates(
            MatchRecoverySnapshot snapshot)
        {
            var keyShopState = (KeyShopLifecycleState)snapshot.keyShop.lifecycle;
            if (snapshot.keyShop.revision < 0 ||
                keyShopState != KeyShopLifecycleState.Inactive &&
                keyShopState != KeyShopLifecycleState.Active ||
                keyShopState == KeyShopLifecycleState.Active !=
                snapshot.keyShop.hasLocation)
            {
                return false;
            }

            if (snapshot.keyShop.hasLocation &&
                (!_boardTopology.TryGetTile(
                    snapshot.keyShop.location,
                    out var keyShopTile) || keyShopTile == null))
            {
                return false;
            }

            if (snapshot.itemShops == null ||
                snapshot.itemShops.Length != ItemShopRules.ShopCount)
            {
                return false;
            }

            for (var index = 0; index < snapshot.itemShops.Length; index++)
            {
                var shop = snapshot.itemShops[index];
                if (shop.active &&
                    (!_boardTopology.TryGetTile(shop.location, out var tile) ||
                     tile == null))
                {
                    return false;
                }
            }

            return true;
        }

        private bool ValidateRecoveryMines(MatchRecoverySnapshot snapshot)
        {
            if (snapshot.mines == null ||
                snapshot.checkpoint == MatchRecoveryCheckpoint.MatchComplete &&
                snapshot.mines.Length != 0)
            {
                return false;
            }

            var armingDelay =
                PrototypeItemCatalog.Get(PrototypeItemId.Mine).ArmingDelay;
            for (var index = 0; index < snapshot.mines.Length; index++)
            {
                var mine = snapshot.mines[index];
                if (mine.ownerSlot < 0 ||
                    mine.ownerSlot >= MultiplayerConstants.MaxPlayers ||
                    float.IsNaN(mine.position.x) ||
                    float.IsInfinity(mine.position.x) ||
                    float.IsNaN(mine.position.y) ||
                    float.IsInfinity(mine.position.y) ||
                    float.IsNaN(mine.position.z) ||
                    float.IsInfinity(mine.position.z) ||
                    float.IsNaN(mine.armRemaining) ||
                    float.IsInfinity(mine.armRemaining) ||
                    mine.armRemaining < 0f ||
                    mine.armRemaining > armingDelay)
                {
                    return false;
                }

                if (!BoardItemLifecycleRules.IsValidMinePosition(
                        _boardTopology,
                        mine.position))
                {
                    return false;
                }
            }

            return true;
        }

        private void SaveMatchRecoveryCheckpoint(
            MatchRecoveryCheckpoint checkpoint)
        {
            if (!IsServer ||
                !_activeMatchVoidGate.AllowsRecoveryWrite ||
                !_gameplayEnabled.Value ||
                _minigameSchedule == null || _minigameScheduleSession == null)
            {
                return;
            }

            try
            {
                var controller = OnlineSessionController.Instance;
                var rosterFingerprint = controller != null
                    ? controller.GetMatchRosterKey()
                    : string.Empty;
                EnsureKeyShopRuntime();
                if (_boardTopology == null ||
                    string.IsNullOrWhiteSpace(rosterFingerprint))
                {
                    throw new InvalidOperationException(
                        "The recovery roster or Board topology is unavailable.");
                }

                var snapshot = CaptureMatchRecoverySnapshot(checkpoint, controller);
                var contentFingerprint =
                    MatchRecoveryFingerprint.CreateContentFingerprint(
                        _boardTopology,
                        _minigameSchedule);
                var record = _minigameScheduleSession.SaveRecovery(
                    rosterFingerprint,
                    contentFingerprint,
                    snapshot);
                ServerEventTrace.Record(
                    ServerEventCode.PersistenceWrite,
                    snapshot.turn,
                    (int)checkpoint,
                    value0: record.Revision);
            }
            catch (Exception exception)
            {
                // Persistence failure must not corrupt or stop the live match.
                Debug.LogWarning(
                    "Could not write a stable match recovery checkpoint: " +
                    exception.Message);
                ServerEventTrace.Record(
                    ServerEventCode.PersistenceRejected,
                    Turn,
                    (int)checkpoint);
            }
        }

        private MatchRecoverySnapshot CaptureMatchRecoverySnapshot(
            MatchRecoveryCheckpoint checkpoint,
            OnlineSessionController controller)
        {
            var scheduleEntries = new int[_minigameSchedule.TurnCount];
            for (var turn = 1; turn <= scheduleEntries.Length; turn++)
            {
                scheduleEntries[turn - 1] =
                    (int)_minigameSchedule.GetMinigameForTurn(turn);
            }

            var players = new MatchRecoveryPlayerSnapshot[
                MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < players.Length; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar == null ||
                    !controller.TryGetMatchPlayerIdForSlot(slot, out var playerId))
                {
                    throw new InvalidOperationException(
                        "All four authoritative player avatars are required.");
                }

                players[slot] = avatar.CaptureMatchRecoverySnapshotOnServer(
                    MatchRecoveryFingerprint.CreatePlayerKey(playerId));
                if (players[slot] == null)
                {
                    throw new InvalidOperationException(
                        "A player snapshot could not be captured.");
                }
            }

            var itemShops = new MatchRecoveryItemShopSnapshot[
                ItemShopRules.ShopCount];
            for (var index = 0; index < itemShops.Length; index++)
            {
                var shop = GetItemShopSnapshot(index);
                var offers = shop.Active
                    ? new int[ItemShopRules.OfferCount]
                    : Array.Empty<int>();
                for (var offer = 0; offer < offers.Length; offer++)
                {
                    offers[offer] = (int)shop.GetOffer(offer);
                }

                itemShops[index] = new MatchRecoveryItemShopSnapshot
                {
                    active = shop.Active,
                    location = shop.Location,
                    revision = shop.Revision,
                    appearedTurn = shop.AppearedTurn,
                    soldMask = shop.SoldMask,
                    offers = offers
                };
            }

            var tombstones = new MatchRecoveryTombstoneSnapshot[_tombstones.Count];
            for (var index = 0; index < _tombstones.Count; index++)
            {
                var tombstone = _tombstones[index];
                tombstones[index] = new MatchRecoveryTombstoneSnapshot
                {
                    id = tombstone.Id,
                    position = tombstone.Position,
                    gold = tombstone.Gold
                };
            }

            var mines = checkpoint == MatchRecoveryCheckpoint.MatchComplete
                ? Array.Empty<MatchRecoveryMineSnapshot>()
                : new MatchRecoveryMineSnapshot[_boardMines.Count];
            for (var index = 0; index < mines.Length; index++)
            {
                var mine = _boardMines[index];
                mines[index] = new MatchRecoveryMineSnapshot
                {
                    ownerSlot = mine.OwnerSlot,
                    position = mine.Position,
                    armRemaining = mine.ArmRemaining
                };
            }

            var stableKeyShopState = _keyShopRuntime != null &&
                                     _keyShopRuntime.HasLocation
                ? KeyShopLifecycleState.Active
                : KeyShopLifecycleState.Inactive;
            var checkpointState = ToBoardFlowState(checkpoint);
            var minigame = checkpointState == BoardFlowState.TurnOverview
                ? ScheduledMinigameId.Skip
                : CurrentMinigame;
            var minigameSeed = checkpointState == BoardFlowState.TurnOverview
                ? 0UL
                : _currentMinigameSeed.Value;
            var remaining = checkpointState == BoardFlowState.MatchComplete
                ? 0
                : _minigameSchedule.TurnCount - Turn + 1;

            return new MatchRecoverySnapshot
            {
                checkpoint = checkpoint,
                turn = Turn,
                settledMinigameTurn = _settledMinigameTurn,
                scheduleSeed = _minigameSchedule.Seed,
                scheduleTurnCount = _minigameSchedule.TurnCount,
                catalogEntryCount =
                    _minigameSchedule.RegisteredGameCountAtCreation,
                catalogFingerprint = MinigameCatalog.GetRecoveryFingerprint(
                    _minigameSchedule.RegisteredGameCountAtCreation),
                scheduleEntries = scheduleEntries,
                currentMinigame = (int)minigame,
                currentMinigameSeed =
                    MatchRecoverySnapshotCodec.EncodeMinigameSeed(minigameSeed),
                remainingMinigameSlots = remaining,
                boardEffectSeed = _boardEffectSeed.Value,
                boardEffectRevision = _boardEffectRevision.Value,
                keyShop = new MatchRecoveryKeyShopSnapshot
                {
                    lifecycle = (byte)stableKeyShopState,
                    hasLocation = stableKeyShopState ==
                                  KeyShopLifecycleState.Active,
                    location = _keyShopRuntime != null
                        ? _keyShopRuntime.Location
                        : default,
                    revision = _keyShopRuntime != null
                        ? _keyShopRuntime.PlacementRevision
                        : 0
                },
                itemShops = itemShops,
                tombstones = tombstones,
                nextTombstoneId = _nextTombstoneId,
                mines = mines,
                players = players
            };
        }

        private void ResetTransientMatchStateForRecovery()
        {
            EndAllMinigameRuntimes();
            ResetCombatRuntimeOnServer();
            ClearBoardItemWorld();
            _rolledMask.Value = 0;
            _arrivedMask.Value = 0;
            _readyMask.Value = 0;
            _overviewPositionMask.Value = 0;
            ClearArrivalGraceOnServer();
            ResetArrivalTimes();
            _endingForReconnectTimeout = false;
            _endingForMinigameLoadingTimeout = false;
            _reconnectPaused.Value = false;
            _reconnectGraceEndsAt.Value = 0d;
            _pausedStateRemaining.Value = 0d;
            _pausedActionRemaining.Value = 0d;
            _pausedChoiceRemaining.Value = 0d;
            _pausedShieldRemaining.Value = 0d;
            ClearPlayerPauseStateOnServer();
            Array.Clear(_reconnectSnapshots, 0, _reconnectSnapshots.Length);
            _keyShopAppearanceEndsAt = 0d;
            _keyShopRevealActive.Value = false;
            _keyShopRevealEndsAt.Value = 0d;
            _keyShopRevealRemainingDuringReconnect = 0d;
            _scheduledSkipAt = 0d;
            _scheduledSkipPaused = false;
            _pausedScheduledSkipRemaining = 0d;
            _nextLandingEffectSlot = 0;
            _lastLandingEffectMessage.Value = default;
            _lastLandingEffectRevision.Value++;
            _lastActionEndReason.Value = (byte)BoardActionEndReason.None;
            ResetAwardCeremonyForRecovery();
        }

        private void RestoreBoardMines(
            MatchRecoveryMineSnapshot[] snapshots,
            int[] ownerSlots)
        {
            if (snapshots == null || ownerSlots == null ||
                snapshots.Length != ownerSlots.Length)
            {
                throw new InvalidOperationException(
                    "Saved mine ownership could not be restored safely.");
            }

            _boardMines.Clear();
            for (var index = 0; index < snapshots.Length; index++)
            {
                var snapshot = snapshots[index];
                _boardMines.Add(new PlantedMine
                {
                    OwnerSlot = ownerSlots[index],
                    Position = snapshot.position,
                    ArmRemaining = snapshot.armRemaining
                });
            }

            SyncBoardMines();
        }

        private void ResetAwardCeremonyForRecovery()
        {
            _awardCeremonyPhase.Value = (byte)AwardCeremonyPhase.None;
            _awardCeremonyPhaseEndsAt.Value = 0d;
            _pausedAwardCeremonyRemaining.Value = 0d;
            _awardCeremonyAutoReturnEndsAt.Value = 0d;
            _pausedAwardCeremonyAutoReturnRemaining.Value = 0d;
            _awardCeremonyCategory0.Value = 0;
            _awardCeremonyCategory1.Value = 0;
            _awardCeremonyWinnerMask0.Value = 0;
            _awardCeremonyWinnerMask1.Value = 0;
            _awardCeremonyWinningValue0.Value = 0;
            _awardCeremonyWinningValue1.Value = 0;
            _awardCeremonyReturnReadyMask.Value = 0;
            _awardCeremonyRemainingMask.Value = 0;
            _completedMatchReturnQueued = false;
            SetFinalCeremonyRanks(null);
            _awardCeremonyRevision.Value++;
        }

        private void RestoreBoardEffects(MatchRecoverySnapshot snapshot)
        {
            _boardEffectSeed.Value = snapshot.boardEffectSeed;
            _boardEffectRevision.Value = snapshot.boardEffectRevision;
            _cachedBoardEffectRevision = -1;
            _boardEffectLayout = null;
            if (!EnsureBoardLandingEffectLayout())
            {
                throw new InvalidOperationException(
                    "The saved Board effect layout could not be restored.");
            }
        }

        private bool RestoreKeyShop(MatchRecoveryKeyShopSnapshot snapshot)
        {
            return _keyShopRuntime.RestoreCheckpoint(
                (KeyShopLifecycleState)snapshot.lifecycle,
                snapshot.hasLocation,
                snapshot.location,
                snapshot.revision,
                _boardTopology.Tiles);
        }

        private bool RestoreItemShops(MatchRecoveryItemShopSnapshot[] snapshots)
        {
            if (snapshots == null || snapshots.Length != ItemShopRules.ShopCount)
            {
                return false;
            }

            for (var index = 0; index < snapshots.Length; index++)
            {
                var snapshot = snapshots[index];
                if (!snapshot.active)
                {
                    _itemShopStocks[index] = null;
                    SetItemShopSnapshot(index, default);
                    continue;
                }

                if (snapshot.offers == null ||
                    snapshot.offers.Length != ItemShopRules.OfferCount)
                {
                    return false;
                }

                var offers = new PrototypeItemId[ItemShopRules.OfferCount];
                for (var offer = 0; offer < offers.Length; offer++)
                {
                    offers[offer] = (PrototypeItemId)snapshot.offers[offer];
                }

                var stock = ItemShopStock.Restore(offers, snapshot.soldMask);
                _itemShopStocks[index] = stock;
                SetItemShopSnapshot(
                    index,
                    ItemShopSnapshot.Create(
                        snapshot.location,
                        snapshot.revision,
                        snapshot.appearedTurn,
                        stock));
            }

            return true;
        }

        private void RestoreTombstones(MatchRecoverySnapshot snapshot)
        {
            _tombstones.Clear();
            for (var index = 0; index < snapshot.tombstones.Length; index++)
            {
                var saved = snapshot.tombstones[index];
                _tombstones.Add(new BoardTombstoneSnapshot
                {
                    Id = saved.id,
                    Position = saved.position,
                    Gold = saved.gold
                });
            }

            _nextTombstoneId = snapshot.nextTombstoneId;
        }

        private static BoardFlowState ToBoardFlowState(
            MatchRecoveryCheckpoint checkpoint)
        {
            switch (checkpoint)
            {
                case MatchRecoveryCheckpoint.TurnOverview:
                    return BoardFlowState.TurnOverview;
                case MatchRecoveryCheckpoint.MinigameIntroReady:
                    return BoardFlowState.MinigameIntroReady;
                case MatchRecoveryCheckpoint.MatchComplete:
                    return BoardFlowState.MatchComplete;
                default:
                    throw new ArgumentOutOfRangeException(nameof(checkpoint));
            }
        }

        private static string GetRecoveryLoadFailure(
            MatchRecoveryLoadStatus status)
        {
            switch (status)
            {
                case MatchRecoveryLoadStatus.Expired:
                    return GameText.T("The saved match expired after 72 hours.");
                case MatchRecoveryLoadStatus.RosterMismatch:
                    return GameText.T("The saved match requires the same four accounts.");
                case MatchRecoveryLoadStatus.ContentMismatch:
                    return GameText.T("The saved match is incompatible with this game version.");
                case MatchRecoveryLoadStatus.Corrupt:
                    return GameText.T("The saved match is incomplete or damaged.");
                default:
                    return GameText.T("No recoverable saved match was found.");
            }
        }
    }
}
