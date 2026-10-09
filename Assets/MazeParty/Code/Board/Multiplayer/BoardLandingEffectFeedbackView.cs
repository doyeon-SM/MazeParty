using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class BoardLandingEffectFeedbackView : MonoBehaviour
    {
        [SerializeField] private BoardMapIcon icon;
        [SerializeField] private Text label;
        [SerializeField] private Sprite moneyFilledIcon;
        [SerializeField, Min(0f)] private float headOffset = 0.55f;
        [SerializeField] private Color goldColor =
            new Color(1f, 0.82f, 0.16f, 1f);
        [SerializeField] private Color healingColor =
            new Color(0.3f, 1f, 0.48f, 1f);
        [SerializeField] private Color damageColor =
            new Color(1f, 0.3f, 0.28f, 1f);
        [SerializeField] private Color itemColor = Color.white;

        private Transform _head;
        private Camera _camera;
        private float _expiresAt;
        private bool _presenting;

        public bool HasRequiredReferences =>
            icon != null && label != null && moneyFilledIcon != null;

        public void Configure(
            BoardMapIcon targetIcon,
            Text targetLabel,
            Sprite goldSprite)
        {
            icon = targetIcon;
            label = targetLabel;
            moneyFilledIcon = goldSprite;
        }

        public void Present(
            Transform head,
            Camera outputCamera,
            BoardMapIconKind iconKind,
            string valueLabel,
            float remainingSeconds)
        {
            _head = head;
            _camera = outputCamera;
            _expiresAt = Time.unscaledTime + Mathf.Max(0f, remainingSeconds);
            _presenting = HasRequiredReferences && _head != null &&
                          remainingSeconds > 0f;
            if (!_presenting)
            {
                gameObject.SetActive(false);
                return;
            }

            var usesMoneySprite = iconKind == BoardMapIconKind.GoldGain ||
                                  iconKind == BoardMapIconKind.GoldLoss;
            icon.SetIcon(iconKind, usesMoneySprite ? moneyFilledIcon : null);
            icon.color = ResolveColor(iconKind, valueLabel);
            label.text = valueLabel ?? string.Empty;
            label.fontStyle = FontStyle.Normal;
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
