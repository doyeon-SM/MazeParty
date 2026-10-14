using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class BoardTombstoneMarker : MonoBehaviour
    {
        public int Id { get; private set; }
        public int Gold { get; private set; }

        public void Configure(int id, int gold)
        {
            Id = id;
            Gold = Mathf.Max(0, gold);
        }
    }
}
