using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class OnlineLoadingView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Image background;
        [SerializeField] private Text loadingText;
        [SerializeField] private string sourceText = "Entering";
        [SerializeField, Min(0.05f)] private float dotInterval = 0.4f;

        private static readonly string[] Suffixes = { "", ".", "..", "..." };
        private bool _visible;
        private double _shownAt;
        private int _renderedDots = -1;

        public bool HasRequiredReferences => canvasGroup != null &&
            background != null && background.sprite != null && loadingText != null &&
            !string.IsNullOrWhiteSpace(sourceText);

        private void Awake() => SetVisible(false);

        private void OnEnable() => GameText.LanguageChanged += RefreshText;

        private void OnDisable()
        {
            GameText.LanguageChanged -= RefreshText;
            SetVisible(false);
        }

        public void SetVisible(bool visible)
        {
            if (!HasRequiredReferences) return;
            if (visible && !_visible)
            {
                _shownAt = Time.realtimeSinceStartupAsDouble;
                _renderedDots = -1;
                // A previously selected lobby button must not receive Submit
                // through the full-screen loading image.
                EventSystem.current?.SetSelectedGameObject(null);
            }
            _visible = visible;
            canvasGroup.alpha = visible ? 1f : 0f;
            canvasGroup.interactable = visible;
            canvasGroup.blocksRaycasts = visible;
            if (visible && _renderedDots < 0) RefreshText();
        }

        private void Update()
        {
            if (!_visible) return;
            var dots = CurrentDots();
            if (dots != _renderedDots) RefreshText();
        }

        private int CurrentDots() => (int)(
            (Time.realtimeSinceStartupAsDouble - _shownAt) /
            Mathf.Max(0.05f, dotInterval) % Suffixes.Length);

        private void RefreshText()
        {
            if (!_visible || loadingText == null) return;
            _renderedDots = CurrentDots();
            loadingText.font = GameFonts.Current;
            loadingText.text = GameText.T(sourceText) + Suffixes[_renderedDots];
        }
    }
}
