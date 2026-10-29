using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Authored wall bindings. Collision always remains enabled for movement.</summary>
    public sealed class MagicMazeWall : MonoBehaviour
    {
        [SerializeField] private Collider[] movementColliders = Array.Empty<Collider>();
        [SerializeField] private Renderer[] solidRenderers = Array.Empty<Renderer>();
        [SerializeField] private GameObject outline;
        private static readonly Dictionary<Collider, MagicMazeWall> Owners = new Dictionary<Collider, MagicMazeWall>();
        public bool IsInactive { get; private set; }
        public IReadOnlyList<Collider> MovementColliders => movementColliders;
        public IReadOnlyList<Renderer> SolidRenderers => solidRenderers;
        public GameObject Outline => outline;
        public bool HasRequiredReferences => movementColliders.Length > 0 && solidRenderers.Length > 0 && outline != null &&
            Array.TrueForAll(movementColliders, c => c != null) && Array.TrueForAll(solidRenderers, r => r != null);
        public void Configure(Collider[] colliders, Renderer[] renderers, GameObject authoredOutline)
        {
            Unregister(); movementColliders = colliders; solidRenderers = renderers; outline = authoredOutline;
            Register(); SetInactive(false);
        }
        private void OnEnable() => Register();
        private void OnDisable() => Unregister();
        private void Register() { foreach (var c in movementColliders) if (c != null) Owners[c] = this; }
        private void Unregister()
        {
            foreach (var c in movementColliders)
                if (c != null && Owners.TryGetValue(c, out var owner) && owner == this) Owners.Remove(c);
        }
        public void SetInactive(bool inactive)
        {
            IsInactive = inactive;
            foreach (var r in solidRenderers) if (r != null) r.enabled = !inactive;
            if (outline != null) outline.SetActive(inactive);
        }
        public static bool AllowsSightAndAttacks(Collider collider) => collider != null &&
            Owners.TryGetValue(collider, out var owner) && owner != null && owner.isActiveAndEnabled && owner.IsInactive;
    }
}
