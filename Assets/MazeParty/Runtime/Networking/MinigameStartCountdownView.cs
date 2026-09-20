using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Countdown section of the shared minigame HUD prefab. It is a Board
    /// scene root, independent of the hidden Board Canvas.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameStartCountdownView : MonoBehaviour
    {
        [SerializeField] private Canvas overlayCanvas;
        [SerializeField] private GameObject contentRoot;
        [SerializeField] private Text numeralText;
        [SerializeField] private NetworkMatchState matchState;

        private int _displayedNumeral = -1;
        private bool _visible;

        public bool HasRequiredReferences =>
            overlayCanvas != null &&
            contentRoot != null &&
            numeralText != null &&
            contentRoot.transform.IsChildOf(transform) &&
            numeralText.transform.IsChildOf(contentRoot.transform);

        public bool IsVisible => _visible;
        public int DisplayedNumeral => _displayedNumeral;
        public NetworkMatchState MatchState => matchState;

        public void ConfigureUiBindings(
            Canvas canvas,
            GameObject content,
            Text numeral)
        {
            overlayCanvas = canvas;
            contentRoot = content;
            numeralText = numeral;
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
            if (match == null || !match.IsMinigameStartCountdown)
            {
                SetCountdown(0, false);
                return;
            }

            var remaining = match.MinigameStartCountdownRemaining;
            var numeral = Mathf.CeilToInt((float)remaining);
            SetCountdown(Mathf.Clamp(numeral, 1, 3), remaining > 0d);
        }

        public void SetCountdown(int numeral, bool visible)
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            var show = visible && numeral > 0;
            if (show && _displayedNumeral != numeral)
            {
                numeralText.text = numeral.ToString();
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
