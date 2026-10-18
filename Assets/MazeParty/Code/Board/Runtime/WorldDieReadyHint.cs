using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Authored world-space mouse icons and localized die instructions.</summary>
    [DisallowMultipleComponent]
    public sealed class WorldDieReadyHint : MonoBehaviour
    {
        [SerializeField] private TextMesh moveText;
        [SerializeField] private TextMesh useText;
        [SerializeField] private Renderer leftMouseIcon;
        [SerializeField] private Renderer rightMouseIcon;

        public bool HasRequiredReferences => moveText != null && useText != null &&
            leftMouseIcon != null && rightMouseIcon != null;

        public void Configure(TextMesh move, TextMesh use, Renderer left, Renderer right)
        {
            moveText = move;
            useText = use;
            leftMouseIcon = left;
            rightMouseIcon = right;
            Refresh();
        }

        private void OnEnable()
        {
            GameText.LanguageChanged += Refresh;
            Refresh();
        }

        private void OnDisable() => GameText.LanguageChanged -= Refresh;

        public void Refresh()
        {
            if (moveText != null)
            {
                GameFonts.Apply(moveText);
                moveText.text = GameText.T("Move");
            }
            if (useText != null)
            {
                GameFonts.Apply(useText);
                useText.text = GameText.T("Use");
            }
        }
    }
}
