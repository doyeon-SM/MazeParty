using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Local raycast marker. Purchase validation remains server-authoritative.</summary>
    public sealed class KeyShopWorldTarget : MonoBehaviour
    {
    }

    /// <summary>Local raycast marker for one of the two replicated item shops.</summary>
    public sealed class ItemShopWorldTarget : MonoBehaviour
    {
        [SerializeField] private int shopIndex = -1;

        public int ShopIndex => shopIndex;

        public void Configure(int index)
        {
            shopIndex = index;
        }
    }
}
