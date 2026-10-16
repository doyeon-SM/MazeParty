using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>Authored board markers. ME and P1-P4 keep their fixed copy and font in every language.</summary>
    [DefaultExecutionOrder(1100)]
    [DisallowMultipleComponent]
    public sealed class BoardLocalPlayerMarkerView : MonoBehaviour
    {
        [SerializeField] private Canvas markerCanvas;
        [SerializeField] private RectTransform canvasRect;
        [SerializeField] private Text label;
        [SerializeField] private Text[] playerLabels = new Text[MultiplayerConstants.MaxPlayers];
        [SerializeField] private Outline[] playerOutlines = new Outline[MultiplayerConstants.MaxPlayers];
        [SerializeField, Min(0f)] private float screenOffset = 0f;

        private Transform _head;
        private Camera _camera;
        private bool _requested;
        private readonly Transform[] _playerHeads = new Transform[MultiplayerConstants.MaxPlayers];
        private readonly bool[] _playerRequested = new bool[MultiplayerConstants.MaxPlayers];

        public Text Label => label;
        public bool HasRequiredReferences
        {
            get
            {
                if (markerCanvas == null || canvasRect == null || label == null ||
                    markerCanvas.renderMode != RenderMode.ScreenSpaceOverlay ||
                    !label.transform.IsChildOf(canvasRect) ||
                    playerLabels == null || playerLabels.Length != MultiplayerConstants.MaxPlayers ||
                    playerOutlines == null || playerOutlines.Length != MultiplayerConstants.MaxPlayers)
                    return false;

                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    var playerLabel = playerLabels[slot];
                    if (playerLabel == null || playerLabel == label ||
                        playerOutlines[slot] == null ||
                        playerOutlines[slot].gameObject != playerLabel.gameObject ||
                        !playerLabel.transform.IsChildOf(canvasRect))
                        return false;
                    for (var previous = 0; previous < slot; previous++)
                        if (playerLabels[previous] == playerLabel)
                            return false;
                }
                return true;
            }
        }

        public bool IsFixedLabel(Text text) =>
            text != null && (text == label ||
                (playerLabels != null && Array.IndexOf(playerLabels, text) >= 0));

        public void Configure(Canvas canvas, Text text)
        {
            markerCanvas = canvas;
            canvasRect = canvas.transform as RectTransform;
            label = text;
        }

        public void ConfigurePlayerMarkers(Text[] labels, Outline[] outlines)
        {
            playerLabels = labels;
            playerOutlines = outlines;
        }

        public void Present(Transform head, Camera outputCamera, bool visible)
        {
            _head = head;
            _camera = outputCamera;
            _requested = visible;
            RefreshPose();
        }

        public void PresentPlayer(int slot, Transform head, Camera outputCamera,
            bool visible, Color playerColor)
        {
            if (slot < 0 || slot >= MultiplayerConstants.MaxPlayers)
                return;
            _playerHeads[slot] = head;
            _playerRequested[slot] = visible;
            _camera = outputCamera;
            if (HasRequiredReferences && playerOutlines[slot].effectColor != playerColor)
                playerOutlines[slot].effectColor = playerColor;
            RefreshPose();
        }

        public void Hide()
        {
            _requested = false;
            _head = null;
            _camera = null;
            Array.Clear(_playerHeads, 0, _playerHeads.Length);
            Array.Clear(_playerRequested, 0, _playerRequested.Length);
            if (markerCanvas != null) markerCanvas.enabled = false;
        }

        private void OnDisable() => Hide();
        private void LateUpdate() => RefreshPose();

        private void RefreshPose()
        {
            if (!HasRequiredReferences) return;
            var visible = RefreshLabel(label, _head, _requested);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                visible |= RefreshLabel(playerLabels[slot], _playerHeads[slot], _playerRequested[slot]);
            markerCanvas.enabled = visible;
        }

        private bool RefreshLabel(Text target, Transform head, bool requested)
        {
            var visible = requested && head != null && _camera != null;
            if (visible)
            {
                var viewport = _camera.WorldToViewportPoint(head.position);
                visible = viewport.z > _camera.nearClipPlane &&
                    viewport.x >= 0f && viewport.x <= 1f &&
                    viewport.y >= 0f && viewport.y <= 1f;
                if (visible && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                        canvasRect, _camera.WorldToScreenPoint(head.position),
                        null, out var position))
                {
                    target.rectTransform.anchoredPosition = position + Vector2.up * screenOffset;
                }
            }
            if (target.gameObject.activeSelf != visible)
                target.gameObject.SetActive(visible);
            return visible;
        }
    }
}
