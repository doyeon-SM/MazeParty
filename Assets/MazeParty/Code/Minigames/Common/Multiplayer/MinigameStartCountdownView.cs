using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Shared minigame and local board-combat countdown. It is a Board
    /// scene root, independent of the hidden Board Canvas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameStartCountdownView : MonoBehaviour
    {
        [SerializeField] private Canvas overlayCanvas;
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private Text numeralText;
        [SerializeField] private Text combatPromptText;
        [SerializeField] private NetworkMatchState matchState;

        private int _displayedNumeral = -1;
        private bool _visible;

        public bool HasRequiredReferences =>
            overlayCanvas != null &&
            contentRoot != null &&
            numeralText != null && combatPromptText != null &&
            contentRoot.transform.IsChildOf(transform) &&
            numeralText.transform.IsChildOf(contentRoot.transform) &&
            combatPromptText.transform.IsChildOf(contentRoot.transform);

        public bool IsVisible => _visible;
        public int DisplayedNumeral => _displayedNumeral;
        public NetworkMatchState MatchState => matchState;

        public void ConfigureUiBindings(
            Canvas canvas,
            GameObject content,
            Text numeral, Text combatPrompt)
        {
            overlayCanvas = canvas;
            contentRoot = content;
            numeralText = numeral;
            combatPromptText = combatPrompt;
        }

        public void ConfigureMatch(NetworkMatchState match)
        {
            matchState = match;
        }

        private void Awake()
        {
            SetCountdown(0, false);
        }

        private void Update()
        {
            var match = NetworkMatchState.Instance != null
                ? NetworkMatchState.Instance
                : matchState;
            var localSlot = -1;
            if (match != null && match.IsCombatCountdown)
            {
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    if (match.GetAvatarForSlot(slot)?.IsOwner == true)
                    {
                        localSlot = slot;
                        break;
                    }
                }
            }
            RefreshForPlayer(match, localSlot);
        }

        public void RefreshForPlayer(NetworkMatchState match, int localSlot)
        {
            var boardCombat = match != null && match.IsCombatCountdown &&
                              match.IsCombatParticipant(localSlot) &&
                              match.IsCombatAlive(localSlot);
            if (match == null || (!boardCombat && !match.IsMinigameRoundCountdown))
            {
                SetCountdown(0, false);
                return;
            }

            var remaining = boardCombat
                ? match.CombatCountdownRemaining
                : match.MinigameRoundCountdownRemaining;
            var numeral = Mathf.CeilToInt((float)remaining);
            SetCountdown(Mathf.Clamp(numeral, 1, 3), remaining > 0d,
                showCombatPrompt: boardCombat);
        }

        public void SetCountdown(int numeral, bool visible, bool playSounds = true,
            bool showCombatPrompt = false)
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            var show = visible && numeral > 0;
            var showPrompt = show && showCombatPrompt;
            if (combatPromptText.gameObject.activeSelf != showPrompt)
            {
                combatPromptText.gameObject.SetActive(showPrompt);
            }
            if (show && _displayedNumeral != numeral)
            {
                numeralText.text = numeral.ToString();
                if (playSounds && Application.isPlaying)
                {
                    GameSound.Play(SoundKeys.MinigameCountdownTick);
                }
            }
            else if (!show && _visible && _displayedNumeral == 1 && playSounds && Application.isPlaying)
            {
                // The countdown ran out after "1": the round starts.
                GameSound.Play(SoundKeys.MinigameCountdownGo);
            }

            if (_visible != show || contentRoot.activeSelf != show)
            {
                contentRoot.SetActive(show);
            }

            _displayedNumeral = show ? numeral : 0;
            _visible = show;
        }
    }
}
