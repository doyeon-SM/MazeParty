using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed class BoardTileEffectWorldView : MonoBehaviour
    {
        [SerializeField] private Canvas worldCanvas;
        [SerializeField] private BoardMapIcon icon;
        [SerializeField] private BoardMapCircleGraphic backdrop;
        [SerializeField, Min(0f)] private float height = .04f;
        private Vector3 _authoredScale;
        private bool _scaleCaptured;
        private BoardTile _placedTile;
        private Vector3 _placedCenter;
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
            if (_placedTile != tile || _placedCenter != tile.WorldCenter)
            {
                _placedTile = tile; _placedCenter = tile.WorldCenter;
                transform.position = BoardTileWorldPlacement.GroundPoint(tile, tile.GetRecoveryCenter()) +
                                     Vector3.up * height;
            }
            transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            transform.localScale = _authoredScale;
            if (camera != null) worldCanvas.worldCamera = camera;
        }
    }
}
