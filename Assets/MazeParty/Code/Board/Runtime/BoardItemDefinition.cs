using UnityEngine;

namespace MazeParty.Gameplay
{
    [CreateAssetMenu(menuName = "MazeParty/Items/Board Item")]
    public sealed class BoardItemDefinition : ScriptableObject
    {
        public PrototypeItemId Id;
        public string DisplayName;
        [TextArea] public string Description;
        [Min(0)] public int Price;
        [Min(0)] public int SpawnWeight = 1;
        [Min(1)] public int Charges = 1;
        [Min(0)] public int Damage;
        [Min(0)] public float Range;
        [Min(.01f)] public float FireInterval = .25f;
        [Min(1)] public float AimMagnification = 1f;
        [Min(0)] public float BlastRadius;
        [Min(0)] public float TriggerRadius;
        [Min(0)] public float ArmingDelay;
        [Min(.1f)] public float ThrowFlightSeconds = 1f;
        [Min(.01f)] public float ProjectileRadius = .12f;
        [Min(.1f)] public float ProjectileLifetime = 5f;
        [Range(1, 12)] public int DiceMinimum = 1;
        [Range(1, 12)] public int DiceMaximum = 12;
        [Min(.01f)] public float CastDuration = 2f;
        public GameObject HeldPrefab;
        public GameObject WorldPrefab;
        public GameObject ExplosionPrefab;
    }
}
