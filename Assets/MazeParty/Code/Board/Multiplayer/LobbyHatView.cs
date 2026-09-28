using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed class LobbyHatView : MonoBehaviour
    {
        [SerializeField] private OnlineLobbyView lobby;
        [SerializeField] private Button previous;
        [SerializeField] private Button next;
        [SerializeField] private Text title;

        public bool HasRequiredReferences =>
            lobby != null && previous != null && next != null && title != null;

        private void Awake()
        {
            if (!HasRequiredReferences)
            {
                Debug.LogError("Hat selector bindings missing.", this);
                enabled = false;
                return;
            }

            previous.onClick.AddListener(SelectPrevious);
            next.onClick.AddListener(SelectNext);
        }

        private void OnDestroy()
        {
            if (previous != null)
            {
                previous.onClick.RemoveListener(SelectPrevious);
            }

            if (next != null)
            {
                next.onClick.RemoveListener(SelectNext);
            }
        }

        private void SelectPrevious()
        {
            Select(-1);
        }

        private void SelectNext()
        {
            Select(1);
        }

        private void Select(int delta)
        {
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null)
            {
                return;
            }

            var count = catalog.Hats.Length + 1;
            lobby.SelectHat((byte)((lobby.SelectedHat + delta + count) % count));
        }

        private void Update()
        {
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null)
            {
                return;
            }

            var id = PlayerExpressionCatalog.SanitizeHat(lobby.SelectedHat);
            title.text = id == 0
                ? GameText.T("None")
                : GameText.T(catalog.Hats[id - 1].Name);
        }
    }
}
