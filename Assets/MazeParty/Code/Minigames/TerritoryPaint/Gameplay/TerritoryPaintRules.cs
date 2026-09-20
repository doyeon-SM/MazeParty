using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.TerritoryPaint
{
    public static class TerritoryPaintRules
    {
        public const int PlayerCount = 4;
        public const int RoundCount = 1;
        public const double RoundSeconds = 60d;
        public const int TotalScore = 1000;
        public const int SurfaceResolution = 96;
        public const byte UnpaintedOwner = byte.MaxValue;

        public static bool IsValidPlayerSlot(int playerSlot)
        {
            return playerSlot >= 0 && playerSlot < PlayerCount;
        }

        public static int NormalizeOwnedArea(
            int ownedCellCount,
            int totalCellCount)
        {
            if (totalCellCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(totalCellCount));
            }
            if (ownedCellCount < 0 || ownedCellCount > totalCellCount)
            {
                throw new ArgumentOutOfRangeException(nameof(ownedCellCount));
            }

            return (ownedCellCount * TotalScore + totalCellCount / 2) /
                   totalCellCount;
        }

        public static int GetPointsForRank(int rank)
        {
            switch (rank)
            {
                case 1: return 3;
                case 2: return 2;
                case 3: return 1;
                case 4: return 0;
                default:
                    throw new ArgumentOutOfRangeException(nameof(rank));
            }
        }
    }

    /// <summary>
    /// Authoritative paint ownership model. Cells are an internal sampling
    /// detail only; gameplay and presentation expose a continuous circular
    /// brush over world coordinates.
    /// </summary>
    public sealed class TerritoryPaintSurface
    {
        private readonly byte[] _owners;
        private readonly int[] _ownedCounts =
            new int[TerritoryPaintRules.PlayerCount];

        public TerritoryPaintSurface(
            int resolution,
            float arenaCenterX,
            float arenaHalfExtent)
        {
            if (resolution < 8)
            {
                throw new ArgumentOutOfRangeException(nameof(resolution));
            }
            if (float.IsNaN(arenaCenterX) ||
                float.IsInfinity(arenaCenterX))
            {
                throw new ArgumentOutOfRangeException(nameof(arenaCenterX));
            }
            if (float.IsNaN(arenaHalfExtent) ||
                float.IsInfinity(arenaHalfExtent) ||
                arenaHalfExtent <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(arenaHalfExtent));
            }

            Resolution = resolution;
            ArenaCenterX = arenaCenterX;
            ArenaHalfExtent = arenaHalfExtent;
            _owners = new byte[resolution * resolution];
            Array.Fill(_owners, TerritoryPaintRules.UnpaintedOwner);
        }

        public int Resolution { get; }
        public int CellCount => _owners.Length;
        public float ArenaCenterX { get; }
        public float ArenaHalfExtent { get; }
        public float CellSize => ArenaHalfExtent * 2f / Resolution;

        public int GetOwnedCellCount(int playerSlot)
        {
            ValidatePlayerSlot(playerSlot);
            return _ownedCounts[playerSlot];
        }

        public int GetNormalizedScore(int playerSlot)
        {
            return TerritoryPaintRules.NormalizeOwnedArea(
                GetOwnedCellCount(playerSlot),
                CellCount);
        }

        public byte GetOwnerAt(int x, int y)
        {
            if (x < 0 || x >= Resolution)
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }
            if (y < 0 || y >= Resolution)
            {
                throw new ArgumentOutOfRangeException(nameof(y));
            }

            return _owners[y * Resolution + x];
        }

        public byte GetOwnerAtIndex(int index)
        {
            if (index < 0 || index >= _owners.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _owners[index];
        }

        public int PaintStroke(
            int playerSlot,
            Vector2 fromWorld,
            Vector2 toWorld,
            float brushRadius)
        {
            ValidatePlayerSlot(playerSlot);
            ValidateFinite(fromWorld, nameof(fromWorld));
            ValidateFinite(toWorld, nameof(toWorld));
            if (float.IsNaN(brushRadius) ||
                float.IsInfinity(brushRadius) ||
                brushRadius <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(brushRadius));
            }

            var distance = Vector2.Distance(fromWorld, toWorld);
            var maximumStep = Mathf.Max(
                CellSize * 0.45f,
                brushRadius * 0.3f);
            var stepCount = Mathf.Max(
                1,
                Mathf.CeilToInt(distance / maximumStep));
            var changed = 0;
            for (var step = 0; step <= stepCount; step++)
            {
                changed += PaintCircle(
                    playerSlot,
                    Vector2.Lerp(
                        fromWorld,
                        toWorld,
                        (float)step / stepCount),
                    brushRadius);
            }

            return changed;
        }

        public void CopyOwnersTo(byte[] destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }
            if (destination.Length != _owners.Length)
            {
                throw new ArgumentException(
                    "Destination length must match the paint surface.",
                    nameof(destination));
            }

            Array.Copy(_owners, destination, _owners.Length);
        }

        private int PaintCircle(
            int playerSlot,
            Vector2 worldCenter,
            float radius)
        {
            var minX = Mathf.Max(
                0,
                WorldToCellFloor(worldCenter.x - radius, true));
            var maxX = Mathf.Min(
                Resolution - 1,
                WorldToCellFloor(worldCenter.x + radius, true));
            var minY = Mathf.Max(
                0,
                WorldToCellFloor(worldCenter.y - radius, false));
            var maxY = Mathf.Min(
                Resolution - 1,
                WorldToCellFloor(worldCenter.y + radius, false));
            var radiusSquared = radius * radius;
            var changed = 0;

            for (var y = minY; y <= maxY; y++)
            {
                var sampleZ = -ArenaHalfExtent +
                              (y + 0.5f) * CellSize;
                for (var x = minX; x <= maxX; x++)
                {
                    var sampleX = ArenaCenterX - ArenaHalfExtent +
                                  (x + 0.5f) * CellSize;
                    var dx = sampleX - worldCenter.x;
                    var dz = sampleZ - worldCenter.y;
                    if (dx * dx + dz * dz > radiusSquared)
                    {
                        continue;
                    }

                    var index = y * Resolution + x;
                    var previousOwner = _owners[index];
                    if (previousOwner == playerSlot)
                    {
                        continue;
                    }

                    if (previousOwner !=
                        TerritoryPaintRules.UnpaintedOwner)
                    {
                        _ownedCounts[previousOwner]--;
                    }

                    _owners[index] = (byte)playerSlot;
                    _ownedCounts[playerSlot]++;
                    changed++;
                }
            }

            return changed;
        }

        private int WorldToCellFloor(float value, bool xAxis)
        {
            var minimum = xAxis
                ? ArenaCenterX - ArenaHalfExtent
                : -ArenaHalfExtent;
            return Mathf.FloorToInt((value - minimum) / CellSize);
        }

        private static void ValidatePlayerSlot(int playerSlot)
        {
            if (!TerritoryPaintRules.IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            }
        }

        private static void ValidateFinite(Vector2 value, string name)
        {
            if (float.IsNaN(value.x) || float.IsInfinity(value.x) ||
                float.IsNaN(value.y) || float.IsInfinity(value.y))
            {
                throw new ArgumentOutOfRangeException(name);
            }
        }
    }

    public readonly struct TerritoryPaintLeaderboardEntry
    {
        internal TerritoryPaintLeaderboardEntry(
            int playerSlot,
            int rank,
            int score,
            int ownedCellCount)
        {
            PlayerSlot = playerSlot;
            Rank = rank;
            Score = score;
            OwnedCellCount = ownedCellCount;
        }

        public int PlayerSlot { get; }
        public int Rank { get; }
        public int Score { get; }
        public int OwnedCellCount { get; }
    }

    public static class TerritoryPaintScoring
    {
        public static IReadOnlyList<TerritoryPaintLeaderboardEntry>
            BuildLeaderboard(
                IReadOnlyList<int> ownedCellCounts,
                int totalCellCount)
        {
            if (ownedCellCounts == null)
            {
                throw new ArgumentNullException(nameof(ownedCellCounts));
            }
            if (ownedCellCounts.Count != TerritoryPaintRules.PlayerCount)
            {
                throw new ArgumentException(
                    "Exactly four ownership counts are required.",
                    nameof(ownedCellCounts));
            }
            if (totalCellCount < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(totalCellCount));
            }

            var slots = new int[TerritoryPaintRules.PlayerCount];
            for (var slot = 0; slot < slots.Length; slot++)
            {
                var count = ownedCellCounts[slot];
                if (count < 0 || count > totalCellCount)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(ownedCellCounts));
                }
                slots[slot] = slot;
            }

            Array.Sort(
                slots,
                (left, right) =>
                {
                    var scoreComparison =
                        ownedCellCounts[right].CompareTo(
                            ownedCellCounts[left]);
                    return scoreComparison != 0
                        ? scoreComparison
                        : left.CompareTo(right);
                });

            var leaderboard =
                new TerritoryPaintLeaderboardEntry[
                    TerritoryPaintRules.PlayerCount];
            for (var index = 0; index < slots.Length; index++)
            {
                var slot = slots[index];
                var owned = ownedCellCounts[slot];
                leaderboard[index] =
                    new TerritoryPaintLeaderboardEntry(
                        slot,
                        index + 1,
                        TerritoryPaintRules.NormalizeOwnedArea(
                            owned,
                            totalCellCount),
                        owned);
            }

            return Array.AsReadOnly(leaderboard);
        }
    }
}
