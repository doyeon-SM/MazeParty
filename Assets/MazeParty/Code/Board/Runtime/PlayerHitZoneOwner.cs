using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Marks a player root whose child trigger colliders provide firearm hit regions.
    /// The root movement collider is deliberately ignored by firearm raycasts.
    /// This MonoBehaviour intentionally lives in a same-named source file so
    /// Unity can persist a valid MonoScript reference in authored prefabs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHitZoneOwner : MonoBehaviour
    {
    }
}
