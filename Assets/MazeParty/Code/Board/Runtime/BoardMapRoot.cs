using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Authored roots belonging to one map. EnvironmentRoot is intentionally not
    /// rebuilt by runtime code or editor synchronization so level art stays intact.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardMapRoot : MonoBehaviour
    {
        [SerializeField] private BoardMapDefinition definition;
        [SerializeField] private BoardTopology topology;
        [SerializeField] private Transform tilesRoot;
        [SerializeField] private Transform connectionsRoot;
        [SerializeField] private Transform environmentRoot;
        [SerializeField] private BoardTile startTile;
        [SerializeField] private BoardTile[] playerStartTiles = new BoardTile[PlayerSlotRules.Count];
        [SerializeField] private Transform[] playerSpawnAnchors = new Transform[PlayerSlotRules.Count];

        public BoardMapDefinition Definition => definition;
        public BoardTopology Topology => topology;
        public Transform TilesRoot => tilesRoot;
        public Transform ConnectionsRoot => connectionsRoot;
        public Transform EnvironmentRoot => environmentRoot;
        public BoardTile StartTile => startTile;
        public IReadOnlyList<BoardTile> PlayerStartTiles =>
            playerStartTiles ?? Array.Empty<BoardTile>();
        public IReadOnlyList<Transform> PlayerSpawnAnchors =>
            playerSpawnAnchors ?? Array.Empty<Transform>();

        public bool HasAuthoringRoots => topology != null &&
                                         tilesRoot != null &&
                                         connectionsRoot != null &&
                                         environmentRoot != null;

        public void Configure(
            BoardMapDefinition mapDefinition,
            BoardTopology boardTopology,
            Transform authoredTilesRoot,
            Transform authoredConnectionsRoot,
            Transform authoredEnvironmentRoot,
            BoardTile authoredStartTile,
            Transform[] spawnAnchors)
        {
            Configure(
                mapDefinition,
                boardTopology,
                authoredTilesRoot,
                authoredConnectionsRoot,
                authoredEnvironmentRoot,
                authoredStartTile,
                null,
                spawnAnchors);
        }

        public void Configure(
            BoardMapDefinition mapDefinition,
            BoardTopology boardTopology,
            Transform authoredTilesRoot,
            Transform authoredConnectionsRoot,
            Transform authoredEnvironmentRoot,
            BoardTile authoredStartTile,
            BoardTile[] slotStartTiles,
            Transform[] spawnAnchors)
        {
            definition = mapDefinition;
            topology = boardTopology;
            tilesRoot = authoredTilesRoot;
            connectionsRoot = authoredConnectionsRoot;
            environmentRoot = authoredEnvironmentRoot;
            startTile = authoredStartTile;
            playerStartTiles = CopySlots(slotStartTiles);
            playerSpawnAnchors = CopySlots(spawnAnchors);
        }

        public BoardTile GetStartTile(int playerSlot)
        {
            PlayerSlotRules.Validate(playerSlot, nameof(playerSlot));
            return playerStartTiles != null &&
                   playerStartTiles.Length > playerSlot &&
                   playerStartTiles[playerSlot] != null
                ? playerStartTiles[playerSlot]
                : startTile;
        }

        public Transform GetSpawnAnchor(int playerSlot)
        {
            PlayerSlotRules.Validate(playerSlot, nameof(playerSlot));
            return playerSpawnAnchors != null &&
                   playerSpawnAnchors.Length > playerSlot
                ? playerSpawnAnchors[playerSlot]
                : null;
        }

        public bool TryGetStartAssignment(
            int playerSlot,
            out BoardTile tile,
            out Transform spawnAnchor)
        {
            if (!PlayerSlotRules.IsValid(playerSlot))
            {
                tile = null;
                spawnAnchor = null;
                return false;
            }

            tile = GetStartTile(playerSlot);
            spawnAnchor = GetSpawnAnchor(playerSlot);
            return tile != null && spawnAnchor != null;
        }

        private static T[] CopySlots<T>(T[] source)
        {
            var result = new T[PlayerSlotRules.Count];
            if (source != null)
            {
                Array.Copy(source, result, Mathf.Min(source.Length, result.Length));
            }

            return result;
        }
    }
}
