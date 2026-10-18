using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed class BoardTileEffectWorldView : MonoBehaviour
    {
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private BoardMapIcon icon;
        [SerializeField] private BoardMapCircleGraphic backdrop;
        [SerializeField, Min(0f)] private float height = 2.8f;
        [SerializeField, Min(0.1f)] private float nearCameraShrinkDistance = 4f;
        private Vector3 _authoredScale;
        private bool _scaleCaptured;
        public bool HasRequiredReferences => worldCanvas != null &&
            worldCanvas.renderMode == RenderMode.WorldSpace && icon != null && backdrop != null;

        public void Present(BoardTile tile, BoardMinimapView appearance, Camera camera)
        {
            if (!_scaleCaptured) { _authoredScale = transform.localScale; _scaleCaptured = true; }
            var visible = tile != null && tile.LandingEffect != BoardLandingEffectType.None;
            gameObject.SetActive(visible);
            if (!visible) return;
            appearance.GetEffectAppearance(tile.LandingEffect, out var kind, out var sprite, out var color);
            icon.SetIcon(kind, sprite);
            icon.color = color;
            transform.position = tile.WorldCenter + Vector3.up * height;
            if (camera != null)
            {
                worldCanvas.worldCamera = camera;
                transform.rotation = camera.transform.rotation;
                transform.localScale = _authoredScale * Mathf.Min(1f,
                    Vector3.Distance(transform.position, camera.transform.position) / nearCameraShrinkDistance);
            }
        }
    }
}
