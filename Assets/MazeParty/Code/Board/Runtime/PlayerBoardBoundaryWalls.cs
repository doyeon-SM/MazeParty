using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Owns a reusable wall pool for one player slot. One active wall is placed
    /// at each unique gate portal connected to the player's logical tile.
    /// Each wall collides only with its owning player's colliders, so private
    /// movement boundaries never interfere with other players.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerBoardBoundaryWalls : MonoBehaviour
    {
        public const int MaxPlayerSlots = 4;
        // Retained as the legacy cardinal prewarm size and source compatibility.
        public const int WallsPerSlot = BoardBoundaryWallPolicy.SideCount;

        private static readonly List<PlayerBoardBoundaryWalls> ActiveSystems =
            new List<PlayerBoardBoundaryWalls>(MaxPlayerSlots);

        [SerializeField, Range(-1, MaxPlayerSlots - 1)] private int playerSlot = -1;
        [SerializeField] private CharacterController ownerController;
        [SerializeField] private BoardTopology topology;
        [SerializeField, Min(0.02f)] private float wallThickness = 0.16f;
        [SerializeField, Min(0.25f)] private float wallHeight = 2.5f;

        [SerializeField] private BoardWorldPrefabs worldPrefabs;

        private readonly List<WallRuntime> _walls =
            new List<WallRuntime>(WallsPerSlot);
        private readonly List<BoardBoundaryPortal> _portals =
            new List<BoardBoundaryPortal>(WallsPerSlot);
        private readonly List<Vector2Int> _connectedCoordinates =
            new List<Vector2Int>(WallsPerSlot);
        private readonly List<Vector2Int> _outgoingCoordinates =
            new List<Vector2Int>(WallsPerSlot);
        private GameObject _wallRoot;
        private Collider[] _ownerColliders = Array.Empty<Collider>();
        private BoardTile _currentTile;
        private BoardBoundaryWallLayout _currentLayout;
        private bool _registered;
        private bool _presentationVisible = true;

        public int PlayerSlot => playerSlot;
        public int WallCount => _walls.Count;
        public int ActivePortalCount => _portals.Count;
        public BoardTile CurrentTile => _currentTile;
        public BoardBoundaryWallLayout CurrentLayout => _currentLayout;
        public IReadOnlyList<BoardBoundaryPortal> CurrentPortals => _portals;
        public bool PresentationVisible => _presentationVisible;
        public BoardWorldPrefabs WorldPrefabs => worldPrefabs;

        public void ConfigureWorldPrefabs(BoardWorldPrefabs assets)
        {
            worldPrefabs = assets != null ? assets : throw new ArgumentNullException(nameof(assets));
        }

        public void Configure(
            int slot,
            CharacterController owner,
            BoardTopology boardTopology)
        {
            if (slot < 0 || slot >= MaxPlayerSlots)
                throw new ArgumentOutOfRangeException(nameof(slot));
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            Unregister();
            playerSlot = slot;
            ownerController = owner;
            topology = boardTopology;
            CacheOwnerColliders();
            EnsureWalls();
            RenameWalls();
            Register();
            Hide();
        }

        /// <summary>
        /// Repositions the existing wall set around the supplied logical tile and
        /// updates passability from that tile's directed exits and remaining moves.
        /// </summary>
        public void Refresh(BoardTile logicalTile, int remainingMoves)
        {
            EnsureWalls();
            _currentTile = logicalTile;

            if (logicalTile == null)
            {
                Hide();
                return;
            }

            _connectedCoordinates.Clear();
            _outgoingCoordinates.Clear();
            BoardBoundaryWallPolicy.EvaluatePortals(
                topology,
                logicalTile,
                remainingMoves,
                _portals);
            for (var i = 0; i < _portals.Count; i++)
            {
                var portal = _portals[i];
                if (portal.ConnectedTile == null)
                {
                    continue;
                }

                _connectedCoordinates.Add(portal.ConnectedTile.Coordinate);
                if (portal.HasOutgoingGate)
                {
                    _outgoingCoordinates.Add(portal.ConnectedTile.Coordinate);
                }
            }

            _currentLayout = BoardBoundaryWallPolicy.Evaluate(
                logicalTile.Coordinate,
                _connectedCoordinates,
                _outgoingCoordinates,
                remainingMoves);
            PlaceWalls(logicalTile);
        }

        public void Hide()
        {
            _currentTile = null;
            _currentLayout = default;
            _portals.Clear();
            if (_wallRoot != null)
                _wallRoot.SetActive(false);
        }

        /// <summary>
        /// Controls only the wall renderers. Physical blocking remains active so a
        /// server-hosted remote avatar can keep its private boundary simulation
        /// without showing that player's walls to the host.
        /// </summary>
        public void SetPresentationVisible(bool visible)
        {
            _presentationVisible = visible;
            EnsureWalls();
            ApplyPresentationVisibility();
        }

        public bool TryGetWall(
            BoardBoundarySide side,
            out GameObject wallObject,
            out BoxCollider wallCollider)
        {
            for (var index = 0; index < _portals.Count; index++)
            {
                if (!TryGetLegacySide(_portals[index], out var portalSide) ||
                    portalSide != side)
                {
                    continue;
                }

                wallObject = _walls[index].GameObject;
                wallCollider = _walls[index].Collider;
                return wallObject != null && wallCollider != null;
            }

            wallObject = null;
            wallCollider = null;
            return false;
        }

        public bool TryGetPortalWall(
            int portalIndex,
            out GameObject wallObject,
            out BoxCollider wallCollider)
        {
            if (portalIndex < 0 || portalIndex >= _portals.Count ||
                portalIndex >= _walls.Count)
            {
                wallObject = null;
                wallCollider = null;
                return false;
            }

            wallObject = _walls[portalIndex].GameObject;
            wallCollider = _walls[portalIndex].Collider;
            return wallObject != null && wallCollider != null;
        }

        private void Awake()
        {
            if (ownerController == null)
                ownerController = GetComponent<CharacterController>();

            CacheOwnerColliders();
            EnsureWalls();
        }

        private void OnEnable()
        {
            Register();
            if (_wallRoot != null)
                _wallRoot.SetActive(_currentTile != null);
        }

        private void OnDisable()
        {
            Unregister();
            if (_wallRoot != null)
                _wallRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            Unregister();
            if (_wallRoot == null)
                return;

            if (Application.isPlaying)
                Destroy(_wallRoot);
            else
                DestroyImmediate(_wallRoot);
            _wallRoot = null;
        }

        private void EnsureWalls()
        {
            if (_wallRoot == null)
            {
                if (worldPrefabs == null)
                    worldPrefabs = BoardWorldPrefabs.LoadRequired();

                _wallRoot = new GameObject(GetRootName())
                {
                    hideFlags = HideFlags.DontSave
                };
                _wallRoot.transform.SetPositionAndRotation(
                    Vector3.zero,
                    Quaternion.identity);

                var ownerScene = gameObject.scene;
                if (ownerScene.IsValid() && ownerScene.isLoaded)
                    SceneManager.MoveGameObjectToScene(_wallRoot, ownerScene);
            }

            EnsureWallPoolCapacity(WallsPerSlot);
            ApplyPresentationVisibility();
            if (_currentTile == null)
                _wallRoot.SetActive(false);
        }

        private void EnsureWallPoolCapacity(int requiredCount)
        {
            if (_wallRoot == null)
                return;

            while (_walls.Count < requiredCount)
            {
                var visual = Instantiate(worldPrefabs.BoundaryWall, _wallRoot.transform, false);
                var wall = visual.gameObject;
                wall.name = "P" + (playerSlot + 1) + " Boundary Portal Pool " +
                            (_walls.Count + 1);
                wall.hideFlags = HideFlags.DontSave;
                var wallCollider = visual.BlockingCollider;
                wallCollider.enabled = false;
                visual.SetVisible(false);
                wall.SetActive(false);
                _walls.Add(new WallRuntime(wall, wallCollider, visual));
            }

            RefreshCollisionIsolationForAllSystems();
        }

        private void PlaceWalls(BoardTile tile)
        {
            EnsureWallPoolCapacity(Mathf.Max(WallsPerSlot, _portals.Count));
            _wallRoot.name = GetRootName();
            _wallRoot.SetActive(true);

            for (var i = 0; i < _walls.Count; i++)
            {
                var pooledWall = _walls[i];
                pooledWall.Collider.enabled = false;
                pooledWall.Visual.SetVisible(false);
                pooledWall.GameObject.SetActive(false);
            }

            var up = tile.transform.up.normalized;
            var thickness = Mathf.Max(0.02f, wallThickness);
            var height = Mathf.Max(0.25f, wallHeight);
            for (var i = 0; i < _portals.Count; i++)
            {
                var wall = _walls[i];
                var portal = _portals[i];
                var outward = portal.OutwardNormal -
                              up * Vector3.Dot(portal.OutwardNormal, up);
                if (outward.sqrMagnitude <= 0.0001f && portal.ConnectedTile != null)
                {
                    outward = portal.ConnectedTile.WorldCenter - tile.WorldCenter;
                    outward -= up * Vector3.Dot(outward, up);
                }

                if (outward.sqrMagnitude <= 0.0001f)
                {
                    outward = tile.transform.forward;
                }
                outward.Normalize();

                wall.Transform.SetPositionAndRotation(
                    portal.PlanePoint + outward * (thickness * 0.5f) +
                    up * (height * 0.5f),
                    Quaternion.LookRotation(outward, up));
                wall.Transform.localScale = new Vector3(
                    portal.Width + thickness * 2f,
                    height,
                    thickness);
                wall.GameObject.name = "P" + (playerSlot + 1) +
                                       " Boundary Portal " + (i + 1);
                wall.GameObject.SetActive(true);

                wall.Visual.SetPassable(portal.IsPassable);

                wall.Visual.SetVisible(_presentationVisible);
            }

            RefreshCollisionIsolationForAllSystems();
        }

        private void ApplyPresentationVisibility()
        {
            for (var i = 0; i < _walls.Count; i++)
            {
                var wall = _walls[i];
                if (wall != null)
                {
                    wall.Visual.SetVisible(
                        _presentationVisible && i < _portals.Count &&
                        wall.GameObject.activeSelf);
                }
            }
        }

        private void CacheOwnerColliders()
        {
            _ownerColliders = ownerController != null
                ? ownerController.GetComponentsInChildren<Collider>(true)
                : Array.Empty<Collider>();
        }

        private void Register()
        {
            if (_registered || !isActiveAndEnabled || ownerController == null ||
                playerSlot < 0 || playerSlot >= MaxPlayerSlots)
            {
                return;
            }

            for (var i = ActiveSystems.Count - 1; i >= 0; i--)
            {
                var other = ActiveSystems[i];
                if (other == null)
                {
                    ActiveSystems.RemoveAt(i);
                    continue;
                }
                if (other != this && other.playerSlot == playerSlot)
                {
                    Debug.LogError(
                        "A boundary wall system is already registered for player slot " + playerSlot + ".",
                        this);
                }
            }

            ActiveSystems.Add(this);
            _registered = true;
            RefreshCollisionIsolationForAllSystems();
        }

        private void Unregister()
        {
            if (!_registered)
                return;

            ActiveSystems.Remove(this);
            _registered = false;
            RefreshCollisionIsolationForAllSystems();
        }

        private static void RefreshCollisionIsolationForAllSystems()
        {
            for (var wallOwnerIndex = ActiveSystems.Count - 1; wallOwnerIndex >= 0; wallOwnerIndex--)
            {
                var wallOwner = ActiveSystems[wallOwnerIndex];
                if (wallOwner == null)
                {
                    ActiveSystems.RemoveAt(wallOwnerIndex);
                    continue;
                }

                for (var playerIndex = 0; playerIndex < ActiveSystems.Count; playerIndex++)
                {
                    var player = ActiveSystems[playerIndex];
                    if (player == null)
                        continue;

                    var ignore = wallOwner != player;
                    for (var wallIndex = 0; wallIndex < wallOwner._walls.Count; wallIndex++)
                    {
                        var wall = wallOwner._walls[wallIndex];
                        if (wall == null || wall.Collider == null)
                            continue;

                        for (var colliderIndex = 0; colliderIndex < player._ownerColliders.Length; colliderIndex++)
                        {
                            var playerCollider = player._ownerColliders[colliderIndex];
                            if (playerCollider != null && playerCollider != wall.Collider)
                                Physics.IgnoreCollision(wall.Collider, playerCollider, ignore);
                        }
                    }
                }
            }
        }

        private string GetRootName()
        {
            return playerSlot >= 0
                ? "Player " + (playerSlot + 1) + " Boundary Walls (Runtime)"
                : "Unassigned Player Boundary Walls (Runtime)";
        }

        private void RenameWalls()
        {
            if (_wallRoot == null)
                return;

            _wallRoot.name = GetRootName();
            for (var i = 0; i < _walls.Count; i++)
            {
                if (_walls[i] != null && _walls[i].GameObject != null)
                {
                    _walls[i].GameObject.name = "P" + (playerSlot + 1) +
                                                " Boundary Portal Pool " + (i + 1);
                }
            }
        }

        private bool TryGetLegacySide(
            BoardBoundaryPortal portal,
            out BoardBoundarySide side)
        {
            if (_currentTile != null && portal.ConnectedTile != null &&
                BoardBoundaryWallPolicy.TryGetSide(
                    _currentTile.Coordinate,
                    portal.ConnectedTile.Coordinate,
                    out side))
            {
                return true;
            }

            if (_currentTile == null)
            {
                side = default;
                return false;
            }

            var local = _currentTile.transform.InverseTransformDirection(
                portal.OutwardNormal).normalized;
            const float cardinalThreshold = 0.95f;
            if (local.z >= cardinalThreshold)
                side = BoardBoundarySide.North;
            else if (local.x >= cardinalThreshold)
                side = BoardBoundarySide.East;
            else if (local.z <= -cardinalThreshold)
                side = BoardBoundarySide.South;
            else if (local.x <= -cardinalThreshold)
                side = BoardBoundarySide.West;
            else
            {
                side = default;
                return false;
            }

            return true;
        }

        private sealed class WallRuntime
        {
            public WallRuntime(GameObject gameObject, BoxCollider collider, BoardBoundaryWallVisual visual)
            {
                GameObject = gameObject;
                Transform = gameObject.transform;
                Collider = collider;
                Visual = visual;
            }

            public BoardBoundaryWallVisual Visual { get; }
            public GameObject GameObject { get; }
            public Transform Transform { get; }
            public BoxCollider Collider { get; }

        }
    }
}
