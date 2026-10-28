using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized contract owned by BoardMapCanvas.prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardMapCanvasBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GraphicRaycaster rootRaycaster;
        [SerializeField] private BoardMapView mapView;
        [SerializeField] private Text movesText;
        [SerializeField] private Text itemUsesText;
        [SerializeField] private GameObject bombardmentHintRoot;
        [SerializeField] private RawImage bombardmentHintIcon;
        [SerializeField] private Text bombardmentHintText;
        [SerializeField] private Text bombardmentProgressText;
        [SerializeField] private string bombardmentHint = "Set Bombardment";
        [SerializeField] private string bombardmentProgress = "{0}/3";

        public void PresentBombardment(bool visible, int count)
        {
            bombardmentHintRoot.SetActive(visible);
            if (!visible) return;
            bombardmentHintText.text = GameText.T(bombardmentHint);
            bombardmentProgressText.text = GameText.F(bombardmentProgress, count);
        }

        [SerializeField] private int numberSizeThrough10 = 50;
        [SerializeField] private int numberSizeThrough20 = 60;
        [SerializeField] private int numberSizeThrough24 = 70;
        [SerializeField] private float mergeBounceSeconds = 0.5f;
        [SerializeField] private float mergeBounceAmount = 0.32f;
        private int _previousMoves = -1;
        private int _presentedTurn = -1;
        private float _bounceElapsed = float.PositiveInfinity;
        private int _numberSize;

        public void PresentMoves(int moves, bool doubleDice, int turn, bool paused)
        {
            if (_presentedTurn != turn)
            {
                _presentedTurn = turn;
                _previousMoves = -1;
                _bounceElapsed = float.PositiveInfinity;
            }
            moves = Mathf.Max(0, moves);
            if (doubleDice && _previousMoves > 0 && moves > _previousMoves)
                _bounceElapsed = 0f;
            _previousMoves = moves;
            _numberSize = moves <= 10 ? numberSizeThrough10 :
                moves <= 20 ? numberSizeThrough20 : numberSizeThrough24;
            if (!paused) _bounceElapsed += Time.unscaledDeltaTime;
            var t = Mathf.Clamp01(_bounceElapsed / Mathf.Max(0.01f, mergeBounceSeconds));
            var bounce = t >= 1f ? 0f : Mathf.Sin(t * Mathf.PI * 3f) * (1f - t) * (1f - t);
            movesText.fontSize = Mathf.RoundToInt(_numberSize * (1f + mergeBounceAmount * bounce));
            movesText.text = moves.ToString();
        }

        public Canvas RootCanvas => rootCanvas;
        public GraphicRaycaster RootRaycaster => rootRaycaster;
        public BoardMapView MapView => mapView;
        public Text MovesText => movesText;
        public Text ItemUsesText => itemUsesText;

        public bool HasRequiredReferences =>
            bombardmentHintRoot != null && bombardmentHintIcon != null &&
            bombardmentHintIcon.texture != null && bombardmentHintText != null &&
            bombardmentProgressText != null &&
            rootCanvas != null &&
            rootRaycaster != null &&
            mapView != null &&
            mapView.HasRequiredReferences &&
            movesText != null && itemUsesText != null &&
            itemUsesText.transform.IsChildOf(movesText.transform);

        public void Configure(
            Canvas canvas,
            GraphicRaycaster raycaster,
            BoardMapView boardMapView,
            Text remainingMovesText,
            Text remainingItemUsesText)
        {
            rootCanvas = canvas;
            rootRaycaster = raycaster;
            mapView = boardMapView;
            movesText = remainingMovesText;
            itemUsesText = remainingItemUsesText;
        }
    }
}
