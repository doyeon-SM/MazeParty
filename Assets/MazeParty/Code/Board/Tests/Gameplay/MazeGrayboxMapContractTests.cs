using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MazeGrayboxMapContractTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/Resources/MazeParty/Board/Maps/MazeGrayboxMapRoot.prefab";
        private const string WindowPrefabPath =
            "Assets/Ignore/Maze/Prefabs/Wall_Window.prefab";
        private const string WallVisualRootName = "Wall";
        private const string WallColliderRootName = "Wall Colliders";
        private const int ExpectedColliderCount = 108;
        private const int ExpectedWindowCount = 10;
        private const int ExpectedWindowColliderCount = 4;
        private const int ExpectedOpenBoundaryCount = 80;
        private const float ColliderThickness = 0.8f;
        private const float ColliderHeight = 4.2f;
        private const float ColliderCenterHeight = 2.1f;
        private const float GateProbeOffset = 1.5f;
        private const float Tolerance = 0.001f;

        [Test]
        public void MazeGraybox_WallCollisionIsSeparateMinimalAndMatchesTopology()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var windowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                WindowPrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            Assert.That(windowPrefab, Is.Not.Null, WindowPrefabPath);

            var windowPaths = prefab
                .GetComponentsInChildren<Transform>(true)
                .Where(transform =>
                    PrefabUtility.GetCorrespondingObjectFromSource(
                        transform.gameObject) == windowPrefab)
                .Select(transform => AnimationUtility.CalculateTransformPath(
                    transform,
                    prefab.transform))
                .ToArray();
            Assert.That(windowPaths, Has.Length.EqualTo(ExpectedWindowCount));
            Assert.That(
                windowPaths.Distinct().Count(),
                Is.EqualTo(ExpectedWindowCount));

            var instance = Object.Instantiate(prefab);
            try
            {
                var mapRoot = instance.GetComponent<BoardMapRoot>();
                Assert.That(mapRoot, Is.Not.Null);

                var wallVisualRoot = mapRoot.transform.Find(WallVisualRootName);
                var wallColliderRoot = mapRoot.transform.Find(WallColliderRootName);
                Assert.That(wallVisualRoot, Is.Not.Null);
                Assert.That(wallColliderRoot, Is.Not.Null);
                Assert.That(wallColliderRoot.parent, Is.SameAs(mapRoot.transform));
                Assert.That(wallColliderRoot.gameObject.activeSelf, Is.True);
                Assert.That(wallColliderRoot.localPosition, Is.EqualTo(Vector3.zero));
                Assert.That(wallColliderRoot.localRotation, Is.EqualTo(Quaternion.identity));
                Assert.That(wallColliderRoot.localScale, Is.EqualTo(Vector3.one));

                var windows = windowPaths
                    .Select(path => instance.transform.Find(path))
                    .ToArray();
                Assert.That(windows, Has.All.Not.Null);
                var windowColliders = windows
                    .SelectMany(window =>
                        window.GetComponentsInChildren<BoxCollider>(true))
                    .ToArray();
                Assert.That(
                    windowColliders,
                    Has.Length.EqualTo(
                        ExpectedWindowCount * ExpectedWindowColliderCount));
                Assert.That(
                    wallVisualRoot.GetComponentsInChildren<Collider>(true),
                    Is.EquivalentTo(windowColliders),
                    "Only the authored window frames may own visual colliders; Wall and Pilar visuals must remain collision-free.");
                foreach (var window in windows)
                {
                    Assert.That(
                        window.GetComponentsInChildren<BoxCollider>(true),
                        Has.Length.EqualTo(ExpectedWindowColliderCount),
                        window.name);
                }

                foreach (var collider in windowColliders)
                {
                    Assert.That(collider.gameObject.layer, Is.Zero, collider.name);
                    Assert.That(collider.enabled, Is.True, collider.name);
                    Assert.That(collider.isTrigger, Is.False, collider.name);
                    Assert.That(collider.GetComponent<Rigidbody>(), Is.Null, collider.name);
                    Assert.That(
                        collider.GetComponentInParent<BoardBoundaryWallVisual>(),
                        Is.Null,
                        collider.name);
                }

                Assert.That(
                    wallColliderRoot.GetComponentsInParent<BoardBoundaryWallVisual>(true),
                    Is.Empty,
                    "Permanent maze collision must not be filtered as a player boundary wall.");

                var colliders = wallColliderRoot
                    .GetComponentsInChildren<BoxCollider>(true);
                Assert.That(wallColliderRoot.childCount, Is.EqualTo(ExpectedColliderCount));
                Assert.That(colliders, Has.Length.EqualTo(ExpectedColliderCount));

                foreach (var collider in colliders)
                {
                    Assert.That(collider.transform.parent, Is.SameAs(wallColliderRoot));
                    Assert.That(collider.gameObject.activeSelf, Is.True, collider.name);
                    Assert.That(collider.gameObject.activeInHierarchy, Is.True, collider.name);
                    Assert.That(collider.gameObject.layer, Is.Zero, collider.name);
                    Assert.That(
                        Physics.DefaultRaycastLayers &
                        (1 << collider.gameObject.layer),
                        Is.Not.Zero,
                        $"{collider.name} must block the shared LOS raycast mask.");
                    Assert.That(collider.enabled, Is.True, collider.name);
                    Assert.That(collider.isTrigger, Is.False, collider.name);
                    Assert.That(collider.GetComponent<Rigidbody>(), Is.Null, collider.name);
                    Assert.That(collider.GetComponent<Renderer>(), Is.Null, collider.name);
                    Assert.That(
                        collider.GetComponent<BoardBoundaryWallVisual>(),
                        Is.Null,
                        collider.name);
                    Assert.That(
                        collider.GetComponents<Component>(),
                        Has.Length.EqualTo(2),
                        collider.name);
                    Assert.That(collider.transform.localPosition.y, Is.Zero.Within(Tolerance));
                    Assert.That(
                        Quaternion.Angle(collider.transform.localRotation, Quaternion.identity),
                        Is.Zero.Within(Tolerance),
                        collider.name);
                    Assert.That(collider.transform.localScale, Is.EqualTo(Vector3.one));
                    Assert.That(collider.center.x, Is.Zero.Within(Tolerance), collider.name);
                    Assert.That(
                        collider.center.y,
                        Is.EqualTo(ColliderCenterHeight).Within(Tolerance),
                        collider.name);
                    Assert.That(collider.center.z, Is.Zero.Within(Tolerance), collider.name);
                    Assert.That(
                        collider.size.y,
                        Is.EqualTo(ColliderHeight).Within(Tolerance),
                        collider.name);

                    var isHorizontal =
                        Mathf.Abs(collider.size.z - ColliderThickness) <= Tolerance &&
                        collider.size.x > ColliderThickness;
                    var isVertical =
                        Mathf.Abs(collider.size.x - ColliderThickness) <= Tolerance &&
                        collider.size.z > ColliderThickness;
                    Assert.That(isHorizontal ^ isVertical, Is.True, collider.name);
                }

                Assert.That(
                    colliders.Count(collider =>
                        Mathf.Abs(collider.size.z - ColliderThickness) <= Tolerance &&
                        collider.size.x > 80f),
                    Is.EqualTo(2),
                    "The north and south perimeter must each remain one merged collider.");
                Assert.That(
                    colliders.Count(collider =>
                        Mathf.Abs(collider.size.x - ColliderThickness) <= Tolerance &&
                        collider.size.z > 70f),
                    Is.EqualTo(2),
                    "The east and west perimeter must each remain one merged collider.");

                var allWallColliders = colliders
                    .Concat(windowColliders)
                    .ToArray();
                AssertTopologyBoundaries(mapRoot.Topology, allWallColliders);
                AssertWindowPhysics(instance, windows, colliders);
            }
            finally
            {
                Object.DestroyImmediate(instance);
                Physics.SyncTransforms();
            }
        }

        private static void AssertWindowPhysics(
            GameObject instance,
            IReadOnlyList<Transform> windows,
            IReadOnlyList<BoxCollider> aggregateColliders)
        {
            instance.transform.position = Vector3.up * 10000f;
            Physics.SyncTransforms();
            var grenadeRadius = PrototypeItemCatalog
                .Get(PrototypeItemId.Grenade)
                .ProjectileRadius;

            foreach (var window in windows)
            {
                var frameColliders = window
                    .GetComponentsInChildren<BoxCollider>(true);
                var sideFrames = window
                    .GetComponents<BoxCollider>()
                    .Where(collider =>
                        collider.size.x < 1f && collider.size.y > 1f)
                    .OrderBy(collider => collider.center.x)
                    .ToArray();
                Assert.That(sideFrames, Has.Length.EqualTo(2), window.name);

                var leftFrame = sideFrames[0];
                var rightFrame = sideFrames[1];
                var openingMinX = leftFrame.center.x + leftFrame.size.x * 0.5f;
                var openingMaxX = rightFrame.center.x - rightFrame.size.x * 0.5f;
                var openingMinY = Mathf.Max(
                    leftFrame.center.y - leftFrame.size.y * 0.5f,
                    rightFrame.center.y - rightFrame.size.y * 0.5f);
                var openingMaxY = Mathf.Min(
                    leftFrame.center.y + leftFrame.size.y * 0.5f,
                    rightFrame.center.y + rightFrame.size.y * 0.5f);
                Assert.That(openingMaxX - openingMinX, Is.GreaterThan(1.6f));
                Assert.That(openingMaxY - openingMinY, Is.GreaterThan(1.4f));

                var openingCenter = window.TransformPoint(new Vector3(
                    (openingMinX + openingMaxX) * 0.5f,
                    (openingMinY + openingMaxY) * 0.5f,
                    0f));
                var normal = window.TransformDirection(Vector3.forward).normalized;
                var origin = openingCenter - normal;
                const float distance = 2f;

                Assert.That(
                    Physics.Raycast(
                        origin,
                        normal,
                        distance,
                        Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore),
                    Is.False,
                    $"{window.name} must leave its sight line open.");
                Assert.That(
                    BoardItemPhysics.HasBlastLineOfSight(
                        origin,
                        openingCenter + normal),
                    Is.True,
                    $"{window.name} must leave its blast line of sight open.");
                Assert.That(
                    BoardItemPhysics.Cast(
                        origin,
                        normal,
                        distance,
                        null,
                        out _,
                        grenadeRadius),
                    Is.False,
                    $"{window.name} must leave enough clearance for a grenade.");

                foreach (var frameCollider in frameColliders)
                {
                    var frameCenter = frameCollider.transform.TransformPoint(
                        frameCollider.center);
                    Assert.That(
                        BoardItemPhysics.Cast(
                            frameCenter - normal,
                            normal,
                            distance,
                            null,
                            out var hit),
                        Is.True,
                        $"{window.name}/{frameCollider.name} must block movement and items.");
                    Assert.That(
                        hit.collider.transform.IsChildOf(window),
                        Is.True,
                        $"{window.name}/{frameCollider.name} must be blocked by its authored frame.");
                }

                Assert.That(
                    IsBlockedByAggregate(
                        window.TransformPoint(new Vector3(0.1f, 2.1f, 0f))),
                    Is.True,
                    $"{window.name} must retain wall collision on one side.");
                Assert.That(
                    IsBlockedByAggregate(
                        window.TransformPoint(new Vector3(-3.1f, 2.1f, 0f))),
                    Is.True,
                    $"{window.name} must retain wall collision on the other side.");

                bool IsBlockedByAggregate(Vector3 point)
                {
                    return aggregateColliders.Any(collider =>
                        ContainsPoint(collider, point));
                }
            }
        }

        private static void AssertTopologyBoundaries(
            BoardTopology topology,
            IReadOnlyList<BoxCollider> colliders)
        {
            Assert.That(topology, Is.Not.Null);
            topology.RebuildIndex();
            var tiles = topology.Tiles.ToDictionary(tile => tile.Coordinate);
            var openBoundaryCount = 0;
            var closedBoundaryCount = 0;

            foreach (var pair in tiles)
            {
                AssertBoundary(Vector2Int.right);
                AssertBoundary(Vector2Int.up);

                void AssertBoundary(Vector2Int offset)
                {
                    if (!tiles.TryGetValue(pair.Key + offset, out var destination))
                        return;

                    var source = pair.Value;
                    var isOpen = topology.Gates.Any(gate =>
                        gate != null &&
                        ((gate.Source == source && gate.Destination == destination) ||
                         (gate.Source == destination && gate.Destination == source)));
                    var midpoint = (source.WorldCenter + destination.WorldCenter) * 0.5f;
                    var direction = destination.WorldCenter - source.WorldCenter;
                    direction.y = 0f;
                    direction.Normalize();
                    var tangent = new Vector3(-direction.z, 0f, direction.x);

                    if (isOpen)
                    {
                        midpoint.y = ColliderCenterHeight;
                        openBoundaryCount++;
                        Assert.That(IsBlocked(midpoint), Is.False, BoundaryMessage("center"));
                        Assert.That(
                            IsBlocked(midpoint + tangent * GateProbeOffset),
                            Is.False,
                            BoundaryMessage("left clearance"));
                        Assert.That(
                            IsBlocked(midpoint - tangent * GateProbeOffset),
                            Is.False,
                            BoundaryMessage("right clearance"));
                    }
                    else
                    {
                        midpoint.y = 0.5f;
                        closedBoundaryCount++;
                        Assert.That(
                            IsBlocked(midpoint),
                            Is.True,
                            BoundaryMessage("player-height center"));
                    }

                    bool IsBlocked(Vector3 point)
                    {
                        return colliders.Any(collider => ContainsPoint(collider, point));
                    }

                    string BoundaryMessage(string probe)
                    {
                        return $"{source.Coordinate} -> {destination.Coordinate} {probe}";
                    }
                }
            }

            Assert.That(openBoundaryCount, Is.EqualTo(ExpectedOpenBoundaryCount));
            Assert.That(closedBoundaryCount, Is.EqualTo(32));
        }

        private static bool ContainsPoint(BoxCollider collider, Vector3 worldPoint)
        {
            var point = collider.transform.InverseTransformPoint(worldPoint) -
                        collider.center;
            var halfSize = collider.size * 0.5f;
            return Mathf.Abs(point.x) <= halfSize.x + Tolerance &&
                   Mathf.Abs(point.y) <= halfSize.y + Tolerance &&
                   Mathf.Abs(point.z) <= halfSize.z + Tolerance;
        }
    }
}
