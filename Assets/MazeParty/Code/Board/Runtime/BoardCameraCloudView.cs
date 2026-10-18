using System;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Gameplay
{
    public static class BoardCameraCloudTiming
    {
        public const double Duration = 2d;
        public const double CoveredAt = .7d;
        public const double RevealAt = 1.1d;
        public static float Opacity(double elapsed)
        {
            if (elapsed <= 0d || elapsed >= Duration) return 0f;
            var t = elapsed < CoveredAt ? elapsed / CoveredAt :
                elapsed <= RevealAt ? 1d : (Duration - elapsed) / (Duration - RevealAt);
            return Mathf.SmoothStep(0f, 1f, (float)t);
        }
        public static bool IsCovered(double elapsed) => elapsed >= CoveredAt && elapsed <= RevealAt;
    }

    /// <summary>Updates only the authored cloud veil and cloud layer transforms.</summary>
    public sealed class BoardCameraCloudView : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private Image opaqueVeil;
        [SerializeField] private RectTransform[] clouds = Array.Empty<RectTransform>();
        [SerializeField] private float approachScale = 1.55f;
        private Vector2[] _positions;
        private Vector3[] _scales;
        public bool HasRequiredReferences => rootCanvas != null && group != null &&
            opaqueVeil != null && clouds != null && clouds.Length > 0 && Array.TrueForAll(clouds, c => c != null);
        public bool IsFullyCovered => HasRequiredReferences && group.alpha >= 1f && opaqueVeil.color.a >= 1f;

        private void Awake() { Capture(); Hide(); }
        private void Capture()
        {
            if (_positions != null || !HasRequiredReferences) return;
            _positions = new Vector2[clouds.Length]; _scales = new Vector3[clouds.Length];
            for (var i = 0; i < clouds.Length; i++)
            { _positions[i] = clouds[i].anchoredPosition; _scales[i] = clouds[i].localScale; }
        }
        public void Present(double elapsed, bool forceCovered = false)
        {
            if (!HasRequiredReferences) return;
            Capture();
            group.alpha = forceCovered ? 1f : BoardCameraCloudTiming.Opacity(elapsed);
            group.blocksRaycasts = group.alpha > 0f;
            var progress = Mathf.Clamp01((float)(elapsed / BoardCameraCloudTiming.Duration));
            for (var i = 0; i < clouds.Length; i++)
            {
                clouds[i].anchoredPosition = _positions[i] * Mathf.Lerp(1.25f, .7f, progress);
                clouds[i].localScale = _scales[i] * Mathf.Lerp(.85f, approachScale, progress);
            }
        }
        public void Hide()
        {
            if (group == null) return;
            group.alpha = 0f; group.blocksRaycasts = false;
        }
        private void OnDisable() => Hide();
    }
}
