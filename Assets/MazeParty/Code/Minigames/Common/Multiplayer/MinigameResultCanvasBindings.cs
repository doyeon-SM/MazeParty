using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized references owned by the separate final minigame result Canvas.
    /// All layout and visual styling remain in MinigameResultCanvas.prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameResultCanvasBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GameObject resultPanel;
        [SerializeField] private Text resultTitle;
        [SerializeField] private Text resultNote;
        [SerializeField] private Text resultSummary;

        public Canvas RootCanvas => rootCanvas;
        public GameObject ResultPanel => resultPanel;
        public Text ResultTitle => resultTitle;
        public Text ResultNote => resultNote;
        public Text ResultSummary => resultSummary;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            resultPanel != null &&
            resultTitle != null &&
            resultNote != null &&
            resultSummary != null &&
            resultPanel.transform.IsChildOf(transform) &&
            resultTitle.transform.IsChildOf(resultPanel.transform) &&
            resultNote.transform.IsChildOf(resultPanel.transform) &&
            resultSummary.transform.IsChildOf(resultPanel.transform);

        public void Configure(
            Canvas canvas,
            GameObject panel,
            Text title,
            Text note,
            Text summary)
        {
            rootCanvas = canvas;
            resultPanel = panel;
            resultTitle = title;
            resultNote = note;
            resultSummary = summary;
        }
    }
}
