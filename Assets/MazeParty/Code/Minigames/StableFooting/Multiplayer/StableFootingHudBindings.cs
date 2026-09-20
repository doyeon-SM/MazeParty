using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized design contract for the Stable Footing HUD prefab. Runtime
    /// code only updates values and panel visibility through these bindings;
    /// layout and styling remain owned by the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class StableFootingHudBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private Text instructionText;
        [SerializeField] private GameObject resultPanel;

        public Canvas RootCanvas => rootCanvas;
        public Text InstructionText => instructionText;
        public GameObject ResultPanel => resultPanel;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            instructionText != null &&
            resultPanel != null &&
            resultPanel.GetComponent<Canvas>() != null;

        public void Configure(
            Canvas canvas,
            Text instructions,
            GameObject result)
        {
            rootCanvas = canvas;
            instructionText = instructions;
            resultPanel = result;
        }
    }
}
