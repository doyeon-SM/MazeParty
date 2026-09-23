using System;
using MazeParty.Gameplay;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        public const double BonusAwardPresentationSeconds =
            AwardCeremonyFlowRules.BonusAwardPresentationSeconds;
        public const double FinalPodiumInputLockSeconds =
            AwardCeremonyFlowRules.FinalPodiumInputLockSeconds;

        private readonly NetworkVariable<byte> _awardCeremonyPhase =
            new NetworkVariable<byte>((byte)AwardCeremonyPhase.None);
        private readonly NetworkVariable<double> _awardCeremonyPhaseEndsAt =
            new NetworkVariable<double>();
        private readonly NetworkVariable<double> _pausedAwardCeremonyRemaining =
            new NetworkVariable<double>();
        private readonly NetworkVariable<byte> _awardCeremonyCategory0 =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _awardCeremonyCategory1 =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _awardCeremonyWinnerMask0 =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _awardCeremonyWinnerMask1 =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<int> _awardCeremonyWinningValue0 =
            new NetworkVariable<int>();
        private readonly NetworkVariable<int> _awardCeremonyWinningValue1 =
            new NetworkVariable<int>();
        private readonly NetworkVariable<byte> _awardCeremonyFinalRank0 =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _awardCeremonyFinalRank1 =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _awardCeremonyFinalRank2 =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _awardCeremonyFinalRank3 =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<byte> _awardCeremonyReturnReadyMask =
            new NetworkVariable<byte>();
        private readonly NetworkVariable<int> _awardCeremonyRevision =
            new NetworkVariable<int>();

        private bool _completedMatchReturnQueued;

        public AwardCeremonyPhase CeremonyPhase =>
            (AwardCeremonyPhase)_awardCeremonyPhase.Value;
        public bool IsAwardCeremonyActive =>
            GameplayEnabled &&
            FlowState == BoardFlowState.MatchComplete &&
            CeremonyPhase != AwardCeremonyPhase.None;
        public int CeremonyRevision => _awardCeremonyRevision.Value;
        public double CeremonyPhaseRemaining =>
            _reconnectPaused.Value
                ? Math.Max(0d, _pausedAwardCeremonyRemaining.Value)
                : Math.Max(0d, _awardCeremonyPhaseEndsAt.Value - ServerNow);
        public int CeremonyReturnReadyCount =>
            CountSetSlots(_awardCeremonyReturnReadyMask.Value);
        public bool CanSubmitCeremonyReturn =>
            IsAwardCeremonyActive &&
            AwardCeremonyFlowRules.CanSubmitReturn(
                CeremonyPhase,
                IsReconnectPaused,
                _completedMatchReturnQueued);

        public MatchAwardCategory GetCeremonyAwardCategory(int awardIndex)
        {
            switch (awardIndex)
            {
                case 0:
                    return (MatchAwardCategory)_awardCeremonyCategory0.Value;
                case 1:
                    return (MatchAwardCategory)_awardCeremonyCategory1.Value;
                default:
                    throw new ArgumentOutOfRangeException(nameof(awardIndex));
            }
        }

        public byte GetCeremonyAwardWinnerMask(int awardIndex)
        {
            switch (awardIndex)
            {
                case 0:
                    return _awardCeremonyWinnerMask0.Value;
                case 1:
                    return _awardCeremonyWinnerMask1.Value;
                default:
                    throw new ArgumentOutOfRangeException(nameof(awardIndex));
            }
        }

        public int GetCeremonyAwardWinningValue(int awardIndex)
        {
            switch (awardIndex)
            {
                case 0:
                    return _awardCeremonyWinningValue0.Value;
                case 1:
                    return _awardCeremonyWinningValue1.Value;
                default:
                    throw new ArgumentOutOfRangeException(nameof(awardIndex));
            }
        }

        public int GetFinalCeremonyRank(int slot)
        {
            switch (slot)
            {
                case 0: return _awardCeremonyFinalRank0.Value;
                case 1: return _awardCeremonyFinalRank1.Value;
                case 2: return _awardCeremonyFinalRank2.Value;
                case 3: return _awardCeremonyFinalRank3.Value;
                default: return 0;
            }
        }

        public bool IsCeremonyReturnReady(int slot)
        {
            return slot >= 0 &&
                   slot < MultiplayerConstants.MaxPlayers &&
                   (_awardCeremonyReturnReadyMask.Value & (1 << slot)) != 0;
        }

        private void BeginAwardCeremonyOnServer(double now)
        {
            if (!IsServer || CeremonyPhase != AwardCeremonyPhase.None)
            {
                return;
            }

            var stats = new MatchAwardStats[MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < stats.Length; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                stats[slot] = avatar != null
                    ? avatar.CreateMatchAwardStatsOnServer()
                    : default;
            }

            var seed = unchecked(
                (int)(_currentMinigameSeed.Value ^
                      (_currentMinigameSeed.Value >> 32)) ^
                _boardEffectSeed.Value ^
                (Turn * 486187739));
            if (!MatchAwardRules.TrySelectTwoDistinctCategories(
                    stats,
                    seed,
                    out var firstCategory,
                    out var secondCategory) ||
                !MatchAwardRules.TryGetWinnerMask(
                    stats,
                    firstCategory,
                    out var firstWinnerMask,
                    out var firstWinningValue) ||
                !MatchAwardRules.TryGetWinnerMask(
                    stats,
                    secondCategory,
                    out var secondWinnerMask,
                    out var secondWinningValue))
            {
                throw new InvalidOperationException(
                    "A four-player completed match must produce two bonus awards.");
            }

            _awardCeremonyCategory0.Value = (byte)firstCategory;
            _awardCeremonyCategory1.Value = (byte)secondCategory;
            _awardCeremonyWinnerMask0.Value = firstWinnerMask;
            _awardCeremonyWinnerMask1.Value = secondWinnerMask;
            _awardCeremonyWinningValue0.Value = firstWinningValue;
            _awardCeremonyWinningValue1.Value = secondWinningValue;
            _awardCeremonyReturnReadyMask.Value = 0;
            _completedMatchReturnQueued = false;
            SetFinalCeremonyRanks(null);

            GrantCeremonyKeyAwardOnServer(firstWinnerMask);
            SetCeremonyPhaseOnServer(
                AwardCeremonyPhase.BonusAwardOne,
                now + BonusAwardPresentationSeconds);
        }

        private void AdvanceAwardCeremonyOnServer(double now)
        {
            if (!IsServer || !IsAwardCeremonyActive || IsReconnectPaused)
            {
                return;
            }

            if (CeremonyPhase == AwardCeremonyPhase.AwaitingReturn)
            {
                TryBeginCompletedMatchReturnOnServer();
                return;
            }

            if (!AwardCeremonyFlowRules.TryGetTimedTransition(
                    CeremonyPhase,
                    _awardCeremonyPhaseEndsAt.Value,
                    now,
                    out var nextPhase,
                    out var action))
            {
                return;
            }

            switch (action)
            {
                case AwardCeremonyServerAction.GrantSecondAward:
                    GrantCeremonyKeyAwardOnServer(
                        _awardCeremonyWinnerMask1.Value);
                    break;
                case AwardCeremonyServerAction.CalculateFinalRanks:
                    CalculateFinalCeremonyRanksOnServer();
                    break;
            }

            SetCeremonyPhaseOnServer(
                nextPhase,
                nextPhase == AwardCeremonyPhase.AwaitingReturn
                    ? 0d
                    : now + AwardCeremonyFlowRules.GetPhaseDuration(nextPhase));
        }

        private void GrantCeremonyKeyAwardOnServer(byte winnerMask)
        {
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if ((winnerMask & (1 << slot)) == 0)
                {
                    continue;
                }

                GetAvatarForSlot(slot)?.AddKeyAwardOnServer(1);
            }
        }

        private void CalculateFinalCeremonyRanksOnServer()
        {
            var rankingStats = new PlayerRankingStats[
                MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < rankingStats.Length; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                rankingStats[slot] = avatar != null
                    ? new PlayerRankingStats(
                        avatar.KeyCount,
                        avatar.Gold,
                        avatar.MinigameWins)
                    : default;
            }

            SetFinalCeremonyRanks(PlayerRankingRules.Calculate(rankingStats));
        }

        private void SetFinalCeremonyRanks(int[] ranks)
        {
            _awardCeremonyFinalRank0.Value = RankAt(ranks, 0);
            _awardCeremonyFinalRank1.Value = RankAt(ranks, 1);
            _awardCeremonyFinalRank2.Value = RankAt(ranks, 2);
            _awardCeremonyFinalRank3.Value = RankAt(ranks, 3);
        }

        private static byte RankAt(int[] ranks, int slot)
        {
            return ranks != null && slot >= 0 && slot < ranks.Length
                ? (byte)Math.Max(0, Math.Min(byte.MaxValue, ranks[slot]))
                : (byte)0;
        }

        private void SetCeremonyPhaseOnServer(
            AwardCeremonyPhase phase,
            double endsAt)
        {
            _awardCeremonyPhase.Value = (byte)phase;
            _awardCeremonyPhaseEndsAt.Value = endsAt;
            _pausedAwardCeremonyRemaining.Value = 0d;
            _awardCeremonyRevision.Value++;
        }

        private void PauseAwardCeremonyOnServer(double now)
        {
            if (CeremonyPhase == AwardCeremonyPhase.None ||
                CeremonyPhase == AwardCeremonyPhase.AwaitingReturn)
            {
                return;
            }

            _pausedAwardCeremonyRemaining.Value =
                AwardCeremonyFlowRules.GetPauseRemaining(
                    CeremonyPhase,
                    _awardCeremonyPhaseEndsAt.Value,
                    now);
            _awardCeremonyPhaseEndsAt.Value = 0d;
            _awardCeremonyRevision.Value++;
        }

        private void ResumeAwardCeremonyOnServer(double now)
        {
            if (CeremonyPhase == AwardCeremonyPhase.None ||
                CeremonyPhase == AwardCeremonyPhase.AwaitingReturn)
            {
                return;
            }

            _awardCeremonyPhaseEndsAt.Value =
                AwardCeremonyFlowRules.GetResumedEndsAt(
                    CeremonyPhase,
                    now,
                    _pausedAwardCeremonyRemaining.Value);
            _pausedAwardCeremonyRemaining.Value = 0d;
            _awardCeremonyRevision.Value++;
        }

        public void TrySetCeremonyReturnReadyOnServer(
            NetworkPlayerAvatar avatar)
        {
            if (!IsServer || !CanSubmitCeremonyReturn || avatar == null)
            {
                return;
            }

            var slot = avatar.AssignedSlot;
            if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers ||
                GetAvatarForSlot(slot) != avatar)
            {
                return;
            }

            var bit = (byte)(1 << slot);
            if ((_awardCeremonyReturnReadyMask.Value & bit) != 0)
            {
                return;
            }

            _awardCeremonyReturnReadyMask.Value = (byte)(
                _awardCeremonyReturnReadyMask.Value | bit);
            _awardCeremonyRevision.Value++;
            if ((_awardCeremonyReturnReadyMask.Value & AllPlayersMask) !=
                AllPlayersMask)
            {
                return;
            }

            TryBeginCompletedMatchReturnOnServer();
        }

        private void TryBeginCompletedMatchReturnOnServer()
        {
            if (!AwardCeremonyFlowRules.ShouldBeginLobbyReturn(
                    CeremonyPhase,
                    _awardCeremonyReturnReadyMask.Value,
                    AllPlayersMask,
                    _completedMatchReturnQueued))
            {
                return;
            }

            var controller = OnlineSessionController.Instance;
            if (controller != null &&
                controller.BeginCompletedMatchLobbyReturnOnServer())
            {
                _completedMatchReturnQueued = true;
                _awardCeremonyRevision.Value++;
            }
        }

        private static int CountSetSlots(byte mask)
        {
            var count = 0;
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if ((mask & (1 << slot)) != 0)
                {
                    count++;
                }
            }
            return count;
        }
    }
}
