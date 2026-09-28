using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Stable, selectable identity for one authored board map. The visual environment
    /// remains inside the referenced prefab and is never generated from this asset.
    /// </summary>
    [CreateAssetMenu(menuName = "MazeParty/Board/Map Definition")]
    public sealed class BoardMapDefinition : ScriptableObject
    {
        [SerializeField] private string mapId = "new-map";
        [SerializeField] private string displayName = "New Map";
        [SerializeField, Min(1)] private int contentVersion = 1;
        [SerializeField] private GameObject mapRootPrefab;

        public string MapId => mapId;
        public string DisplayName => displayName;
        public int ContentVersion => Mathf.Max(1, contentVersion);
        public GameObject MapRootPrefab => mapRootPrefab;

        public bool HasValidIdentity => IsValidMapId(mapId) &&
                                        !string.IsNullOrWhiteSpace(displayName);

        public bool HasValidPrefab => mapRootPrefab != null &&
                                      mapRootPrefab.GetComponent<BoardMapRoot>() != null;

        public void Configure(
            string id,
            string name,
            int version,
            GameObject rootPrefab)
        {
            mapId = NormalizeMapId(id);
            displayName = string.IsNullOrWhiteSpace(name) ? mapId : name.Trim();
            contentVersion = Mathf.Max(1, version);
            mapRootPrefab = rootPrefab;
        }

        public static string NormalizeMapId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var source = value.Trim().ToLowerInvariant();
            var normalized = new char[source.Length];
            var length = 0;
            var pendingDash = false;
            for (var index = 0; index < source.Length; index++)
            {
                var character = source[index];
                if ((character >= 'a' && character <= 'z') ||
                    (character >= '0' && character <= '9'))
                {
                    if (pendingDash && length > 0)
                    {
                        normalized[length++] = '-';
                    }

                    normalized[length++] = character;
                    pendingDash = false;
                }
                else
                {
                    pendingDash = length > 0;
                }
            }

            return new string(normalized, 0, length);
        }

        public static bool IsValidMapId(string value)
        {
            return !string.IsNullOrEmpty(value) &&
                   string.Equals(value, NormalizeMapId(value), StringComparison.Ordinal);
        }
    }
}
