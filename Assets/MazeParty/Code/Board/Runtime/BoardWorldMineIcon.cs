using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Prefab-authored world marker for an owner-visible board mine. The
    /// marker stays camera-facing while its parent remains at the authoritative
    /// placement point.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardWorldMineIcon : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer iconRenderer;

        public SpriteRenderer IconRenderer => iconRenderer;
        public bool HasRequiredReferences =>
            iconRenderer != null && iconRenderer.sprite != null;

        public void Configure(SpriteRenderer renderer)
        {
            iconRenderer = renderer;
        }

        private void LateUpdate()
        {
            var outputCamera = Camera.main;
            if (outputCamera == null)
                return;

            transform.rotation = outputCamera.transform.rotation;
        }
    }
}
