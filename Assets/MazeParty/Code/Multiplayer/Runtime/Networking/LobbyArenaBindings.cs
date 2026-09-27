using System;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Authored lobby geometry and gameplay anchors. The disabled bounds collider
    /// is a designer-visible volume and is not used for physical collision.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyArenaBindings : MonoBehaviour
    {
        [SerializeField] private GameObject presentationRoot;
        [SerializeField] private BoxCollider horizontalBounds;
        [SerializeField] private Transform[] spawnAnchors = Array.Empty<Transform>();

        public GameObject PresentationRoot => presentationRoot;
        public BoxCollider HorizontalBounds => horizontalBounds;
        public Transform[] SpawnAnchors => spawnAnchors;
        public int SpawnCount => spawnAnchors != null ? spawnAnchors.Length : 0;

        public Vector3 RoomCenter => horizontalBounds != null
            ? horizontalBounds.transform.TransformPoint(horizontalBounds.center)
            : transform.position;

        public Vector2 InnerSize
        {
            get
            {
                if (horizontalBounds == null)
                {
                    return Vector2.zero;
                }
                var scale = horizontalBounds.transform.lossyScale;
                return new Vector2(
                    Mathf.Abs(horizontalBounds.size.x * scale.x),
                    Mathf.Abs(horizontalBounds.size.z * scale.z));
            }
        }

        public bool HasRequiredReferences =>
            presentationRoot != null &&
            horizontalBounds != null &&
            horizontalBounds.transform.IsChildOf(transform) &&
            presentationRoot.transform.IsChildOf(transform) &&
            HasFourAssigned(spawnAnchors);

        public void Configure(
            GameObject presentation,
            BoxCollider bounds,
            Transform[] spawns)
        {
            presentationRoot = presentation;
            horizontalBounds = bounds;
            spawnAnchors = spawns ?? Array.Empty<Transform>();
        }

        public Vector3 GetSpawnPosition(int slot)
        {
            if (spawnAnchors == null || slot < 0 || slot >= spawnAnchors.Length ||
                spawnAnchors[slot] == null)
            {
                return RoomCenter;
            }
            return spawnAnchors[slot].position;
        }

        public Vector3 ClampHorizontalPosition(Vector3 position, float radius)
        {
            if (horizontalBounds == null)
            {
                return position;
            }

            var boundsTransform = horizontalBounds.transform;
            var local = boundsTransform.InverseTransformPoint(position);
            var center = horizontalBounds.center;
            var half = horizontalBounds.size * 0.5f;
            var scale = boundsTransform.lossyScale;
            var localRadiusX = Mathf.Max(0f, radius) /
                               Mathf.Max(0.0001f, Mathf.Abs(scale.x));
            var localRadiusZ = Mathf.Max(0f, radius) /
                               Mathf.Max(0.0001f, Mathf.Abs(scale.z));
            local.x = Mathf.Clamp(
                local.x,
                center.x - Mathf.Max(0.1f, half.x - localRadiusX),
                center.x + Mathf.Max(0.1f, half.x - localRadiusX));
            local.z = Mathf.Clamp(
                local.z,
                center.z - Mathf.Max(0.1f, half.z - localRadiusZ),
                center.z + Mathf.Max(0.1f, half.z - localRadiusZ));
            return boundsTransform.TransformPoint(local);
        }

        private static bool HasFourAssigned(Transform[] values)
        {
            if (values == null || values.Length != MultiplayerConstants.MaxPlayers)
            {
                return false;
            }
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] == null)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
