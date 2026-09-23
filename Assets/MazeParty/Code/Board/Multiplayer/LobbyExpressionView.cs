using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed class LobbyExpressionView : MonoBehaviour
    {
        [SerializeField] private OnlineLobbyView lobby;
        [SerializeField] private Button previous, next;
        [SerializeField] private Image preview;
        [SerializeField] private Text title;
        public bool HasRequiredReferences => lobby != null && previous != null && next != null && preview != null && title != null;
        private void Awake()
        {
            if (!HasRequiredReferences) { Debug.LogError("Expression selector bindings missing.", this); enabled = false; return; }
            previous.onClick.AddListener(() => Select(-1)); next.onClick.AddListener(() => Select(1));
        }
        private void Select(int delta)
        {
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null || catalog.Faces.Length == 0) return;
            lobby.SelectExpression((byte)((lobby.SelectedExpression + delta + catalog.Faces.Length) % catalog.Faces.Length));
        }
        private void Update()
        {
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null || catalog.Faces.Length == 0) return;
            var face = catalog.Faces[PlayerExpressionCatalog.SanitizeFace(lobby.SelectedExpression)];
            preview.sprite = face.Sprite; title.text = face.Name;
        }
    }
}
