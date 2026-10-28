using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum BoardItemUseTiming : byte
    {
        Immediate = 0,
        TurnStart = 1
    }

    [CreateAssetMenu(menuName = "MazeParty/Items/Board Item")]
    public sealed class BoardItemDefinition : ScriptableObject
    {
        public PrototypeItemId Id;
        public BoardItemUseTiming UseTiming;
        public string DisplayName;
        [Tooltip("Empty for common items; otherwise the only board map that can grant or use this item.")]
        public string RequiredMapId;
        public bool IsAvailableOnMap(string mapId) =>
            string.IsNullOrEmpty(RequiredMapId) || RequiredMapId == mapId;
        [TextArea] public string Description;
        [Min(0)] public int Price;
        [Min(0)] public int SpawnWeight = 1;
        [Min(1)] public int Charges = 1;
        [Min(0)] public int Damage;
        [Min(0)] public float Range;
        [Min(.01f)] public float FireInterval = .25f;
        [Min(0)] public float BlastRadius;
        [Min(0)] public float TriggerRadius;
        [Min(0)] public float ArmingDelay;
        [Min(.1f)] public float ThrowFlightSeconds = 1f;
        [Min(.01f)] public float ProjectileRadius = .12f;
        [Min(.1f)] public float ProjectileLifetime = 5f;
        [Range(1, 12)] public int DiceMinimum = 1;
        [Range(1, 12)] public int DiceMaximum = 12;
        [Min(.01f)] public float CastDuration = 2f;
        public Sprite Icon;
        public GameObject HeldPrefab;
        public GameObject WorldPrefab;
        public GameObject ExplosionPrefab;
        [Min(0.01f)] public float ExplosionScaleMultiplier = 1f;
        public GameObject ImpactPrefab;
        [Tooltip("Firearm impact on a player; ImpactPrefab remains the world-surface effect.")]
        public GameObject PlayerImpactPrefab;
    }
}
