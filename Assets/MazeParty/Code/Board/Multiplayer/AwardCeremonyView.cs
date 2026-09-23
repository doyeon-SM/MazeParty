using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Drives the authored award Canvas from replicated ceremony state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AwardCeremonyView : MonoBehaviour
    {
        private const string OverlayVisibleState = "Visible";
        private const string OverlaySlideUpState = "SlideUp";

        [SerializeField] private AwardCeremonyCanvasBindings bindings;

        private NetworkPlayerAvatar _localAvatar;
        private AwardCeremonyPhase _observedPhase = AwardCeremonyPhase.None;
        private int _observedRevision = -1;
        private bool _wired;

        public AwardCeremonyCanvasBindings Bindings => bindings;

        public void Configure(AwardCeremonyCanvasBindings value)
        {
            bindings = value;
            WireButton();
        }

        private void Awake()
        {
            if (bindings == null)
            {
                bindings = GetComponent<AwardCeremonyCanvasBindings>();
            }
            WireButton();
            SetCanvasVisible(false);
        }

        private void OnEnable()
        {
            WireButton();
        }

        private void OnDisable()
        {
            UnwireButton();
        }

        private void Update()
        {
            var match = NetworkMatchState.Instance;
            var active = match != null &&
                         match.IsSpawned &&
                         match.IsAwardCeremonyActive;
            SetCanvasVisible(active);
            if (!active || bindings == null ||
                !bindings.HasRequiredReferences)
            {
                _observedPhase = AwardCeremonyPhase.None;
                _observedRevision = -1;
                return;
            }

            ResolveLocalAvatar();
            if (_observedPhase != match.CeremonyPhase)
            {
                EnterPhase(match.CeremonyPhase);
                _observedPhase = match.CeremonyPhase;
            }

            if (_observedRevision != match.CeremonyRevision)
            {
                _observedRevision = match.CeremonyRevision;
                RefreshStaticCopy(match);
            }

            RefreshCountdownAndInteraction(match);
        }

        private void EnterPhase(AwardCeremonyPhase phase)
        {
            var bonus = phase == AwardCeremonyPhase.BonusAwardOne ||
                        phase == AwardCeremonyPhase.BonusAwardTwo;
            var final = phase == AwardCeremonyPhase.FinalPodiumLocked ||
                        phase == AwardCeremonyPhase.AwaitingReturn;

            bindings.BonusAwardOverlay.SetActive(bonus ||
                                                  phase == AwardCeremonyPhase.FinalPodiumLocked);
            bindings.FinalRankingPanel.SetActive(final);
            if (bonus)
            {
                bindings.BonusAwardAnimator.Play(
                    OverlayVisibleState,
                    0,
                    0f);
            }
            else if (phase == AwardCeremonyPhase.FinalPodiumLocked)
            {
                bindings.BonusAwardAnimator.Play(
                    OverlaySlideUpState,
                    0,
                    0f);
            }
            else if (phase == AwardCeremonyPhase.AwaitingReturn)
            {
                bindings.BonusAwardOverlay.SetActive(false);
            }
        }

        private void RefreshStaticCopy(NetworkMatchState match)
        {
            switch (match.CeremonyPhase)
            {
                case AwardCeremonyPhase.BonusAwardOne:
                    RefreshAward(match, 0);
                    break;
                case AwardCeremonyPhase.BonusAwardTwo:
                    RefreshAward(match, 1);
                    break;
                case AwardCeremonyPhase.FinalPodiumLocked:
                case AwardCeremonyPhase.AwaitingReturn:
                    RefreshFinalRanks(match);
                    break;
            }
        }

        private void RefreshAward(NetworkMatchState match, int awardIndex)
        {
            var category = match.GetCeremonyAwardCategory(awardIndex);
            bindings.AwardStepText.text =
                "BONUS KEY AWARD " + (awardIndex + 1) + " / 2";
            bindings.AwardCategoryText.text =
                MatchAwardRules.GetDisplayName(category);
            bindings.AwardValueText.text =
                FormatWinningValue(
                    category,
                    match.GetCeremonyAwardWinningValue(awardIndex));
            bindings.AwardWinnerText.text = BuildWinnerNames(
                match,
                match.GetCeremonyAwardWinnerMask(awardIndex));
            bindings.AwardRewardText.text = "+1 KEY EACH";
        }

        private void RefreshFinalRanks(NetworkMatchState match)
        {
            bindings.FinalTitleText.text = "FINAL RANKING";
            var slots = new List<int>(MultiplayerConstants.MaxPlayers);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                slots.Add(slot);
            }
            slots.Sort((left, right) =>
            {
                var rank = match.GetFinalCeremonyRank(left).CompareTo(
                    match.GetFinalCeremonyRank(right));
                return rank != 0 ? rank : left.CompareTo(right);
            });

            for (var row = 0; row < bindings.FinalRankTexts.Length; row++)
            {
                var slot = slots[row];
                var avatar = match.GetAvatarForSlot(slot);
                var name = avatar != null &&
                           !string.IsNullOrWhiteSpace(avatar.DisplayName)
                    ? avatar.DisplayName
                    : "Player " + (slot + 1);
                var rank = match.GetFinalCeremonyRank(slot);
                var keys = avatar != null ? avatar.KeyCount : 0;
                var gold = avatar != null ? avatar.Gold : 0;
                var wins = avatar != null ? avatar.MinigameWins : 0;
                bindings.FinalRankTexts[row].text =
                    "#" + rank + "  " + name + "     " +
                    keys + " KEY  /  " + gold + " GOLD  /  " +
                    wins + " WIN" + (wins == 1 ? string.Empty : "S");
            }
        }

        private void RefreshCountdownAndInteraction(NetworkMatchState match)
        {
            if (match.IsReconnectPaused)
            {
                bindings.LeaveRoomButton.interactable = false;
                if (match.CeremonyPhase == AwardCeremonyPhase.BonusAwardOne ||
                    match.CeremonyPhase == AwardCeremonyPhase.BonusAwardTwo)
                {
                    bindings.AwardRewardText.text =
                        "CEREMONY PAUSED  -  WAITING FOR PLAYER";
                }
                else
                {
                    bindings.InputLockText.text = "CEREMONY PAUSED";
                    bindings.ReturnStatusText.text =
                        "Waiting for the disconnected player to return.";
                }
                return;
            }

            if (match.CeremonyPhase == AwardCeremonyPhase.FinalPodiumLocked)
            {
                var seconds = Math.Max(
                    0,
                    (int)Math.Ceiling(match.CeremonyPhaseRemaining));
                bindings.InputLockText.text =
                    "WINNER REVEAL  -  CONTROLS UNLOCK IN " + seconds;
                bindings.LeaveRoomButton.interactable = false;
                bindings.LeaveRoomButtonText.text = "LEAVE ROOM";
                bindings.ReturnStatusText.text =
                    "The first-place podium is in the spotlight.";
                return;
            }

            if (match.CeremonyPhase != AwardCeremonyPhase.AwaitingReturn)
            {
                bindings.InputLockText.text = string.Empty;
                bindings.LeaveRoomButton.interactable = false;
                bindings.ReturnStatusText.text = string.Empty;
                return;
            }

            ResolveLocalAvatar();
            var localReady = _localAvatar != null &&
                             match.IsCeremonyReturnReady(
                                 _localAvatar.AssignedSlot);
            bindings.InputLockText.text = "CEREMONY COMPLETE";
            bindings.LeaveRoomButton.interactable =
                match.CanSubmitCeremonyReturn && !localReady;
            bindings.LeaveRoomButtonText.text = localReady
                ? "WAITING..."
                : "LEAVE ROOM";
            bindings.ReturnStatusText.text =
                "Waiting for players  " +
                match.CeremonyReturnReadyCount + " / " +
                MultiplayerConstants.MaxPlayers;
        }

        private void RequestReturnToLobby()
        {
            OnlineSessionController.Instance?
                .RequestCompletedMatchReturn();
        }

        private void ResolveLocalAvatar()
        {
            if (_localAvatar != null && _localAvatar.IsSpawned)
            {
                return;
            }

            var manager = NetworkManager.Singleton;
            var playerObject = manager != null && manager.SpawnManager != null
                ? manager.SpawnManager.GetLocalPlayerObject()
                : null;
            _localAvatar = playerObject != null
                ? playerObject.GetComponent<NetworkPlayerAvatar>()
                : null;
        }

        private string BuildWinnerNames(NetworkMatchState match, byte mask)
        {
            var names = new List<string>(MultiplayerConstants.MaxPlayers);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                if ((mask & (1 << slot)) == 0)
                {
                    continue;
                }
                var avatar = match.GetAvatarForSlot(slot);
                names.Add(avatar != null &&
                          !string.IsNullOrWhiteSpace(avatar.DisplayName)
                    ? avatar.DisplayName
                    : "Player " + (slot + 1));
            }
            return names.Count > 0
                ? string.Join("  +  ", names)
                : "NO WINNER";
        }

        private static string FormatWinningValue(
            MatchAwardCategory category,
            int value)
        {
            switch (category)
            {
                case MatchAwardCategory.PeakGoldHeld:
                case MatchAwardCategory.TotalGoldEarned:
                    return value + " GOLD";
                case MatchAwardCategory.MinigameWins:
                    return value + " WIN" + (value == 1 ? string.Empty : "S");
                case MatchAwardCategory.MinigameLastPlaces:
                    return value + " LAST PLACE" + (value == 1 ? string.Empty : "S");
                case MatchAwardCategory.ItemUses:
                    return value + " ITEM USE" + (value == 1 ? string.Empty : "S");
                default:
                    return value + " DAMAGE";
            }
        }

        private void WireButton()
        {
            if (_wired || bindings == null || bindings.LeaveRoomButton == null)
            {
                return;
            }
            bindings.LeaveRoomButton.onClick.AddListener(RequestReturnToLobby);
            _wired = true;
        }

        private void UnwireButton()
        {
            if (!_wired || bindings == null || bindings.LeaveRoomButton == null)
            {
                return;
            }
            bindings.LeaveRoomButton.onClick.RemoveListener(RequestReturnToLobby);
            _wired = false;
        }

        private void SetCanvasVisible(bool visible)
        {
            if (bindings == null)
            {
                return;
            }
            if (bindings.RootCanvas != null)
            {
                bindings.RootCanvas.enabled = visible;
            }
            if (bindings.RootRaycaster != null)
            {
                bindings.RootRaycaster.enabled = visible;
            }
        }
    }
}
