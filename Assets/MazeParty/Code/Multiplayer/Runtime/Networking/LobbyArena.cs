using System;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Defines the small shared waiting room used before the board scene loads.
    /// Movement remains server authoritative; this component only supplies stable
    /// spawn positions and horizontal room bounds.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LobbyArena : MonoBehaviour
    {
        private const string SurfaceMaterialResource =
            "MazeParty/Materials/LobbySurface";

        [SerializeField] private Vector3 roomCenter = new Vector3(3.5f, 0f, 0f);
        [SerializeField] private Vector2 innerSize = new Vector2(12f, 8f);
        [SerializeField] private Vector3[] spawnPositions = Array.Empty<Vector3>();

        public static LobbyArena Instance { get; private set; }

        public Vector3 RoomCenter => roomCenter;
        public Vector2 InnerSize => innerSize;
        public int SpawnCount => spawnPositions != null ? spawnPositions.Length : 0;

        public static LobbyArena EnsureRuntimeCreated(Camera lobbyCamera)
        {
            var arena = Instance;
            if (arena == null)
            {
                arena = FindAnyObjectByType<LobbyArena>(FindObjectsInactive.Include);
                if (arena == null)
                {
                    var root = new GameObject("Lobby Waiting Room");
                    arena = root.AddComponent<LobbyArena>();
                    var center = new Vector3(3.5f, 0f, 0f);
                    arena.Configure(
                        center,
                        new Vector2(12f, 8f),
                        new[]
                        {
                            center + new Vector3(-2.7f, 1f, -1.8f),
                            center + new Vector3(2.7f, 1f, -1.8f),
                            center + new Vector3(-2.7f, 1f, 1.8f),
                            center + new Vector3(2.7f, 1f, 1.8f)
                        });
                    arena.BuildRoomVisuals();
                }

                // EditMode construction does not guarantee that OnEnable has run
                // before the next line, so the factory must own this assignment.
                Instance = arena;
            }

            arena.ConfigureCamera(lobbyCamera);
            return arena;
        }

        public void Configure(Vector3 center, Vector2 size, Vector3[] spawns)
        {
            roomCenter = center;
            innerSize = new Vector2(
                Mathf.Max(2f, size.x),
                Mathf.Max(2f, size.y));
            spawnPositions = spawns != null
                ? (Vector3[])spawns.Clone()
                : Array.Empty<Vector3>();
        }

        public Vector3 GetSpawnPosition(int slot)
        {
            if (spawnPositions != null && slot >= 0 && slot < spawnPositions.Length)
            {
                return spawnPositions[slot];
            }

            var safeSlot = Mathf.Max(0, slot);
            var column = safeSlot % 2;
            var row = (safeSlot / 2) % 2;
            return roomCenter + new Vector3(
                column == 0 ? -2.5f : 2.5f,
                1f,
                row == 0 ? -1.75f : 1.75f);
        }

        public Vector3 ClampHorizontalPosition(Vector3 position, float radius)
        {
            var safeRadius = Mathf.Max(0f, radius);
            var halfWidth = Mathf.Max(0.1f, innerSize.x * 0.5f - safeRadius);
            var halfDepth = Mathf.Max(0.1f, innerSize.y * 0.5f - safeRadius);
            position.x = Mathf.Clamp(
                position.x,
                roomCenter.x - halfWidth,
                roomCenter.x + halfWidth);
            position.z = Mathf.Clamp(
                position.z,
                roomCenter.z - halfDepth,
                roomCenter.z + halfDepth);
            return position;
        }

        private void BuildRoomVisuals()
        {
            if (transform.Find("Floor") != null)
            {
                return;
            }

            var material = Resources.Load<Material>(SurfaceMaterialResource);
            if (material == null)
            {
                Debug.LogError(
                    "Lobby surface material is missing from Resources: " +
                    SurfaceMaterialResource,
                    this);
            }

            CreateRoomPart(
                "Floor",
                roomCenter + Vector3.down * 0.25f,
                new Vector3(innerSize.x + 1f, 0.5f, innerSize.y + 1f),
                material);

            const float thickness = 0.5f;
            const float height = 0.8f;
            CreateRoomPart(
                "North Wall",
                roomCenter + new Vector3(0f, height * 0.5f, innerSize.y * 0.5f + thickness * 0.5f),
                new Vector3(innerSize.x + thickness * 2f, height, thickness),
                material);
            CreateRoomPart(
                "South Wall",
                roomCenter + new Vector3(0f, height * 0.5f, -innerSize.y * 0.5f - thickness * 0.5f),
                new Vector3(innerSize.x + thickness * 2f, height, thickness),
                material);
            CreateRoomPart(
                "East Wall",
                roomCenter + new Vector3(innerSize.x * 0.5f + thickness * 0.5f, height * 0.5f, 0f),
                new Vector3(thickness, height, innerSize.y),
                material);
            CreateRoomPart(
                "West Wall",
                roomCenter + new Vector3(-innerSize.x * 0.5f - thickness * 0.5f, height * 0.5f, 0f),
                new Vector3(thickness, height, innerSize.y),
                material);
        }

        private void CreateRoomPart(
            string partName,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            var value = GameObject.CreatePrimitive(PrimitiveType.Cube);
            value.name = partName;
            value.transform.SetParent(transform, false);
            value.transform.position = position;
            value.transform.localScale = scale;
            if (material != null)
            {
                value.GetComponent<MeshRenderer>().sharedMaterial = material;
            }
        }

        private void ConfigureCamera(Camera lobbyCamera)
        {
            if (lobbyCamera == null)
            {
                return;
            }

            lobbyCamera.fieldOfView = 48f;
            lobbyCamera.transform.position = new Vector3(3.5f, 11.5f, -10.5f);
            lobbyCamera.transform.LookAt(new Vector3(3.5f, 0.65f, 0f));
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogError("Only one LobbyArena may be active at a time.", this);
                enabled = false;
                return;
            }

            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }
    }
}
