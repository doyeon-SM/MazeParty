using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Prefab-authored world label shared by player nameplates and corner-base
    /// score signs. Runtime only changes text, color and local emphasis.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftGrabBaseLabel : MonoBehaviour
    {
        [SerializeField] private TextMesh titleText;
        [SerializeField] private TextMesh detailText;
        [SerializeField] private Renderer colorSwatchRenderer;
        [SerializeField] private Renderer localHighlightRenderer;

        public TextMesh TitleText => titleText;
        public TextMesh DetailText => detailText;
        public Renderer ColorSwatchRenderer => colorSwatchRenderer;
        public Renderer LocalHighlightRenderer => localHighlightRenderer;

        public bool HasRequiredReferences =>
            titleText != null &&
            detailText != null &&
            colorSwatchRenderer != null &&
            localHighlightRenderer != null;

        public void Configure(
            TextMesh title,
            TextMesh detail,
            Renderer colorSwatch,
            Renderer localHighlight)
        {
            titleText = title;
            detailText = detail;
            colorSwatchRenderer = colorSwatch;
            localHighlightRenderer = localHighlight;
        }

        public void SetContent(
            string title,
            string detail,
            bool isLocalPlayer,
            Color ownerColor)
        {
            titleText.text = string.IsNullOrWhiteSpace(title)
                ? "PLAYER"
                : title.Trim();
            detailText.text = detail ?? string.Empty;
            titleText.color = isLocalPlayer
                ? Color.white
                : new Color(0.88f, 0.92f, 0.98f, 1f);
            detailText.color = isLocalPlayer
                ? new Color(1f, 0.9f, 0.3f, 1f)
                : Color.white;
            localHighlightRenderer.gameObject.SetActive(isLocalPlayer);
            SetRendererColor(colorSwatchRenderer, ownerColor);
        }

        public void FaceCamera(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            transform.rotation = camera.orthographic
                ? camera.transform.rotation
                : Quaternion.LookRotation(
                    (transform.position - camera.transform.position)
                    .normalized,
                    Vector3.up);
        }

        private void Awake()
        {
            WorldTextOcclusion.Apply(titleText);
            WorldTextOcclusion.Apply(detailText);
        }

        private static void SetRendererColor(Renderer renderer, Color color)
        {
            if (renderer == null)
            {
                return;
            }

            var block = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            renderer.SetPropertyBlock(block);
        }
    }
}
