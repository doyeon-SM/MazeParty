using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Identifies the damage region represented by a player trigger collider.
    /// This MonoBehaviour intentionally lives in a same-named source file so
    /// Unity can persist a valid MonoScript reference in authored prefabs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHitZone : MonoBehaviour
    {
        [SerializeField] private PlayerHitRegion region = PlayerHitRegion.Body;

        public PlayerHitRegion Region => region;

        public void Configure(PlayerHitRegion value)
        {
            region = value;
        }
    }
}
