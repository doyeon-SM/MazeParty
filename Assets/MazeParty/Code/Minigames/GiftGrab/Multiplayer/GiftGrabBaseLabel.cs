using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Prefab-authored world label shared by temporary player status and
    /// corner-base score signs. Runtime only changes text, color and emphasis.
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
            SetElementActive(titleText, true);
            SetElementActive(detailText, true);
            SetElementActive(colorSwatchRenderer, true);
            SetElementActive(
                transform.Find("Label Backing")?.gameObject,
                true);

            titleText.text = string.IsNullOrWhiteSpace(title)
                ? GameText.T("PLAYER")
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

        private static void SetElementActive(
            Component component,
            bool active)
        {
            if (component != null)
            {
                component.gameObject.SetActive(active);
            }
        }

        private static void SetElementActive(
            GameObject gameObject,
            bool active)
        {
            if (gameObject != null)
            {
                gameObject.SetActive(active);
            }
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

        public void SetNumberOnly(int value)
        {
            SetElementActive(titleText, false);
            SetElementActive(colorSwatchRenderer, false);
            SetElementActive(localHighlightRenderer, false);
            SetElementActive(
                transform.Find("Label Backing")?.gameObject,
                false);
            SetElementActive(detailText, true);

            detailText.text = Mathf.Max(0, value).ToString();
            detailText.color = Color.white;
            detailText.fontSize = 72;
            detailText.characterSize = 0.085f;
            detailText.transform.localPosition =
                new Vector3(0f, 0f, -0.055f);
        }
    }
}
