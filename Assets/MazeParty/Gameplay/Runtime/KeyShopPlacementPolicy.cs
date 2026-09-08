using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Random source injected into the key-shop policy. The authoritative server
    /// owns the source; tests can provide a deterministic implementation.
    /// </summary>
    public interface IKeyShopRandomSource
    {
        int NextIndex(int exclusiveMaximum);
    }

    public sealed class UnityKeyShopRandomSource : IKeyShopRandomSource
    {
        public static readonly UnityKeyShopRandomSource Shared = new UnityKeyShopRandomSource();

        private UnityKeyShopRandomSource()
        {
        }

        public int NextIndex(int exclusiveMaximum)
        {
            if (exclusiveMaximum <= 0)
                throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));

            return UnityEngine.Random.Range(0, exclusiveMaximum);
        }
    }

    /// <summary>
    /// Stateless selection rules shared by the turn-two spawn and every
    /// post-purchase relocation. Presentation and networking deliberately live
    /// outside this policy.
    /// </summary>
    public static class KeyShopPlacementPolicy
    {
        public static List<BoardTile> BuildCandidates(
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyCollection<Vector2Int> occupiedCoordinates,
            bool excludePreviousLocation = false,
            Vector2Int previousLocation = default)
        {
            if (tiles == null)
                throw new ArgumentNullException(nameof(tiles));

            var occupied = occupiedCoordinates != null
                ? new HashSet<Vector2Int>(occupiedCoordinates)
                : new HashSet<Vector2Int>();
            var seenCoordinates = new HashSet<Vector2Int>();
            var candidates = new List<BoardTile>();

            for (var i = 0; i < tiles.Count; i++)
            {
                var tile = tiles[i];
                if (tile == null || tile.TileType != BoardTileType.Normal)
                    continue;
                if (!seenCoordinates.Add(tile.Coordinate))
                    continue;
                if (occupied.Contains(tile.Coordinate))
                    continue;
                if (excludePreviousLocation && tile.Coordinate == previousLocation)
                    continue;

                candidates.Add(tile);
            }

            return candidates;
        }

        public static bool TryChoose(
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyCollection<Vector2Int> occupiedCoordinates,
            IKeyShopRandomSource randomSource,
            out BoardTile selectedTile,
            bool excludePreviousLocation = false,
            Vector2Int previousLocation = default)
        {
            var candidates = BuildCandidates(
                tiles,
                occupiedCoordinates,
                excludePreviousLocation,
                previousLocation);
            return TryChooseCandidate(candidates, randomSource, out selectedTile);
        }

        public static bool TryChooseCandidate(
            IReadOnlyList<BoardTile> candidates,
            IKeyShopRandomSource randomSource,
            out BoardTile selectedTile)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));

            selectedTile = null;
            if (candidates.Count == 0)
                return false;
            if (randomSource == null)
                throw new ArgumentNullException(nameof(randomSource));

            var index = randomSource.NextIndex(candidates.Count);
            if (index < 0 || index >= candidates.Count)
            {
                throw new InvalidOperationException(
                    "The key-shop random source returned an index outside the candidate range.");
            }

            selectedTile = candidates[index];
            return selectedTile != null;
        }
    }
}
