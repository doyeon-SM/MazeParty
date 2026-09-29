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

        private readonly FinalRankRow[] _finalRows =
            new FinalRankRow[MultiplayerConstants.MaxPlayers];
        private NetworkPlayerAvatar _localAvatar;
        private AwardCeremonyPhase _observedPhase = AwardCeremonyPhase.None;
        private int _observedRevision = -1;
        private bool _wired;
        private SoundHandle _awardReadyHandle;
        private bool _wasSimulationSuspended;

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
            StopAwardReady();
            _observedPhase = AwardCeremonyPhase.None;
            _observedRevision = -1;
            _wasSimulationSuspended = false;
            UnwireButton();
        }

        private void Update()
        {
            var match = NetworkMatchState.Instance;
            var active = match != null &&
                         match.IsSpawned &&
                         match.IsAwardCeremonyActive;
            if (active)
            {
                // After "clean up board" this player is in the waiting room;
                // the others may still be looking at the ranking.
                ResolveLocalAvatar();
                active = _localAvatar == null ||
                         !match.IsBackInWaitingRoomDuringCeremony(
                             _localAvatar.AssignedSlot);
            }

            SetCanvasVisible(active);
            if (!active || bindings == null ||
                !bindings.HasRequiredReferences)
            {
                StopAwardReady();
                _observedPhase = AwardCeremonyPhase.None;
                _observedRevision = -1;
                _wasSimulationSuspended = false;
                Array.Clear(_finalRows, 0, _finalRows.Length);
                return;
            }

            ResolveLocalAvatar();
            var suspended = match.IsSimulationSuspended;
            if (_observedPhase != match.CeremonyPhase)
            {
                EnterPhase(match.CeremonyPhase, suspended);
                _observedPhase = match.CeremonyPhase;
            }
            else if (_wasSimulationSuspended != suspended)
            {
                if (suspended)
                {
                    StopAwardReady();
                }
                else if (match.CeremonyPhase ==
                             AwardCeremonyPhase.BonusAwardOneReady ||
                         match.CeremonyPhase ==
                             AwardCeremonyPhase.BonusAwardTwoReady)
                {
                    _awardReadyHandle =
                        GameSound.Play(SoundKeys.CeremonyAwardReady);
                }
            }
            _wasSimulationSuspended = suspended;

            if (_observedRevision != match.CeremonyRevision)
            {
                _observedRevision = match.CeremonyRevision;
                RefreshStaticCopy(match);
            }

            RefreshCountdownAndInteraction(match);
        }

        private void EnterPhase(
            AwardCeremonyPhase phase,
            bool simulationSuspended)
        {
            StopAwardReady();
            var ready = phase == AwardCeremonyPhase.BonusAwardOneReady ||
                        phase == AwardCeremonyPhase.BonusAwardTwoReady;
            var reveal = phase == AwardCeremonyPhase.BonusAwardOne ||
                         phase == AwardCeremonyPhase.BonusAwardTwo;
            var bonus = ready || reveal;
            var final = phase == AwardCeremonyPhase.FinalPodiumLocked ||
                        phase == AwardCeremonyPhase.AwaitingReturn;

            bindings.BonusAwardOverlay.SetActive(
                bonus || phase == AwardCeremonyPhase.FinalPodiumLocked);
            bindings.FinalRankingPanel.SetActive(final);
            if (ready)
            {
                bindings.BonusAwardAnimator.Play(
                    OverlayVisibleState,
                    0,
                    0f);
                ShowAwardReady(
                    phase == AwardCeremonyPhase.BonusAwardOneReady ? 0 : 1);
                if (!simulationSuspended)
                {
                    _awardReadyHandle =
                        GameSound.Play(SoundKeys.CeremonyAwardReady);
                }
            }
            else if (reveal)
            {
                bindings.BonusAwardAnimator.Play(
                    OverlayVisibleState,
                    0,
                    0f);
                GameSound.Play(SoundKeys.CeremonyAward);
            }
            else if (phase == AwardCeremonyPhase.FinalPodiumLocked)
            {
                bindings.BonusAwardAnimator.Play(
                    OverlaySlideUpState,
                    0,
                    0f);
                GameSound.Play(SoundKeys.CeremonyFanfare);
                GameSound.Play(SoundKeys.CeremonyApplause);
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
                case AwardCeremonyPhase.BonusAwardOneReady:
                    ShowAwardReady(0);
                    break;
                case AwardCeremonyPhase.BonusAwardOne:
                    RefreshAward(match, 0);
                    break;
                case AwardCeremonyPhase.BonusAwardTwoReady:
                    ShowAwardReady(1);
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
                GameText.F("BONUS KEY AWARD {0} / 2", awardIndex + 1);
            bindings.AwardCategoryText.text =
                GameText.T(MatchAwardRules.GetDisplayName(category));
            bindings.AwardValueText.text =
                FormatWinningValue(
                    category,
                    match.GetCeremonyAwardWinningValue(awardIndex));
            bindings.AwardWinnerText.text = BuildWinnerNames(
                match,
                match.GetCeremonyAwardWinnerMask(awardIndex));
            bindings.AwardRewardText.text = GameText.T("+1 KEY EACH");
        }

        private void RefreshFinalRanks(NetworkMatchState match)
        {
            bindings.FinalTitleText.text = GameText.T("FINAL RANKING");
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
                if (avatar != null)
                {
                    _finalRows[slot] = new FinalRankRow(
                        avatar.DisplayName,
                        avatar.KeyCount,
                        avatar.Gold,
                        avatar.MinigameWins);
                }

                // A player who already left the room keeps the row they had.
                var cached = _finalRows[slot];
                var name = cached.Known &&
                           !string.IsNullOrWhiteSpace(cached.DisplayName)
                    ? cached.DisplayName
                    : GameText.F("Player {0}", slot + 1);
                var rank = match.GetFinalCeremonyRank(slot);
                var keys = cached.Keys;
                var gold = cached.Gold;
                var wins = cached.Wins;
                bindings.FinalRankTexts[row].text = wins == 1
                    ? GameText.F(
                        "#{0}  {1}     {2} KEY  /  {3} GOLD  /  {4} WIN",
                        rank, name, keys, gold, wins)
                    : GameText.F(
                        "#{0}  {1}     {2} KEY  /  {3} GOLD  /  {4} WINS",
                        rank, name, keys, gold, wins);
            }
        }

        private void RefreshCountdownAndInteraction(NetworkMatchState match)
        {
            if (match.IsSimulationSuspended)
            {
                var reconnect = match.IsReconnectPaused;
                bindings.LeaveRoomButton.interactable = false;
                if (match.CeremonyPhase == AwardCeremonyPhase.BonusAwardOneReady ||
                    match.CeremonyPhase == AwardCeremonyPhase.BonusAwardOne ||
                    match.CeremonyPhase == AwardCeremonyPhase.BonusAwardTwoReady ||
                    match.CeremonyPhase == AwardCeremonyPhase.BonusAwardTwo)
                {
                    bindings.AwardRewardText.text = reconnect
                        ? GameText.T("CEREMONY PAUSED  -  WAITING FOR PLAYER")
                        : GameText.T("CEREMONY PAUSED");
                }
                else
                {
                    bindings.InputLockText.text = GameText.T("CEREMONY PAUSED");
                    bindings.ReturnStatusText.text = reconnect
                        ? GameText.T("Waiting for the disconnected player to return.")
                        : GameText.T("The game is paused.");
                }
                return;
            }

            if (match.CeremonyPhase == AwardCeremonyPhase.FinalPodiumLocked)
            {
                var seconds = Math.Max(
                    0,
                    (int)Math.Ceiling(match.CeremonyPhaseRemaining));
                bindings.InputLockText.text =
                    GameText.F("WINNER REVEAL  -  CONTROLS UNLOCK IN {0}", seconds);
                bindings.LeaveRoomButton.interactable = false;
                bindings.LeaveRoomButtonText.text = GameText.T("CLEAN UP BOARD");
                bindings.ReturnStatusText.text =
                    GameText.F(
                        "Returning to the waiting room automatically in {0} seconds.",
                        Math.Max(
                            0,
                            (int)Math.Ceiling(
                                match.CeremonyAutoReturnRemaining)));
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
            bindings.InputLockText.text = GameText.T("CEREMONY COMPLETE");
            bindings.LeaveRoomButton.interactable =
                match.CanSubmitCeremonyReturn && !localReady;
            bindings.LeaveRoomButtonText.text = localReady
                ? GameText.T("WAITING...")
                : GameText.T("CLEAN UP BOARD");
            bindings.ReturnStatusText.text =
                GameText.F(
                    "Waiting for players  {0} / {1}  ·  automatic return in {2}s",
                    match.CeremonyReturnReadyCount,
                    match.CeremonyReturnRequiredCount,
                    Math.Max(
                        0,
                        (int)Math.Ceiling(
                            match.CeremonyAutoReturnRemaining)));
        }

        private void StopAwardReady()
        {
            if (!_awardReadyHandle.IsValid)
            {
                return;
            }

            GameSound.Stop(_awardReadyHandle, 0f);
            _awardReadyHandle = default;
        }

        private void ShowAwardReady(int awardIndex)
        {
            bindings.AwardStepText.text =
                GameText.F("BONUS KEY AWARD {0} / 2", awardIndex + 1);
            bindings.AwardCategoryText.text = GameText.T("GET READY");
            bindings.AwardValueText.text = string.Empty;
            bindings.AwardWinnerText.text = string.Empty;
            bindings.AwardRewardText.text = string.Empty;
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
                    : GameText.F("Player {0}", slot + 1));
            }
            return names.Count > 0
                ? string.Join("  +  ", names)
                : GameText.T("NO WINNER");
        }

        private static string FormatWinningValue(
            MatchAwardCategory category,
            int value)
        {
            switch (category)
            {
                case MatchAwardCategory.PeakGoldHeld:
                case MatchAwardCategory.TotalGoldEarned:
                    return GameText.F("{0} GOLD", value);
                case MatchAwardCategory.MinigameWins:
                    return value == 1
                        ? GameText.F("{0} WIN", value)
                        : GameText.F("{0} WINS", value);
                case MatchAwardCategory.MinigameLastPlaces:
                    return value == 1
                        ? GameText.F("{0} LAST PLACE", value)
                        : GameText.F("{0} LAST PLACES", value);
                case MatchAwardCategory.ItemUses:
                    return value == 1
                        ? GameText.F("{0} ITEM USE", value)
                        : GameText.F("{0} ITEM USES", value);
                default:
                    return GameText.F("{0} DAMAGE", value);
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

        /// <summary>Last seen final-ranking values of one seat.</summary>
        private readonly struct FinalRankRow
        {
            public FinalRankRow(string displayName, int keys, int gold, int wins)
            {
                Known = true;
                DisplayName = displayName;
                Keys = keys;
                Gold = gold;
                Wins = wins;
            }

            public bool Known { get; }
            public string DisplayName { get; }
            public int Keys { get; }
            public int Gold { get; }
            public int Wins { get; }
        }
    }
}
