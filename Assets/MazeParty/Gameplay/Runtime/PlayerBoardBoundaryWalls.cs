using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Owns exactly four reusable N/E/S/W walls for one player slot.
    /// Each wall collides only with its owning player's colliders, so the four
    /// slot-specific sets (16 walls total) never interfere with other players.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerBoardBoundaryWalls : MonoBehaviour
    {
        public const int MaxPlayerSlots = 4;
        public const int WallsPerSlot = BoardBoundaryWallPolicy.SideCount;

        private static readonly List<PlayerBoardBoundaryWalls> ActiveSystems =
            new List<PlayerBoardBoundaryWalls>(MaxPlayerSlots);

        [SerializeField, Range(-1, MaxPlayerSlots - 1)] private int playerSlot = -1;
        [SerializeField] private CharacterController ownerController;
        [SerializeField] private BoardTopology topology;
        [SerializeField, Min(0.02f)] private float wallThickness = 0.16f;
        [SerializeField, Min(0.25f)] private float wallHeight = 2.5f;
        [SerializeField] private Material wallMaterial;
        [SerializeField] private Color passableColor = new Color(0.04f, 0.32f, 1f, 0.72f);
        [SerializeField] private Color blockedColor = new Color(0.005f, 0.008f, 0.012f, 1f);

        private readonly WallRuntime[] _walls = new WallRuntime[WallsPerSlot];
        private readonly List<Vector2Int> _outgoingCoordinates = new List<Vector2Int>(2);
        private GameObject _wallRoot;
        private Collider[] _ownerColliders = Array.Empty<Collider>();
        private BoardTile _currentTile;
        private BoardBoundaryWallLayout _currentLayout;
        private bool _registered;
        private bool _presentationVisible = true;

        public int PlayerSlot => playerSlot;
        public int WallCount => _wallRoot != null ? WallsPerSlot : 0;
        public BoardTile CurrentTile => _currentTile;
        public BoardBoundaryWallLayout CurrentLayout => _currentLayout;
        public bool PresentationVisible => _presentationVisible;
        public Material WallMaterial => wallMaterial;

        public void ConfigureVisualMaterial(Material material)
        {
            wallMaterial = material;
            for (var index = 0; index < _walls.Length; index++)
            {
                var wall = _walls[index];
                if (wall != null && wall.Renderer != null && wallMaterial != null)
                {
                    wall.Renderer.sharedMaterial = wallMaterial;
                }
            }
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

            _outgoingCoordinates.Clear();
            if (topology != null)
            {
                var outgoing = topology.GetOutgoingGates(logicalTile);
                for (var i = 0; i < outgoing.Count; i++)
                {
                    var gate = outgoing[i];
                    if (gate != null && gate.Destination != null)
                        _outgoingCoordinates.Add(gate.Destination.Coordinate);
                }
            }

            _currentLayout = BoardBoundaryWallPolicy.Evaluate(
                logicalTile.Coordinate,
                _outgoingCoordinates,
                remainingMoves);
            PlaceWalls(logicalTile, _currentLayout);
        }

        public void Hide()
        {
            _currentTile = null;
            _currentLayout = default;
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
            var index = (int)side;
            if (index < 0 || index >= _walls.Length || _walls[index] == null)
            {
                wallObject = null;
                wallCollider = null;
                return false;
            }

            wallObject = _walls[index].GameObject;
            wallCollider = _walls[index].Collider;
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
            if (_wallRoot != null)
                return;

            if (wallMaterial == null)
            {
                wallMaterial = Resources.Load<Material>(
                    "MazeParty/Materials/LobbySurface");
            }

            _wallRoot = new GameObject(GetRootName())
            {
                hideFlags = HideFlags.DontSave
            };
            _wallRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);

            var ownerScene = gameObject.scene;
            if (ownerScene.IsValid() && ownerScene.isLoaded)
                SceneManager.MoveGameObjectToScene(_wallRoot, ownerScene);

            for (var i = 0; i < WallsPerSlot; i++)
            {
                var side = (BoardBoundarySide)i;
                var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
                wall.name = "P" + (playerSlot + 1) + " Boundary " + side;
                wall.hideFlags = HideFlags.DontSave;
                wall.transform.SetParent(_wallRoot.transform, false);

                var wallCollider = wall.GetComponent<BoxCollider>();
                wallCollider.isTrigger = false;
                var wallRenderer = wall.GetComponent<MeshRenderer>();
                if (wallMaterial != null)
                {
                    // A serialized URP material keeps the shader in standalone
                    // builds. Runtime primitive defaults can be stripped and render
                    // magenta even though their property block contains a color.
                    wallRenderer.sharedMaterial = wallMaterial;
                }
                wallRenderer.shadowCastingMode = ShadowCastingMode.Off;
                wallRenderer.receiveShadows = false;
                _walls[i] = new WallRuntime(wall, wallCollider, wallRenderer);
            }

            ApplyPresentationVisibility();
            _wallRoot.SetActive(false);
            RefreshCollisionIsolationForAllSystems();
        }

        private void PlaceWalls(BoardTile tile, BoardBoundaryWallLayout layout)
        {
            _wallRoot.name = GetRootName();
            _wallRoot.SetActive(true);

            var up = tile.transform.up.normalized;
            for (var i = 0; i < _walls.Length; i++)
            {
                var side = (BoardBoundarySide)i;
                var wall = _walls[i];
                var hasConnectedPath = layout.HasExit(side);
                wall.GameObject.SetActive(hasConnectedPath);
                if (!hasConnectedPath)
                {
                    wall.Collider.enabled = false;
                    wall.Renderer.enabled = false;
                    continue;
                }

                var localNormal = GetLocalNormal(side);
                var worldNormal = tile.transform.TransformDirection(localNormal).normalized;
                var thickness = Mathf.Max(0.02f, wallThickness);
                var height = Mathf.Max(0.25f, wallHeight);

                wall.Transform.SetPositionAndRotation(
                    tile.WorldCenter + worldNormal * (BoardTile.HalfRoomSize + thickness * 0.5f) +
                    up * (height * 0.5f),
                    tile.transform.rotation);
                wall.Transform.localScale = side == BoardBoundarySide.North || side == BoardBoundarySide.South
                    ? new Vector3(BoardTile.RoomSize + thickness * 2f, height, thickness)
                    : new Vector3(thickness, height, BoardTile.RoomSize + thickness * 2f);

                var passable = layout.IsPassable(side);
                wall.Collider.enabled = !passable;
                ApplyColor(wall.Renderer, passable ? passableColor : blockedColor);
                wall.Renderer.enabled = _presentationVisible;
            }

            RefreshCollisionIsolationForAllSystems();
        }

        private void ApplyPresentationVisibility()
        {
            for (var i = 0; i < _walls.Length; i++)
            {
                var wall = _walls[i];
                if (wall != null && wall.Renderer != null)
                    wall.Renderer.enabled = _presentationVisible;
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
                    for (var wallIndex = 0; wallIndex < wallOwner._walls.Length; wallIndex++)
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
            for (var i = 0; i < _walls.Length; i++)
            {
                if (_walls[i] != null && _walls[i].GameObject != null)
                {
                    _walls[i].GameObject.name = "P" + (playerSlot + 1) +
                                                " Boundary " + (BoardBoundarySide)i;
                }
            }
        }

        private static Vector3 GetLocalNormal(BoardBoundarySide side)
        {
            switch (side)
            {
                case BoardBoundarySide.North: return Vector3.forward;
                case BoardBoundarySide.East: return Vector3.right;
                case BoardBoundarySide.South: return Vector3.back;
                case BoardBoundarySide.West: return Vector3.left;
                default: return Vector3.zero;
            }
        }

        private static void ApplyColor(Renderer target, Color color)
        {
            if (target == null)
                return;

            var properties = new MaterialPropertyBlock();
            target.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", color);
            properties.SetColor("_Color", color);
            target.SetPropertyBlock(properties);
        }

        private sealed class WallRuntime
        {
            public WallRuntime(GameObject gameObject, BoxCollider collider, MeshRenderer renderer)
            {
                GameObject = gameObject;
                Transform = gameObject.transform;
                Collider = collider;
                Renderer = renderer;
            }

            public GameObject GameObject { get; }
            public Transform Transform { get; }
            public BoxCollider Collider { get; }
            public MeshRenderer Renderer { get; }
        }
    }
}
