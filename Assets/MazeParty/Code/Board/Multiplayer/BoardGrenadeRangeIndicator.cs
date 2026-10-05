using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Binds the authored world-space grenade range ring. Runtime code only
    /// changes its radius and visibility; it never constructs presentation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardGrenadeRangeIndicator : MonoBehaviour
    {
        [SerializeField] private GameObject rangeGraphic;
        [SerializeField] private Transform rangeRoot;
        [SerializeField] private LineRenderer rangeLine;

        public bool HasRequiredReferences =>
            rangeGraphic != null &&
            rangeRoot != null &&
            rangeLine != null &&
            rangeRoot.gameObject == rangeGraphic &&
            rangeLine.transform == rangeRoot;

        public bool IsVisible =>
            rangeGraphic != null && rangeGraphic.activeSelf;

#if UNITY_EDITOR || DEBUG
        public float DevelopmentPresentedRadius =>
            rangeRoot != null ? rangeRoot.localScale.x : 0f;
#endif

        public void Configure(
            GameObject graphic,
            Transform root,
            LineRenderer line)
        {
            rangeGraphic = graphic;
            rangeRoot = root;
            rangeLine = line;
        }

        public void SetPresentation(float radius, bool visible)
        {
            if (!HasRequiredReferences)
            {
                return;
            }

            var sanitizedRadius = Mathf.Max(0f, radius);
            rangeRoot.localScale = new Vector3(
                sanitizedRadius,
                1f,
                sanitizedRadius);
            rangeGraphic.SetActive(visible && sanitizedRadius > 0f);
        }

        public void Hide()
        {
            if (rangeGraphic != null)
            {
                rangeGraphic.SetActive(false);
            }
        }
    }
}
