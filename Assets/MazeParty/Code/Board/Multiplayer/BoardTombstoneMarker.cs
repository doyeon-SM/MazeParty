using MazeParty.Gameplay;
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
                goldLabel.text = GameText.F("+{0} GOLD\nRMB", gold);
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
