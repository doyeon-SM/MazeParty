using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    [CreateAssetMenu(menuName = "MazeParty/Board/Map Catalog")]
    public sealed class BoardMapCatalog : ScriptableObject
    {
        [SerializeField] private BoardMapDefinition[] maps = Array.Empty<BoardMapDefinition>();

        public IReadOnlyList<BoardMapDefinition> Maps =>
            maps ?? Array.Empty<BoardMapDefinition>();

        public void Configure(BoardMapDefinition[] definitions)
        {
            maps = definitions != null
                ? (BoardMapDefinition[])definitions.Clone()
                : Array.Empty<BoardMapDefinition>();
        }

        public bool TryGetMap(string mapId, out BoardMapDefinition definition)
        {
            definition = null;
            if (!BoardMapDefinition.IsValidMapId(mapId))
            {
                return false;
            }

            var definitions = maps ?? Array.Empty<BoardMapDefinition>();
            for (var index = 0; index < definitions.Length; index++)
            {
                var candidate = definitions[index];
                if (candidate != null &&
                    string.Equals(candidate.MapId, mapId, StringComparison.Ordinal))
                {
                    definition = candidate;
                    return true;
                }
            }

            return false;
        }

        public bool HasUniqueValidIds()
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var definitions = maps ?? Array.Empty<BoardMapDefinition>();
            for (var index = 0; index < definitions.Length; index++)
            {
                var definition = definitions[index];
                if (definition == null ||
                    !definition.HasValidIdentity ||
                    !ids.Add(definition.MapId))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
