using System;
using System.Globalization;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private const double SpecialEventPreviewIntervalSeconds = 0.1d;

        private readonly BoardLandingEffectType[] _landingEffectPlan =
            new BoardLandingEffectType[MultiplayerConstants.MaxPlayers];
        private readonly BoardTile[] _landingEffectTiles =
            new BoardTile[MultiplayerConstants.MaxPlayers];
        private readonly BoardSpecialEventRules.Resolution[] _specialEventPlan =
            new BoardSpecialEventRules.Resolution[MultiplayerConstants.MaxPlayers];
        private readonly double[] _landingEffectDurations =
            new double[MultiplayerConstants.MaxPlayers];
        private readonly int[] _landingEffectOrder =
            new int[MultiplayerConstants.MaxPlayers];
        private bool _landingEffectPlanReady;
        private byte _landingEffectAppliedMask;
        private double _landingEffectSlotStartsAt;
        private int _lastLandingEffectPreviewKey = -1;

        private double PrepareLandingEffectPlanOnServer()
        {
            var hasLayout = IsServer && EnsureBoardLandingEffectLayout();
            var totalDuration = 0d;
            var rankingStats = new PlayerRankingStats[MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                _landingEffectOrder[slot] = slot;
                rankingStats[slot] = avatar != null
                    ? new PlayerRankingStats(
                        avatar.KeyCount, avatar.Gold, avatar.MinigameWins)
                    : default;
                var tile = avatar != null ? avatar.CurrentBoardTileOnServer : null;
                var effect = BoardLandingEffectType.None;
                if (hasLayout && tile != null)
                {
                    _boardEffectLayout.TryGetEffect(tile.Coordinate, out effect);
                }

                _landingEffectTiles[slot] = tile;
                _landingEffectPlan[slot] = effect;
                _specialEventPlan[slot] = effect == BoardLandingEffectType.SpecialEvent
                    ? BoardSpecialEventRules.Resolve(
                        _boardEffectSeed.Value,
                        unchecked((_boardEffectRevision.Value * 486187739) ^ _turn.Value),
                        slot,
                        tile != null ? tile.Coordinate : default,
                        MultiplayerConstants.MaxPlayers)
                    : default;
                var isTransfer = effect == BoardLandingEffectType.SpecialEvent &&
                                 _specialEventPlan[slot].Family ==
                                     BoardSpecialEventFamily.Transfer;
                _landingEffectDurations[slot] = isTransfer
                    ? BoardLandingEffectLayout.SpecialEventRouletteDurationSeconds +
                      BoardResourceTransferPresentationRules.TotalDurationSeconds
                    : BoardLandingEffectLayout.GetDurationSeconds(effect);
                totalDuration += _landingEffectDurations[slot];
            }

            var order = BoardLandingEffectLayout.OrderResolutionSlots(
                rankingStats, _landingEffectPlan);
            Array.Copy(order, _landingEffectOrder, order.Length);

            _landingEffectPlanReady = true;
            return totalDuration;
        }

        private void BeginLandingEffectsOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            if (!_landingEffectPlanReady)
            {
                PrepareLandingEffectPlanOnServer();
            }

            _nextLandingEffectSlot = 0;
            _landingEffectAppliedMask = 0;
            _landingEffectSlotStartsAt = 0d;
            _lastLandingEffectPreviewKey = -1;
            ClearLandingEffectPresentationOnServer();
            ClearResourceTransferPresentationOnServer();
            AdvanceLandingEffectResolutionOnServer(ServerNow);
        }

        private void AdvanceLandingEffectResolutionOnServer(double now)
        {
            if (!IsServer || _flow == null ||
                _flow.State != BoardFlowState.LandingEffectResolve)
            {
                return;
            }

            if (!_landingEffectPlanReady)
            {
                PrepareLandingEffectPlanOnServer();
            }

            var elapsed = Math.Max(
                0d,
                _flow.ToFlowTime(now) - _flow.StateStartedAt);
            while (_nextLandingEffectSlot < MultiplayerConstants.MaxPlayers)
            {
                var slot = _landingEffectOrder[_nextLandingEffectSlot];
                var stageElapsed = elapsed - _landingEffectSlotStartsAt;
                if (stageElapsed < 0d)
                {
                    return;
                }

                var effect = _landingEffectPlan[slot];
                var duration = _landingEffectDurations[slot];
                var wasApplied =
                    (_landingEffectAppliedMask & (1 << slot)) != 0;
                if (effect == BoardLandingEffectType.SpecialEvent)
                {
                    AdvanceSpecialEventRouletteOnServer(slot, stageElapsed);
                }
                else
                {
                    ApplyLandingEffectForSlotOnServer(slot);
                }

                var isApplied =
                    (_landingEffectAppliedMask & (1 << slot)) != 0;
                if (!isApplied)
                {
                    // A reconnect can briefly leave a slot out of the lookup
                    // cache. Never consume or partially apply its event.
                    return;
                }

                var appliedThisFrame = !wasApplied;
                if (appliedThisFrame)
                {
                    var expectedApplyOffset =
                        effect == BoardLandingEffectType.SpecialEvent
                            ? BoardLandingEffectLayout.SpecialEventRouletteDurationSeconds
                            : 0d;
                    if (stageElapsed > expectedApplyOffset)
                    {
                        // A stalled server frame must not collapse this result
                        // into the next player's message before clients see it.
                        _landingEffectSlotStartsAt =
                            elapsed - expectedApplyOffset;
                        return;
                    }
                }

                if (stageElapsed < duration)
                {
                    return;
                }

                ClearResourceTransferPresentationOnServer();
                _landingEffectSlotStartsAt += duration;
                _nextLandingEffectSlot++;
                _lastLandingEffectPreviewKey = -1;
            }
        }

        private bool ShouldDeferLandingEffectResolutionOnServer(
            bool boardDeathInProgress)
        {
            return _flow != null &&
                   _flow.State == BoardFlowState.LandingEffectResolve &&
                   (boardDeathInProgress ||
                    _nextLandingEffectSlot < MultiplayerConstants.MaxPlayers);
        }

        private void ResolveAllRemainingLandingEffectsOnServer()
        {
            if (!IsServer)
            {
                return;
            }

            if (!_landingEffectPlanReady)
            {
                PrepareLandingEffectPlanOnServer();
            }

            while (_nextLandingEffectSlot < MultiplayerConstants.MaxPlayers)
            {
                var slot = _landingEffectOrder[_nextLandingEffectSlot];
                ApplyLandingEffectForSlotOnServer(slot);
                if ((_landingEffectAppliedMask &
                     (1 << slot)) == 0)
                {
                    return;
                }
                _nextLandingEffectSlot++;
            }
        }

        private void AdvanceSpecialEventRouletteOnServer(
            int slot,
            double stageElapsed)
        {
            var tile = _landingEffectTiles[slot];
            var resolution = _specialEventPlan[slot];
            if (stageElapsed >=
                BoardLandingEffectLayout.SpecialEventTargetDurationSeconds +
                BoardLandingEffectLayout.SpecialEventResourceDurationSeconds +
                BoardLandingEffectLayout.SpecialEventOperationDurationSeconds)
            {
                ApplyLandingEffectForSlotOnServer(slot);
                return;
            }

            var phase = stageElapsed <
                        BoardLandingEffectLayout.SpecialEventTargetDurationSeconds
                ? 0
                : stageElapsed <
                  BoardLandingEffectLayout.SpecialEventTargetDurationSeconds +
                  BoardLandingEffectLayout.SpecialEventResourceDurationSeconds
                    ? 1
                    : 2;
            var phaseStartedAt = phase == 0
                ? 0d
                : phase == 1
                    ? BoardLandingEffectLayout.SpecialEventTargetDurationSeconds
                    : BoardLandingEffectLayout.SpecialEventTargetDurationSeconds +
                      BoardLandingEffectLayout.SpecialEventResourceDurationSeconds;
            var previewTick = Math.Max(
                0,
                (int)Math.Floor(
                    (stageElapsed - phaseStartedAt) /
                    SpecialEventPreviewIntervalSeconds));
            var previewKey = phase * 10000 + previewTick;
            if (_lastLandingEffectPreviewKey == previewKey)
            {
                return;
            }

            _lastLandingEffectPreviewKey = previewKey;
            switch (phase)
            {
                case 0:
                    PublishLandingEffectOnServer(
                        slot,
                        tile,
                        BoardLandingEffectType.SpecialEvent,
                        GameText.N("SPECIAL EVENT  TARGET > {0}"),
                        GetPreviewTargetLabel(resolution, slot, previewTick));
                    break;
                case 1:
                    PublishLandingEffectOnServer(
                        slot,
                        tile,
                        BoardLandingEffectType.SpecialEvent,
                        GameText.N("SPECIAL EVENT  {0}  RESOURCE > {1}"),
                        GetSpecialEventTargetLabel(resolution, slot),
                        GetPreviewResourceLabel(previewTick));
                    break;
                default:
                    PublishLandingEffectOnServer(
                        slot,
                        tile,
                        BoardLandingEffectType.SpecialEvent,
                        GameText.N("SPECIAL EVENT  {0}  {1}  ACTION > {2}"),
                        GetSpecialEventTargetLabel(resolution, slot),
                        GetSpecialEventResourceLabel(resolution),
                        GetPreviewOperationLabel(resolution, previewTick));
                    break;
            }
        }

        private void ApplyLandingEffectForSlotOnServer(int slot)
        {
            if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers ||
                (_landingEffectAppliedMask & (1 << slot)) != 0)
            {
                return;
            }

            var avatar = GetAvatarForSlot(slot);
            var tile = _landingEffectTiles[slot];
            var effect = _landingEffectPlan[slot];
            if (avatar == null ||
                effect == BoardLandingEffectType.SpecialEvent &&
                !AreSpecialEventParticipantsAvailableOnServer(
                    slot,
                    _specialEventPlan[slot]))
            {
                return;
            }

            _landingEffectAppliedMask = (byte)(
                _landingEffectAppliedMask | (1 << slot));
            if (tile == null)
            {
                PublishLandingEffectOnServer(
                    slot,
                    tile,
                    effect,
                    GameText.N("NO EFFECT (0 change)"));
                return;
            }

            switch (effect)
            {
                case BoardLandingEffectType.GoldGain:
                case BoardLandingEffectType.GoldLoss:
                    var goldDelta = avatar.ApplyGoldDeltaOnServer(
                        BoardLandingEffectLayout.GetGoldDelta(effect));
                    PublishLandingEffectOnServer(
                        slot,
                        tile,
                        effect,
                        effect == BoardLandingEffectType.GoldGain
                            ? GameText.N("GOLD GAIN {0} GOLD")
                            : GameText.N("GOLD LOSS {0} GOLD"),
                        (goldDelta > 0 ? "+" : string.Empty) +
                        goldDelta.ToString(CultureInfo.InvariantCulture));
                    PublishLandingEffectFeedbackOnServer(
                        slot,
                        BoardLandingEffectFeedbackKind.Gold,
                        goldDelta);
                    break;
                case BoardLandingEffectType.Healing20:
                case BoardLandingEffectType.Healing10:
                    var healed = avatar.HealOnServer(
                        BoardLandingEffectLayout.GetHealthDelta(effect));
                    PublishLandingEffectOnServer(
                        slot,
                        tile,
                        effect,
                        GameText.N("HEALING +{0} HP"),
                        healed.ToString(CultureInfo.InvariantCulture));
                    PublishLandingEffectFeedbackOnServer(
                        slot,
                        BoardLandingEffectFeedbackKind.Health,
                        healed);
                    break;
                case BoardLandingEffectType.Damage40:
                case BoardLandingEffectType.Damage20:
                    var healthBefore = avatar.CurrentHealth;
                    avatar.ApplyDamage(new DamageRequest(
                        -BoardLandingEffectLayout.GetHealthDelta(effect),
                        DamageKind.Environment,
                        null));
                    var damage = Math.Max(0, healthBefore - avatar.CurrentHealth);
                    PublishLandingEffectOnServer(
                        slot,
                        tile,
                        effect,
                        GameText.N("DAMAGE -{0} HP"),
                        damage.ToString(CultureInfo.InvariantCulture));
                    PublishLandingEffectFeedbackOnServer(
                        slot,
                        BoardLandingEffectFeedbackKind.Health,
                        -damage);
                    break;
                case BoardLandingEffectType.ItemReward:
                    ResolveLandingItemRewardOnServer(slot, avatar, tile);
                    break;
                case BoardLandingEffectType.SpecialEvent:
                    ResolveSpecialEventOnServer(
                        slot,
                        tile,
                        _specialEventPlan[slot]);
                    break;
                default:
                    PublishLandingEffectOnServer(
                        slot,
                        tile,
                        effect,
                        GameText.N("NO EFFECT (0 change)"));
                    break;
            }
        }

        private void ResolveLandingItemRewardOnServer(
            int slot,
            NetworkPlayerAvatar avatar,
            BoardTile tile)
        {
            var random = new System.Random(unchecked(
                _boardEffectSeed.Value ^ (_turn.Value * 486187739) ^
                (slot * 16777619)));
            var reward = PrototypeItemCatalog.GetRandomId(random);
            if (avatar.TryAddItemOnServer(reward))
            {
                PublishLandingEffectOnServer(
                    slot,
                    tile,
                    BoardLandingEffectType.ItemReward,
                    GameText.N("ITEM ACQUIRED"));
                PublishLandingEffectFeedbackOnServer(
                    slot,
                    BoardLandingEffectFeedbackKind.Item,
                    itemId: reward);
                return;
            }

            PublishLandingEffectOnServer(
                slot,
                tile,
                BoardLandingEffectType.ItemReward,
                GameText.N("ITEM REWARD +0 (INVENTORY FULL)"));
        }

        private void ResolveSpecialEventOnServer(
            int actorSlot,
            BoardTile tile,
            BoardSpecialEventRules.Resolution resolution)
        {
            if (resolution.Family == BoardSpecialEventFamily.Transfer)
            {
                ResolveSpecialEventTransferOnServer(
                    actorSlot,
                    tile,
                    resolution);
                return;
            }

            var targetMask = BoardSpecialEventRules.GetTargetMask(
                resolution,
                actorSlot,
                MultiplayerConstants.MaxPlayers);
            var delta = resolution.Operation == BoardSpecialEventOperation.Gain
                ? resolution.Amount
                : -resolution.Amount;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if ((targetMask & (1 << slot)) == 0)
                {
                    continue;
                }

                ApplySpecialEventResourceDeltaOnServer(
                    GetAvatarForSlot(slot),
                    resolution.Resource,
                    delta);
            }

            PublishLandingEffectOnServer(
                actorSlot,
                tile,
                BoardLandingEffectType.SpecialEvent,
                resolution.Operation == BoardSpecialEventOperation.Gain
                    ? GameText.N("EVENT RESULT: {0} RECEIVES UP TO {1}")
                    : GameText.N("EVENT RESULT: {0} LOSES UP TO {1}"),
                GetSpecialEventTargetLabel(resolution, actorSlot),
                GetSpecialEventResourceLabel(resolution));
            PublishLandingEffectFeedbackOnServer(
                actorSlot,
                BoardLandingEffectFeedbackKind.Event,
                affectedMask: (byte)targetMask);
        }

        private bool AreSpecialEventParticipantsAvailableOnServer(
            int actorSlot,
            BoardSpecialEventRules.Resolution resolution)
        {
            if (resolution.Family == BoardSpecialEventFamily.Transfer)
            {
                return GetAvatarForSlot(actorSlot) != null &&
                       GetAvatarForSlot(resolution.OpponentSlot) != null;
            }

            var targetMask = BoardSpecialEventRules.GetTargetMask(
                resolution,
                actorSlot,
                MultiplayerConstants.MaxPlayers);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if ((targetMask & (1 << slot)) != 0 &&
                    GetAvatarForSlot(slot) == null)
                {
                    return false;
                }
            }

            return true;
        }

        private void ResolveSpecialEventTransferOnServer(
            int actorSlot,
            BoardTile tile,
            BoardSpecialEventRules.Resolution resolution)
        {
            var actor = GetAvatarForSlot(actorSlot);
            var opponent = GetAvatarForSlot(resolution.OpponentSlot);
            var opponentGives = resolution.Operation ==
                                BoardSpecialEventOperation.OpponentGivesToActor;
            var source = opponentGives ? opponent : actor;
            var destination = opponentGives ? actor : opponent;
            var transferred = BoardSpecialEventRules.GetTransferAmount(
                resolution.Amount,
                GetSpecialEventResourceBalance(source, resolution.Resource),
                GetSpecialEventResourceBalance(destination, resolution.Resource));
            ApplySpecialEventResourceDeltaOnServer(
                source,
                resolution.Resource,
                -transferred);
            ApplySpecialEventResourceDeltaOnServer(
                destination,
                resolution.Resource,
                transferred);

            var sourceSlot = opponentGives
                ? resolution.OpponentSlot
                : actorSlot;
            var destinationSlot = opponentGives
                ? actorSlot
                : resolution.OpponentSlot;
            if (transferred > 0)
            {
                PublishResourceTransferPresentationOnServer(
                    sourceSlot,
                    destinationSlot,
                    resolution.Resource,
                    transferred);
            }
            else
            {
                ShortenEmptyTransferPresentationOnServer(actorSlot);
            }
            PublishLandingEffectOnServer(
                actorSlot,
                tile,
                BoardLandingEffectType.SpecialEvent,
                opponentGives
                    ? GameText.N("EVENT RESULT: {0} GIVES {1} {2}")
                    : GameText.N("EVENT RESULT: {0} STEALS {2} FROM {1}"),
                LandingEffectMessage.PlayerToken(opponentGives ? sourceSlot : destinationSlot),
                LandingEffectMessage.PlayerToken(opponentGives ? destinationSlot : sourceSlot),
                LandingEffectMessage.ResourceToken(resolution.Resource, transferred));
            PublishLandingEffectFeedbackOnServer(
                actorSlot,
                BoardLandingEffectFeedbackKind.Event,
                affectedMask: (byte)((1 << sourceSlot) |
                                     (1 << destinationSlot)));
        }

        private static int GetSpecialEventResourceBalance(
            NetworkPlayerAvatar avatar,
            BoardSpecialEventResource resource)
        {
            if (avatar == null)
            {
                return 0;
            }

            return resource == BoardSpecialEventResource.Gold
                ? avatar.Gold
                : avatar.KeyCount;
        }

        private void ShortenEmptyTransferPresentationOnServer(int actorSlot)
        {
            var resultOnlyDuration =
                BoardLandingEffectLayout.SpecialEventRouletteDurationSeconds +
                BoardResourceTransferPresentationRules.ResultDurationSeconds;
            var reduction = Math.Max(
                0d,
                _landingEffectDurations[actorSlot] - resultOnlyDuration);
            _landingEffectDurations[actorSlot] = resultOnlyDuration;
            var now = ServerNow;
            if (reduction <= 0d ||
                !_flow.TryShortenLandingEffectResolve(now, reduction))
            {
                return;
            }

            SyncFlowSnapshot(now);
        }

        private static int ApplySpecialEventResourceDeltaOnServer(
            NetworkPlayerAvatar avatar,
            BoardSpecialEventResource resource,
            int delta)
        {
            if (avatar == null || delta == 0)
            {
                return 0;
            }

            return resource == BoardSpecialEventResource.Gold
                ? avatar.ApplyGoldDeltaOnServer(delta)
                : avatar.ApplyKeyDeltaOnServer(delta);
        }

        private static string GetPreviewTargetLabel(
            BoardSpecialEventRules.Resolution resolution,
            int actorSlot,
            int previewTick)
        {
            var opponentSlot = GetPreviewOpponentSlot(actorSlot, previewTick);
            if (resolution.Family == BoardSpecialEventFamily.Transfer)
            {
                return LandingEffectMessage.PlayerToken(opponentSlot) + " / " + LandingEffectMessage.PlayerToken(actorSlot);
            }

            switch (previewTick % 4)
            {
                case 0:
                    return LandingEffectMessage.PlayerToken(actorSlot);
                case 1:
                    return LandingEffectMessage.PlayerToken(opponentSlot);
                case 2:
                    return LandingEffectMessage.PlayersToken(15);
                default:
                    return LandingEffectMessage.PlayersToken(15 & ~(1 << actorSlot));
            }
        }

        private static string GetSpecialEventTargetLabel(
            BoardSpecialEventRules.Resolution resolution,
            int actorSlot)
        {
            if (resolution.Family == BoardSpecialEventFamily.Transfer)
            {
                return LandingEffectMessage.PlayerToken(resolution.OpponentSlot) +
                       " / " + LandingEffectMessage.PlayerToken(actorSlot);
            }

            switch (resolution.Audience)
            {
                case BoardSpecialEventAudience.Self:
                    return LandingEffectMessage.PlayerToken(actorSlot);
                case BoardSpecialEventAudience.OneOpponent:
                    return LandingEffectMessage.PlayerToken(resolution.OpponentSlot);
                case BoardSpecialEventAudience.Everyone:
                    return LandingEffectMessage.PlayersToken(15);
                case BoardSpecialEventAudience.EveryoneExceptSelf:
                    return LandingEffectMessage.PlayersToken(15 & ~(1 << actorSlot));
                default:
                    return LandingEffectMessage.PlayerToken(actorSlot);
            }
        }

        private static string GetPreviewResourceLabel(int previewTick)
        {
            switch (previewTick % 10)
            {
                case 0:
                case 1:
                case 2:
                    return LandingEffectMessage.ResourceToken(BoardSpecialEventResource.Gold, 30);
                case 3:
                case 4:
                case 5:
                    return LandingEffectMessage.ResourceToken(BoardSpecialEventResource.Gold, 20);
                case 6:
                case 7:
                case 8:
                    return LandingEffectMessage.ResourceToken(BoardSpecialEventResource.Gold, 10);
                default:
                    return LandingEffectMessage.ResourceToken(BoardSpecialEventResource.Key, 1);
            }
        }

        private static string GetSpecialEventResourceLabel(
            BoardSpecialEventRules.Resolution resolution)
        {
            if (resolution.Resource == BoardSpecialEventResource.Key)
            {
                return LandingEffectMessage.ResourceToken(BoardSpecialEventResource.Key, 1);
            }

            switch (resolution.Amount)
            {
                case 30:
                    return LandingEffectMessage.ResourceToken(BoardSpecialEventResource.Gold, 30);
                case 20:
                    return LandingEffectMessage.ResourceToken(BoardSpecialEventResource.Gold, 20);
                default:
                    return LandingEffectMessage.ResourceToken(BoardSpecialEventResource.Gold, 10);
            }
        }

        private static string GetPreviewOperationLabel(
            BoardSpecialEventRules.Resolution resolution,
            int previewTick)
        {
            if (resolution.Family == BoardSpecialEventFamily.Transfer)
            {
                return previewTick % 2 == 0
                    ? GameText.N("GIVE")
                    : GameText.N("STEAL");
            }

            return previewTick % 2 == 0
                ? GameText.N("RECEIVE")
                : GameText.N("LOSE");
        }

        private static int GetPreviewOpponentSlot(
            int actorSlot,
            int previewTick)
        {
            var index = previewTick % (MultiplayerConstants.MaxPlayers - 1);
            return index >= actorSlot ? index + 1 : index;
        }

        private void PublishLandingEffectFeedbackOnServer(
            int slot,
            BoardLandingEffectFeedbackKind kind,
            int signedAmount = 0,
            PrototypeItemId itemId = PrototypeItemId.None,
            byte affectedMask = 0)
        {
            var current = _landingEffectFeedback.Value;
            _landingEffectFeedback.Value =
                new BoardLandingEffectFeedbackSnapshot
                {
                    Active = true,
                    Revision = current.Revision + 1,
                    Slot = slot,
                    Kind = (byte)kind,
                    SignedAmount = signedAmount,
                    AffectedMask = affectedMask,
                    ItemId = (byte)itemId,
                    StartedAt = ServerNow
                };
        }

        private void ResetLandingEffectRuntimeOnServer(bool clearMessage)
        {
            _landingEffectPlanReady = false;
            _nextLandingEffectSlot = 0;
            _landingEffectAppliedMask = 0;
            _landingEffectSlotStartsAt = 0d;
            _lastLandingEffectPreviewKey = -1;
            Array.Clear(_landingEffectPlan, 0, _landingEffectPlan.Length);
            Array.Clear(_landingEffectTiles, 0, _landingEffectTiles.Length);
            Array.Clear(_specialEventPlan, 0, _specialEventPlan.Length);
            Array.Clear(_landingEffectDurations, 0, _landingEffectDurations.Length);
            _pausedResourceTransferPresentationRemaining = 0d;
            ClearResourceTransferPresentationOnServer();
            ClearLandingEffectFeedbackOnServer();
            if (clearMessage)
            {
                ClearLandingEffectPresentationOnServer();
            }
        }

        private void ClearLandingEffectPresentationOnServer()
        {
            if (_lastLandingEffectMessage.Value.Length == 0)
            {
                return;
            }

            _lastLandingEffectMessage.Value = default;
            _lastLandingEffectRevision.Value++;
        }

        private void ClearLandingEffectFeedbackOnServer()
        {
            _landingFeedbackPausedAt.Value = -1d;
            var current = _landingEffectFeedback.Value;
            if (!current.Active)
            {
                return;
            }

            _landingEffectFeedback.Value =
                new BoardLandingEffectFeedbackSnapshot
                {
                    Revision = current.Revision + 1,
                    Slot = -1
                };
        }

        private void PublishResourceTransferPresentationOnServer(
            int sourceSlot,
            int destinationSlot,
            BoardSpecialEventResource resource,
            int amount)
        {
            var current = _resourceTransferPresentation.Value;
            _resourceTransferPresentation.Value =
                new BoardResourceTransferSnapshot
                {
                    Active = true,
                    Revision = current.Revision + 1,
                    SourceSlot = sourceSlot,
                    DestinationSlot = destinationSlot,
                    Resource = (byte)resource,
                    Amount = Math.Max(0, amount),
                    StartedAt = ServerNow
                };
            _pausedResourceTransferPresentationRemaining = 0d;
        }

        private void ClearResourceTransferPresentationOnServer()
        {
            var current = _resourceTransferPresentation.Value;
            if (!current.Active)
            {
                return;
            }

            _resourceTransferPresentation.Value =
                new BoardResourceTransferSnapshot
                {
                    Revision = current.Revision + 1,
                    SourceSlot = -1,
                    DestinationSlot = -1
                };
            _pausedResourceTransferPresentationRemaining = 0d;
        }

        private void PauseResourceTransferPresentationOnServer(double now)
        {
            _landingFeedbackPausedAt.Value = now;
            _pausedResourceTransferPresentationRemaining =
                BoardResourceTransferPresentationRules.GetRemainingSeconds(
                    _resourceTransferPresentation.Value,
                    now);
        }

        private void ResumeResourceTransferPresentationOnServer(double now)
        {
            if (_landingFeedbackPausedAt.Value >= 0d)
            {
                var feedback = _landingEffectFeedback.Value;
                if (feedback.Active)
                {
                    feedback.StartedAt += Math.Max(0d, now - _landingFeedbackPausedAt.Value);
                    _landingEffectFeedback.Value = feedback;
                }
                _landingFeedbackPausedAt.Value = -1d;
            }

            var snapshot = _resourceTransferPresentation.Value;
            if (!snapshot.Active ||
                _pausedResourceTransferPresentationRemaining <= 0d)
            {
                _pausedResourceTransferPresentationRemaining = 0d;
                return;
            }

            var elapsed =
                BoardResourceTransferPresentationRules.TotalDurationSeconds -
                _pausedResourceTransferPresentationRemaining;
            snapshot.StartedAt = now - Math.Max(0d, elapsed);
            _resourceTransferPresentation.Value = snapshot;
            _pausedResourceTransferPresentationRemaining = 0d;
        }
    }
}
