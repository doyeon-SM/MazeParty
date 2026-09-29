using System;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Stable identity replicated by the server for one Board scene lifetime.
    /// Empty id plus version zero deliberately identifies the authored legacy board.
    /// </summary>
    public readonly struct BoardMapSelection : IEquatable<BoardMapSelection>
    {
        // Board map ids are ASCII kebab-case and are replicated through
        // FixedString64Bytes, whose UTF-8 payload capacity is 61 bytes.
        public const int MaximumMapIdLength = 61;

        public static readonly BoardMapSelection Legacy =
            new BoardMapSelection(string.Empty, 0);

        public BoardMapSelection(string mapId, int contentVersion)
        {
            MapId = mapId ?? string.Empty;
            ContentVersion = contentVersion;
        }

        public string MapId { get; }
        public int ContentVersion { get; }
        public bool IsLegacy => string.IsNullOrEmpty(MapId) && ContentVersion == 0;

        public static bool TryCreate(
            string mapId,
            int contentVersion,
            out BoardMapSelection selection)
        {
            mapId = mapId?.Trim() ?? string.Empty;
            if (mapId.Length == 0)
            {
                selection = Legacy;
                return contentVersion == 0;
            }

            if (mapId.Length > MaximumMapIdLength ||
                !BoardMapDefinition.IsValidMapId(mapId) ||
                contentVersion < 1)
            {
                selection = default;
                return false;
            }

            selection = new BoardMapSelection(mapId, contentVersion);
            return true;
        }

        public static BoardMapSelection FromDefinition(BoardMapDefinition definition)
        {
            if (definition == null ||
                !TryCreate(
                    definition.MapId,
                    definition.ContentVersion,
                    out var selection))
            {
                throw new ArgumentException(
                    "A valid board map definition is required.",
                    nameof(definition));
            }

            return selection;
        }

        public bool Equals(BoardMapSelection other)
        {
            return ContentVersion == other.ContentVersion &&
                   string.Equals(MapId, other.MapId, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is BoardMapSelection other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((MapId != null ? MapId.GetHashCode() : 0) * 397) ^
                       ContentVersion;
            }
        }

        public static bool operator ==(
            BoardMapSelection left,
            BoardMapSelection right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(
            BoardMapSelection left,
            BoardMapSelection right)
        {
            return !left.Equals(right);
        }

        public override string ToString()
        {
            return IsLegacy ? "legacy" : $"{MapId}@{ContentVersion}";
        }
    }
}
