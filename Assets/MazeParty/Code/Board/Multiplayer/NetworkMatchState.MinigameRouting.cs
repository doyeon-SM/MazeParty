using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.BalloonBlow;
using MazeParty.Gameplay.Minigames.GiftGrab;
using MazeParty.Gameplay.Minigames.Minefield;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Gameplay.Minigames.RedLightGreenLight;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Gameplay.Minigames.StableFooting;
using MazeParty.Gameplay.Minigames.TagChase;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using MazeParty.Gameplay.Minigames.WrongWay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private static bool TryGetMinigameRuntime(
            ScheduledMinigameId minigameId,
            out IMinigameRuntimeAdapter runtime)
        {
            return MinigameRuntimeRegistry.TryGet(minigameId, out runtime);
        }

        private bool TryGetCurrentMinigameRuntime(
            out IMinigameRuntimeAdapter runtime)
        {
            return TryGetMinigameRuntime(CurrentMinigame, out runtime);
        }

        private bool TryGetCurrentMinigameCapability<TCapability>(
            out TCapability capability)
            where TCapability : class
        {
            if (TryGetCurrentMinigameRuntime(out var runtime) &&
                runtime is TCapability supportedCapability)
            {
                capability = supportedCapability;
                return true;
            }

            capability = null;
            return false;
        }

        public bool CanCurrentMinigameAcceptInputForSlot(int slot)
        {
            return TryGetCurrentMinigameRuntime(out var runtime) &&
                   runtime != null &&
                   runtime.CanAcceptInputForSlot(slot);
        }

        public bool CurrentMinigameUsesFirstPersonForSlot(int slot)
        {
            return TryGetCurrentMinigameCapability<
                       IMinigameFirstPersonCapability>(out var capability) &&
                   capability.UsesFirstPersonControlsForSlot(slot);
        }


        public bool TryGetCurrentMinigameRoundAndInputEpoch(
            out byte roundNumber,
            out uint inputEpoch)
        {
            roundNumber = 0;
            inputEpoch = 0U;
            if (!TryGetCurrentMinigameCapability<
                    IMinigameRoundEpochCapability>(out var capability))
            {
                return false;
            }

            return capability.TryGetRoundAndInputEpoch(
                out roundNumber,
                out inputEpoch);
        }



        private static void PauseAllMinigameRuntimes(double now)
        {
            MinigameRuntimeRegistry.PauseAll(now);
        }

        private static void ResumeAllMinigameRuntimes(double now)
        {
            MinigameRuntimeRegistry.ResumeAll(now);
        }

        private static void RestoreAllMinigameRuntimes(
            NetworkPlayerAvatar avatar)
        {
            MinigameRuntimeRegistry.RestoreAll(avatar);
        }

        private static void EndAllMinigameRuntimes()
        {
            MinigameRuntimeRegistry.EndAll();
        }

        public void RouteMovementInputOnCurrentMinigameOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            byte roundNumber = 0,
            uint inputEpoch = 0U)
        {
            if (TryGetCurrentMinigameCapability<
                    IMinigameMovementInputCapability>(out var capability))
            {
                capability.ReceiveMovementInputOnServer(
                    avatar,
                    Vector2.ClampMagnitude(input, 1f),
                    roundNumber,
                    inputEpoch);
            }
        }

        public void RouteLookInputOnCurrentMinigameOnServer(
            NetworkPlayerAvatar avatar,
            float yaw,
            byte roundNumber,
            uint inputEpoch)
        {
            if (TryGetCurrentMinigameCapability<
                    IMinigameLookInputCapability>(out var capability))
            {
                capability.ReceiveLookInputOnServer(
                    avatar,
                    yaw,
                    roundNumber,
                    inputEpoch);
            }
        }


        public void RoutePushInputOnCurrentMinigameOnServer(NetworkPlayerAvatar avatar)
        {
            if (TryGetCurrentMinigameCapability<
                    IMinigamePushInputCapability>(out var capability))
            {
                capability.RequestPushOnServer(avatar);
            }
        }

        public void RouteInflateHeldOnCurrentMinigameOnServer(
            NetworkPlayerAvatar avatar,
            bool isHeld,
            byte roundNumber,
            uint inputEpoch)
        {
            if (TryGetCurrentMinigameCapability<
                    IMinigameInflateInputCapability>(out var capability))
            {
                capability.SetInflateHeldOnServer(
                    avatar,
                    isHeld,
                    roundNumber,
                    inputEpoch);
            }
        }

        public void RoutePrimaryActionOnCurrentMinigameOnServer(
            NetworkPlayerAvatar avatar,
            byte roundNumber,
            uint inputEpoch)
        {
            if (TryGetCurrentMinigameCapability<
                    IMinigamePrimaryActionCapability>(out var capability))
            {
                capability.RequestPrimaryActionOnServer(
                    avatar,
                    roundNumber,
                    inputEpoch);
            }
        }

        public void RouteSonarInputOnCurrentMinigameOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input)
        {
            if (TryGetCurrentMinigameCapability<
                    IMinigameSonarInputCapability>(out var capability))
            {
                capability.TrySonarOnServer(
                    avatar,
                    Vector2.ClampMagnitude(input, 1f));
            }
        }

        public void RouteWrongWayDirectionOnCurrentMinigameOnServer(
            NetworkPlayerAvatar avatar,
            WrongWayDirection direction)
        {
            if (TryGetCurrentMinigameCapability<
                    IWrongWayDirectionInputCapability>(out var capability))
            {
                capability.TrySubmitDirectionOnServer(avatar, direction);
            }
        }

        public void RouteRaceStepOnCurrentMinigameOnServer(
            NetworkPlayerAvatar avatar,
            RaceStepInput input,
            byte roundNumber,
            uint inputEpoch)
        {
            if (TryGetCurrentMinigameCapability<
                    IRaceStepInputCapability>(out var capability))
            {
                capability.TrySubmitRaceStepOnServer(
                    avatar,
                    input,
                    roundNumber,
                    inputEpoch);
            }
        }

        public void RouteSequenceMemoryInputOnCurrentMinigameOnServer(
            NetworkPlayerAvatar avatar,
            SequenceMemoryInput input,
            byte roundNumber,
            uint inputEpoch)
        {
            if (TryGetCurrentMinigameCapability<
                    ISequenceMemoryInputCapability>(out var capability))
            {
                capability.TrySubmitSequenceMemoryInputOnServer(
                    avatar,
                    input,
                    roundNumber,
                    inputEpoch);
            }
        }

        public void RouteBouncingShieldAxisOnCurrentMinigameOnServer(
            NetworkPlayerAvatar avatar,
            float axis,
            byte roundNumber,
            uint inputEpoch)
        {
            if (TryGetCurrentMinigameCapability<
                    IBouncingShieldInputCapability>(out var capability))
            {
                capability.SetBouncingShieldAxisOnServer(
                    avatar,
                    axis,
                    roundNumber,
                    inputEpoch);
            }
        }

        public bool TrySetMinigameReadyOnServer(NetworkPlayerAvatar avatar)
        {
            if (!CanProcessAvatarRequest(avatar) ||
                FlowState != BoardFlowState.MinigameIntroReady ||
                CurrentMinigame == ScheduledMinigameId.Skip)
            {
                return false;
            }

            _readyMask.Value = (byte)(_readyMask.Value | (1 << avatar.AssignedSlot));
            if ((_readyMask.Value & AllPlayersMask) == AllPlayersMask)
            {
                _flow.TryBeginMinigameLoading(ServerNow);
            }

            return true;
        }

        public bool TryCompleteMinefieldOnServer(
            IReadOnlyList<MinefieldLeaderboardEntry> leaderboard)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.Minefield,
                ProjectPlacements(
                    leaderboard,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteWrongWayOnServer(
            IReadOnlyList<WrongWayLeaderboardEntry> leaderboard)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.WrongWay,
                ProjectPlacements(
                    leaderboard,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteRedLightGreenLightOnServer(
            IReadOnlyList<RedLightGreenLightLeaderboardEntry> leaderboard)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.RedLightGreenLight,
                ProjectPlacements(
                    leaderboard,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteStableFootingOnServer(
            IReadOnlyList<StableFootingLeaderboardEntry> leaderboard)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.StableFooting,
                ProjectPlacements(
                    leaderboard,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteBalloonBlowOnServer(
            IReadOnlyList<BalloonBlowLeaderboardEntry> leaderboard)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.BalloonBlow,
                ProjectPlacements(
                    leaderboard,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteGiftGrabOnServer(
            IReadOnlyList<GiftGrabLeaderboardEntry> leaderboard)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.GiftGrab,
                ProjectPlacements(
                    leaderboard,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteTerritoryPaintOnServer(
            IReadOnlyList<TerritoryPaintLeaderboardEntry> leaderboard)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.TerritoryPaint,
                ProjectPlacements(
                    leaderboard,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteTagChaseOnServer(
            IReadOnlyList<TagChaseLeaderboardEntry> leaderboard)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.TagChase,
                ProjectPlacements(
                    leaderboard,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteRaceOnServer(
            IReadOnlyList<RaceLeaderboardEntry> leaderboard)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.Race,
                ProjectPlacements(
                    leaderboard,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteSequenceMemoryOnServer(
            IReadOnlyList<SequenceMemoryStanding> standings)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.SequenceMemory,
                ProjectPlacements(
                    standings,
                    entry => new PlayerPlacement(
                        entry.PlayerSlot,
                        entry.Rank)));
        }

        public bool TryCompleteBouncingBallsOnServer(
            IReadOnlyList<int> ranks)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.BouncingBalls,
                ProjectRanksBySlot(ranks));
        }

        public bool TryCompleteBombPassingOnServer(
            IReadOnlyList<int> ranks)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.BombPassing,
                ProjectRanksBySlot(ranks));
        }

        public bool TryCompleteSnowySpinOnServer(
            IReadOnlyList<int> ranks)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.SnowySpin,
                ProjectRanksBySlot(ranks));
        }

        public bool TryCompleteArenaCombatOnServer(
            IReadOnlyList<int> ranks)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.ArenaCombat,
                ProjectRanksBySlot(ranks));
        }

        public bool TryCompleteCliffBarrageOnServer(
            IReadOnlyList<int> ranks)
        {
            return TryCompleteMinigameOnServer(
                ScheduledMinigameId.CliffBarrage,
                ProjectRanksBySlot(ranks));
        }

        internal bool TryCompleteMinigameOnServer(
            ScheduledMinigameId expectedMinigame,
            IReadOnlyList<PlayerPlacement> placements)
        {
            if (!IsServer ||
                FlowState != BoardFlowState.MinigamePlaying ||
                CurrentMinigame != expectedMinigame ||
                _settledMinigameTurn == Turn ||
                !PlayerPlacementSet.TryCreate(
                    placements,
                    out var placementSet,
                    out _))
            {
                return false;
            }

            var rewardAvatars =
                new NetworkPlayerAvatar[placementSet.Count];
            for (var slot = 0; slot < placementSet.Count; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar == null || !avatar.IsSpawned)
                {
                    return false;
                }

                rewardAvatars[slot] = avatar;
            }

            if (!_flow.TryCompleteMinigame(ServerNow))
            {
                return false;
            }

            _settledMinigameTurn = Turn;
            for (var slot = 0; slot < placementSet.Count; slot++)
            {
                SettleMinigamePlacementOnServer(
                    rewardAvatars[slot],
                    placementSet.GetRankForSlot(slot));
            }

            return true;
        }

        private static IReadOnlyList<PlayerPlacement> ProjectPlacements<TEntry>(
            IReadOnlyList<TEntry> entries,
            Func<TEntry, PlayerPlacement> selector)
        {
            if (entries == null)
            {
                return null;
            }

            var placements = new PlayerPlacement[entries.Count];
            for (var index = 0; index < entries.Count; index++)
            {
                placements[index] = selector(entries[index]);
            }

            return placements;
        }

        private static IReadOnlyList<PlayerPlacement> ProjectRanksBySlot(
            IReadOnlyList<int> ranks)
        {
            if (ranks == null)
            {
                return null;
            }

            var placements = new PlayerPlacement[ranks.Count];
            for (var slot = 0; slot < ranks.Count; slot++)
            {
                placements[slot] = new PlayerPlacement(slot, ranks[slot]);
            }

            return placements;
        }

        private static void SettleMinigamePlacementOnServer(
            NetworkPlayerAvatar avatar,
            int rank)
        {
            avatar.ApplyGoldDeltaOnServer(
                MinigameRewardRules.GetFinalPlacementGold(rank));
            avatar.RecordMinigamePlacementOnServer(rank);
        }
    }
}
