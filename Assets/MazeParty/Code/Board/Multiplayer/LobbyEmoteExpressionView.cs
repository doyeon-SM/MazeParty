using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed class LobbyEmoteExpressionView : MonoBehaviour
    {
        [SerializeField] private OnlineLobbyView lobby;
        [SerializeField] private Button previousEmote;
        [SerializeField] private Button nextEmote;
        [SerializeField] private Button previousFace;
        [SerializeField] private Button nextFace;
        [SerializeField] private Image preview;
        [SerializeField] private Text emoteTitle;
        [SerializeField] private Text faceTitle;

        private byte _gestureId = (byte)HandEmoteId.Greeting;

        public bool HasRequiredReferences =>
            lobby != null &&
            previousEmote != null &&
            nextEmote != null &&
            previousFace != null &&
            nextFace != null &&
            preview != null &&
            emoteTitle != null &&
            faceTitle != null;

        private void Awake()
        {
            if (!HasRequiredReferences)
            {
                Debug.LogError("Emote expression selector bindings missing.", this);
                enabled = false;
                return;
            }

            previousEmote.onClick.AddListener(() => SelectEmote(-1));
            nextEmote.onClick.AddListener(() => SelectEmote(1));
            previousFace.onClick.AddListener(() => SelectFace(-1));
            nextFace.onClick.AddListener(() => SelectFace(1));
        }

        private void SelectEmote(int delta)
        {
            _gestureId = (byte)((_gestureId - 1 + delta +
                                 HandEmoteRules.GestureCount) %
                                HandEmoteRules.GestureCount + 1);
        }

        private void SelectFace(int delta)
        {
            if ((HandEmoteId)_gestureId == HandEmoteId.EyesCover)
            {
                return;
            }
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null || catalog.Faces.Length == 0)
            {
                return;
            }

            var current = lobby.SelectedEmoteExpressions.Get(
                _gestureId,
                lobby.SelectedExpression);
            var next = (byte)((current + delta + catalog.Faces.Length) %
                              catalog.Faces.Length);
            lobby.SelectEmoteExpression(_gestureId, next);
        }

        private void Update()
        {
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null ||
                catalog.Faces.Length == 0 ||
                catalog.Gestures.Length < HandEmoteRules.GestureCount)
            {
                return;
            }

            var gesture = catalog.Gestures[_gestureId - 1];
            var keepsBaseFace =
                (HandEmoteId)_gestureId == HandEmoteId.EyesCover;
            var faceId = keepsBaseFace
                ? lobby.SelectedExpression
                : lobby.SelectedEmoteExpressions.Get(
                    _gestureId,
                    lobby.SelectedExpression);
            var face = catalog.Faces[
                PlayerExpressionCatalog.SanitizeFace(faceId)];
            emoteTitle.text = GameText.T(gesture.Name);
            faceTitle.text = keepsBaseFace
                ? GameText.F("{0} (FACE UNCHANGED)", GameText.T(face.Name))
                : GameText.T(face.Name);
            preview.sprite = face.Sprite;
            previousFace.interactable = !keepsBaseFace;
            nextFace.interactable = !keepsBaseFace;
        }
    }
}
