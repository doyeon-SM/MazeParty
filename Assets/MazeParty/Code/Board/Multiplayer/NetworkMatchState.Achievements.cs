using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private string _achievementMatchId;
        private readonly AchievementMatchProgress[] _achievementPlayers = new AchievementMatchProgress[4];
        private string[] _achievementReceipts;
        private double _achievementResendAt;
        private static bool AchievementBuildEligible
        {
            get
            {
#if (MAZEPARTY_STOVE || MAZEPARTY_STEAM) && !UNITY_EDITOR && !DEVELOPMENT_BUILD
                return true;
#else
                return false;
#endif
            }
        }
        private void BeginAchievementsOnServer()
        {
            _achievementMatchId = null; _achievementReceipts = null;
            var controller = OnlineSessionController.Instance;
            if (!IsServer || !IsSpawned || !AchievementBuildEligible || controller == null ||
                controller.CurrentSession == null || controller.CurrentSession.Players.Count != 4 ||
                !PartyAchievements.EligibleTurns(GameSettings.Turns)) return;
            for (var slot = 0; slot < 4; slot++)
            {
                if (GetAvatarForSlot(slot) == null || !controller.TryGetMatchPlayerIdForSlot(slot, out _)) return;
                _achievementPlayers[slot] = new AchievementMatchProgress();
            }
            _achievementMatchId = Guid.NewGuid().ToString("N");
        }
        private AchievementMatchProgress AchievementFor(int slot)
        {
            return IsServer && GameplayEnabled && _activeMatchVoidGate.AllowsResultMutation &&
                _achievementReceipts == null && !string.IsNullOrEmpty(_achievementMatchId) && slot >= 0 && slot < 4
                ? _achievementPlayers[slot] : null;
        }
        public void RecordAchievementOnServer(int slot, PartyAchievement id) => AchievementFor(slot)?.Earn(id);
        public void RecordMinigameAchievementOnServer(int slot, ScheduledMinigameId game, PartyAchievement id)
        {
            if (CurrentMinigame == game && FlowState == BoardFlowState.MinigamePlaying)
                RecordAchievementOnServer(slot, id);
        }
        public void RecordStolenGiftsOnServer(int slot, int count)
        {
            if (CurrentMinigame != ScheduledMinigameId.GiftGrab || FlowState != BoardFlowState.MinigamePlaying) return;
            var progress = AchievementFor(slot);
            if (progress != null && count > 0) progress.stolenGifts = Math.Min(10, progress.stolenGifts + Math.Min(10, count));
        }
        public void RecordItemDeathOnServer(NetworkPlayerAvatar source, NetworkPlayerAvatar victim, PrototypeItemId item)
        {
            if (source == null || victim == null || GetAvatarForSlot(source.AssignedSlot) != source ||
                GetAvatarForSlot(victim.AssignedSlot) != victim) return;
            AchievementFor(source.AssignedSlot)?.ItemDeath(item, source == victim);
        }
        private void RecordMinigameAchievements(PlayerPlacementSet placements)
        {
            for (var slot = 0; slot < 4; slot++)
                AchievementFor(slot)?.CompleteMinigame(Turn, CurrentMinigame, placements.GetRankForSlot(slot),
                    NetworkSequenceMemoryState.Instance != null && NetworkSequenceMemoryState.Instance.GetMistakeCount(slot) == 0);
        }
        private void RestoreAchievements(MatchRecoverySnapshot snapshot, MatchRecoveryPlayerSnapshot[] players)
        {
            _achievementMatchId = null; _achievementReceipts = null;
            // Legacy journals have no achievement history, so cannot grant partial matches.
            if (!AchievementBuildEligible || !Guid.TryParseExact(snapshot.achievementMatchId, "N", out _)) return;
            for (var slot = 0; slot < 4; slot++)
            {
                if (!AchievementMatchProgress.TryDecode(players[slot].achievementProgress, out var progress)) return;
                _achievementPlayers[slot] = progress;
            }
            _achievementMatchId = snapshot.achievementMatchId;
        }
        private void FinalizeAchievementsOnServer()
        {
            if (!IsServer || _achievementReceipts != null || string.IsNullOrEmpty(_achievementMatchId) ||
                !_activeMatchVoidGate.AllowsResultMutation || !GameplayEnabled) return;
            var controller = OnlineSessionController.Instance;
            if (controller == null) return;
            var receipts = new string[4];
            for (var slot = 0; slot < 4; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar == null || !controller.TryGetMatchPlayerIdForSlot(slot, out var playerId)) return;
                var bonus = false;
                for (var award = 0; award < 2; award++)
                    bonus |= GetCeremonyAwardWinningValue(award) > 0 && (GetCeremonyAwardWinnerMask(award) & (1 << slot)) != 0;
                var receipt = new AchievementReceipt
                {
                    matchId = _achievementMatchId, playerKey = MatchRecoveryFingerprint.CreatePlayerKey(playerId),
                    mapId = CurrentBoardMapSelection.MapId, turns = GameSettings.Turns,
                    rank = GetFinalCeremonyRank(slot), finalKeys = avatar.KeyCount, bonusWinner = bonus,
                    progress = _achievementPlayers[slot]
                };
                if (!receipt.IsValid) return;
                receipts[slot] = JsonUtility.ToJson(receipt);
            }
            _achievementReceipts = receipts;
            _achievementResendAt = 0;
            TickAchievementDelivery();
        }
        private void TickAchievementDelivery()
        {
            if (!IsServer || _achievementReceipts == null || ServerNow < _achievementResendAt) return;
            _achievementResendAt = ServerNow + 5d;
            foreach (var receipt in _achievementReceipts) ReceiveAchievementReceiptRpc(receipt);
        }
        [Rpc(SendTo.ClientsAndHost, RequireOwnership = true, Delivery = RpcDelivery.Reliable)]
        private void ReceiveAchievementReceiptRpc(string json, RpcParams rpcParams = default)
        {
            if (rpcParams.Receive.SenderClientId != Unity.Netcode.NetworkManager.ServerClientId || !AchievementBuildEligible) return;
            try { OnlineSessionController.Instance?.CommitAchievements(JsonUtility.FromJson<AchievementReceipt>(json)); }
            catch (Exception) { Debug.LogWarning("Invalid achievement result received."); }
        }
    }
}
