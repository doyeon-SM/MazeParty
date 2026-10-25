using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class BoardLandingEffectFeedbackView : MonoBehaviour
    {
        [SerializeField] private Text label;
        [SerializeField, Min(0f)] private float headOffset = 0.55f;
        [SerializeField] private Color goldColor =
            new Color(1f, 0.82f, 0.16f, 1f);
        [SerializeField] private Color healingColor =
            new Color(0.3f, 1f, 0.48f, 1f);
        [SerializeField] private Color damageColor =
            new Color(1f, 0.3f, 0.28f, 1f);
        [SerializeField] private Color itemColor = Color.white;

        [SerializeField, Min(0f)] private float minimumHeadGapPixels = 10f;
        private readonly Vector3[] _labelCorners = new Vector3[4];
        private PlayerAvatarVisual _avatarVisual;
        private Transform _head;
        private Camera _camera;
        private float _expiresAt;
        private bool _presenting;

        public bool HasRequiredReferences => label != null;

        public void Configure(Text targetLabel)
        {
            label = targetLabel;
        }

        public void Present(
            Transform head,
            Camera outputCamera,
            BoardMapIconKind iconKind,
            string valueLabel,
            float remainingSeconds)
        {
            _head = head;
            _avatarVisual = head != null ? head.GetComponentInParent<PlayerAvatarVisual>() : null;
            _camera = outputCamera;
            _expiresAt = Time.unscaledTime + Mathf.Max(0f, remainingSeconds);
            _presenting = HasRequiredReferences && _head != null &&
                          remainingSeconds > 0f;
            if (!_presenting)
            {
                gameObject.SetActive(false);
                return;
            }

            label.color = ResolveColor(iconKind, valueLabel);
            label.text = valueLabel ?? string.Empty;
            gameObject.SetActive(true);
            RefreshPose();
        }

        private void LateUpdate()
        {
            if (!_presenting)
            {
                return;
            }

            if (_head == null || Time.unscaledTime >= _expiresAt)
            {
                Destroy(gameObject);
                return;
            }

            RefreshPose();
        }

        private void RefreshPose()
        {
            transform.position = _head.position + Vector3.up * headOffset;
            if (_camera == null)
            {
                _camera = Camera.main;
            }
            if (_camera == null)
            {
                return;
            }

            var awayFromCamera = transform.position -
                                 _camera.transform.position;
            if (awayFromCamera.sqrMagnitude > 0.000001f)
            {
                transform.rotation = Quaternion.LookRotation(
                    awayFromCamera.normalized,
                    _camera.transform.up);
            }
            // Keep the entire authored label above the projected head and hat,
            // including perspective views where a world-height offset is insufficient.
            if (_avatarVisual == null || !_avatarVisual.TryGetHeadViewportTop(_camera, out var top)) return;
            label.rectTransform.GetWorldCorners(_labelCorners);
            var bottom = float.PositiveInfinity;
            var bottomDepth = 0f;
            foreach (var corner in _labelCorners)
            {
                var projected = _camera.WorldToViewportPoint(corner);
                if (projected.z > _camera.nearClipPlane && projected.y < bottom)
                { bottom = projected.y; bottomDepth = projected.z; }
            }
            var anchor = _camera.WorldToViewportPoint(transform.position);
            if (anchor.z <= _camera.nearClipPlane || float.IsPositiveInfinity(bottom)) return;
            var gap = minimumHeadGapPixels / Mathf.Max(1f, _camera.pixelHeight);
            if (top + gap > bottom)
            {
                anchor.y += (top + gap - bottom) * bottomDepth / anchor.z;
                transform.position = _camera.ViewportToWorldPoint(anchor);
            }
        }

        private Color ResolveColor(
            BoardMapIconKind iconKind,
            string valueLabel)
        {
            switch (iconKind)
            {
                case BoardMapIconKind.GoldGain:
                case BoardMapIconKind.GoldLoss:
                    return goldColor;
                case BoardMapIconKind.Item:
                    return itemColor;
                default:
                    return !string.IsNullOrEmpty(valueLabel) &&
                           valueLabel[0] == '-'
                        ? damageColor
                        : healingColor;
            }
        }
    }
}
