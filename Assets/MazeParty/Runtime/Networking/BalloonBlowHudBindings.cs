using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized design contract for the Balloon Blow HUD prefab. Runtime
    /// presentation updates values and visibility only; layout, typography,
    /// colors and progress-bar styling remain owned by the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BalloonBlowHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Text instructionText;
        [SerializeField] private Text localProgressText;
        [SerializeField] private Image localProgressFill;
        [SerializeField] private Text resultText;
        [SerializeField] private GameObject resultPanel;

        public Canvas RootCanvas => rootCanvas;
        public Text InstructionText => instructionText;
        public Text LocalProgressText => localProgressText;
        public Image LocalProgressFill => localProgressFill;
        public Text ResultText => resultText;
        public GameObject ResultPanel => resultPanel;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            instructionText != null &&
            localProgressText != null &&
            localProgressFill != null &&
            resultText != null &&
            resultPanel != null &&
            resultPanel.GetComponent<Canvas>() != null;

        public void Configure(
            Canvas canvas,
            Text instructions,
            Text progressText,
            Image progressFill,
            Text resultMessage,
            GameObject resultPanelObject)
        {
            rootCanvas = canvas;
            instructionText = instructions;
            localProgressText = progressText;
            localProgressFill = progressFill;
            resultText = resultMessage;
            resultPanel = resultPanelObject;
        }
    }
}
