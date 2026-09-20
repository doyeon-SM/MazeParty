using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized world-space label contract used above each fixed Balloon
    /// Blow station. Its prefab owns typography and bar geometry; runtime code
    /// only supplies the player name, progress and local-player emphasis.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BalloonBlowStationLabel : MonoBehaviour
    {
        [SerializeField] private TextMesh displayNameText;
        [SerializeField] private TextMesh progressText;
        [SerializeField] private Transform progressFill;
        [SerializeField] private Renderer progressFillRenderer;
        [SerializeField] private Renderer localHighlightRenderer;

        private Vector3 _fullFillScale;
        private Vector3 _fullFillPosition;
        private bool _defaultsCaptured;

        public TextMesh DisplayNameText => displayNameText;
        public TextMesh ProgressText => progressText;
        public Transform ProgressFill => progressFill;
        public Renderer ProgressFillRenderer => progressFillRenderer;
        public Renderer LocalHighlightRenderer => localHighlightRenderer;

        public bool HasRequiredReferences =>
            displayNameText != null &&
            progressText != null &&
            progressFill != null &&
            progressFillRenderer != null &&
            localHighlightRenderer != null;

        public void Configure(
            TextMesh nameText,
            TextMesh percentText,
            Transform fill,
            Renderer fillRenderer,
            Renderer highlightRenderer)
        {
            displayNameText = nameText;
            progressText = percentText;
            progressFill = fill;
            progressFillRenderer = fillRenderer;
            localHighlightRenderer = highlightRenderer;
            _defaultsCaptured = false;
            CaptureDefaults();
        }

        public void SetContent(
            string playerName,
            float progressPercent,
            bool popped,
            bool isLocalPlayer,
            Color playerColor)
        {
            CaptureDefaults();
            var clamped = Mathf.Clamp(progressPercent, 0f, 100f);
            displayNameText.text = string.IsNullOrWhiteSpace(playerName)
                ? "PLAYER"
                : playerName.Trim();
            progressText.text = popped
                ? "POP!"
                : Mathf.RoundToInt(clamped) + "%";

            var normalized = clamped * 0.01f;
            var scale = _fullFillScale;
            scale.x *= normalized;
            progressFill.localScale = scale;
            var position = _fullFillPosition;
            position.x -= _fullFillScale.x * (1f - normalized) * 0.5f;
            progressFill.localPosition = position;

            SetRendererColor(progressFillRenderer, playerColor);
            localHighlightRenderer.gameObject.SetActive(isLocalPlayer);
            displayNameText.color = isLocalPlayer
                ? Color.white
                : new Color(0.86f, 0.9f, 0.96f, 1f);
            progressText.color = popped
                ? new Color(1f, 0.86f, 0.25f, 1f)
                : Color.white;
        }

        public void FaceCamera(Camera camera)
        {
            if (camera == null)
            {
                return;
            }

            if (camera.orthographic)
            {
                transform.rotation = camera.transform.rotation;
                return;
            }

            var direction = transform.position - camera.transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    direction.normalized,
                    Vector3.up);
            }
        }

        private void Awake()
        {
            WorldTextOcclusion.Apply(displayNameText);
            WorldTextOcclusion.Apply(progressText);
            CaptureDefaults();
        }

        private void CaptureDefaults()
        {
            if (_defaultsCaptured || progressFill == null)
            {
                return;
            }

            _fullFillScale = progressFill.localScale;
            _fullFillPosition = progressFill.localPosition;
            _defaultsCaptured = true;
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
