using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Authored world assets shared by board scenes and dynamically created players.</summary>
    [CreateAssetMenu(menuName = "MazeParty/Board/World Prefabs")]
    public sealed class BoardWorldPrefabs : ScriptableObject
    {
        public const string ResourcePath = "MazeParty/Board/BoardWorldPrefabs";
        [SerializeField] private BoardShopVisual keyShop;
        [SerializeField] private BoardShopVisual[] itemShops = new BoardShopVisual[2];
        [SerializeField] private BoardBoundaryWallVisual boundaryWall;
        public BoardShopVisual KeyShop => keyShop;
        public BoardShopVisual ItemShop(int index) => itemShops[index];
        public BoardBoundaryWallVisual BoundaryWall => boundaryWall;
        public bool HasRequiredReferences => keyShop != null && keyShop.HasRequiredReferences &&
            itemShops != null && itemShops.Length == 2 &&
            itemShops[0] != null && itemShops[0].HasRequiredReferences &&
            itemShops[1] != null && itemShops[1].HasRequiredReferences &&
            boundaryWall != null && boundaryWall.HasRequiredReferences;

        public static BoardWorldPrefabs LoadRequired()
        {
            var assets = Resources.Load<BoardWorldPrefabs>(ResourcePath);
            if (assets == null || !assets.HasRequiredReferences)
                throw new InvalidOperationException("Board world prefabs are missing. Run MazeParty/Board/Install World Prefabs.");
            return assets;
        }
    }
}
