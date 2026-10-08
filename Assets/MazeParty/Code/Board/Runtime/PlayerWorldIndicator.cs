using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Authored world-space nameplate and local-player location marker used by
    /// gameplay representations that do not use <see cref="PlayerAvatarVisual"/>.
    /// Runtime code only updates its text, visibility and pose.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerWorldIndicator : MonoBehaviour
    {
        [SerializeField] private Transform nameplateAnchor;
        [SerializeField] private TextMesh nameText;
        [SerializeField] private GameObject localStartHighlight;

        public Transform NameplateAnchor => nameplateAnchor;
        public TextMesh NameText => nameText;
        public GameObject LocalStartHighlight => localStartHighlight;

        public bool HasRequiredReferences =>
            nameplateAnchor != null &&
            nameText != null &&
            nameText.transform.IsChildOf(nameplateAnchor) &&
            localStartHighlight != null &&
            localStartHighlight.transform.IsChildOf(transform);

        public void Configure(
            Transform nameplate,
            TextMesh displayName,
            GameObject highlight)
        {
            nameplateAnchor = nameplate;
            nameText = displayName;
            localStartHighlight = highlight;
        }

        public void SetDisplayName(string value)
        {
            if (nameText != null)
            {
                nameText.text = string.IsNullOrWhiteSpace(value)
                    ? GameText.T("Player")
                    : value.Trim();
            }
        }

        public void SetVisible(bool visible)
        {
            if (gameObject.activeSelf != visible)
            {
                gameObject.SetActive(visible);
            }
        }

        public void SetLocalStartHighlight(bool visible)
        {
            if (localStartHighlight != null &&
                localStartHighlight.activeSelf != visible)
            {
                localStartHighlight.SetActive(visible);
            }
        }

        public void SetPose(
            Transform target,
            Camera viewingCamera,
            float nameOffset,
            float highlightScale)
        {
            if (target == null || viewingCamera == null ||
                !HasRequiredReferences)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            var cameraTransform = viewingCamera.transform;
            transform.position = target.position;

            nameplateAnchor.position =
                target.position + cameraTransform.up * nameOffset;
            var nameDirection =
                nameplateAnchor.position - cameraTransform.position;
            if (nameDirection.sqrMagnitude > 0.0001f)
            {
                nameplateAnchor.rotation = Quaternion.LookRotation(
                    nameDirection.normalized,
                    cameraTransform.up);
            }

            var highlightTransform = localStartHighlight.transform;
            highlightTransform.position = target.position;
            highlightTransform.rotation = Quaternion.identity;
            highlightTransform.localScale = Vector3.one * highlightScale;
        }
    }
}
