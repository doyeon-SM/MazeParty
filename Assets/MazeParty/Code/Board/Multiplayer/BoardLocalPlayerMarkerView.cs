using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Authored, local-only board marker. Its fixed ME copy is never localized.</summary>
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed class BoardLocalPlayerMarkerView : MonoBehaviour
    {
        [SerializeField] private Canvas markerCanvas;
        [SerializeField] private RectTransform canvasRect;
        [SerializeField] private Text label;
        [SerializeField, Min(0f)] private float screenOffset = 40f;

        private Transform _head;
        private Camera _camera;
        private bool _requested;

        public Text Label => label;
        public bool HasRequiredReferences => markerCanvas != null &&
            canvasRect != null && label != null &&
            markerCanvas.renderMode == RenderMode.ScreenSpaceOverlay &&
            label.transform.IsChildOf(canvasRect);

        public void Configure(Canvas canvas, Text text)
        {
            markerCanvas = canvas;
            canvasRect = canvas.transform as RectTransform;
            label = text;
        }

        public void Present(Transform head, Camera outputCamera, bool visible)
        {
            _head = head;
            _camera = outputCamera;
            _requested = visible;
            RefreshPose();
        }

        public void Hide()
        {
            _requested = false;
            _head = null;
            _camera = null;
            if (markerCanvas != null) markerCanvas.enabled = false;
        }

        private void OnDisable() => Hide();
        private void LateUpdate() => RefreshPose();

        private void RefreshPose()
        {
            if (!HasRequiredReferences) return;
            var visible = _requested && _head != null && _camera != null;
            if (visible)
            {
                var viewport = _camera.WorldToViewportPoint(_head.position);
                visible = viewport.z > _camera.nearClipPlane &&
                    viewport.x >= 0f && viewport.x <= 1f &&
                    viewport.y >= 0f && viewport.y <= 1f;
                if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect, _camera.WorldToScreenPoint(_head.position),
                        null, out var position))
                {
                    label.rectTransform.anchoredPosition =
                        position + Vector2.up * screenOffset;
                }
            }
            markerCanvas.enabled = visible;
        }
    }
}
