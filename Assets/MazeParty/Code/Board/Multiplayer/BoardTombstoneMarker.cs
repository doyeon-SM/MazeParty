using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class BoardTombstoneMarker : MonoBehaviour
    {
        [SerializeField] private TextMesh goldLabel;

        public int Id { get; private set; }

        public void Configure(int id, int gold)
        {
            Id = id;
            if (goldLabel != null)
            {
                goldLabel.text = "+" + gold + " GOLD\nRMB";
            }
        }

#if UNITY_EDITOR
        public void SetGoldLabel(TextMesh label)
        {
            goldLabel = label;
        }
#endif
    }
}
