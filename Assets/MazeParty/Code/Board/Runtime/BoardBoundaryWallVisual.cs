using UnityEngine;

namespace MazeParty.Gameplay
{
    public sealed class BoardBoundaryWallVisual : MonoBehaviour
    {
        [SerializeField] private BoxCollider blockingCollider;
        [SerializeField] private Renderer[] stateRenderers;
        [SerializeField] private Color passableColor = new Color(.04f, .32f, 1f, .72f);
        [SerializeField] private Color blockedColor = new Color(.005f, .008f, .012f, 1f);
        private MaterialPropertyBlock _properties;
        private Renderer[] _allRenderers;
        public BoxCollider BlockingCollider => blockingCollider;
        public bool HasRequiredReferences => blockingCollider != null && stateRenderers != null &&
            stateRenderers.Length > 0 && System.Array.TrueForAll(stateRenderers, renderer => renderer != null);
        public void SetVisible(bool visible)
        {
            // Includes decorative children so remote players' private walls stay invisible.
            _allRenderers ??= GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in _allRenderers) renderer.enabled = visible;
        }
        public void SetPassable(bool passable)
        {
            blockingCollider.enabled = !passable;
            _properties ??= new MaterialPropertyBlock();
            foreach (var renderer in stateRenderers)
            {
                renderer.GetPropertyBlock(_properties);
                _properties.SetColor("_BaseColor", passable ? passableColor : blockedColor);
                _properties.SetColor("_Color", passable ? passableColor : blockedColor);
                renderer.SetPropertyBlock(_properties);
            }
        }
    }
}
