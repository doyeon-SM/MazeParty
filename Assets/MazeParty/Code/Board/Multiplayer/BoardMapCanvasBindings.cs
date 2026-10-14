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

        public Canvas RootCanvas => rootCanvas;
        public GraphicRaycaster RootRaycaster => rootRaycaster;
        public BoardMapView MapView => mapView;
        public Text MovesText => movesText;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            rootRaycaster != null &&
            mapView != null &&
            mapView.HasRequiredReferences &&
            movesText != null;

        public void Configure(
            Canvas canvas,
            GraphicRaycaster raycaster,
            BoardMapView boardMapView,
            Text remainingMovesText)
        {
            rootCanvas = canvas;
            rootRaycaster = raycaster;
            mapView = boardMapView;
            movesText = remainingMovesText;
        }
    }
}
