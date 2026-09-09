using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum PlayerHitRegion : byte
    {
        Body,
        Head,
        Hand
    }

    /// <summary>
    /// Marks a player root whose child trigger colliders provide firearm hit regions.
    /// The root movement collider is deliberately ignored by firearm raycasts.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerHitZoneOwner : MonoBehaviour
    {
    }

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

    public static class FirearmDamageRules
    {
        public const int PulseBlasterBaseDamage = 20;
        public const float PulseBlasterRange = 40f;
        public const float PulseBlasterPush = 6f;

        public static int ApplyRegionMultiplier(int bodyDamage, PlayerHitRegion region)
        {
            var safeDamage = Mathf.Max(0, bodyDamage);
            switch (region)
            {
                case PlayerHitRegion.Head:
                    return Mathf.RoundToInt(safeDamage * 1.3f);
                case PlayerHitRegion.Hand:
                    return Mathf.RoundToInt(safeDamage * 0.7f);
                default:
                    return safeDamage;
            }
        }
    }

    public static class PlayerUnarmedRules
    {
        public const float PunchRange = 1.65f;
        public const float PunchRadius = 0.3f;
        public const double PunchCooldownSeconds = 0.5d;
    }

    public readonly struct FirearmHitReport
    {
        public FirearmHitReport(
            bool hit,
            GameObject target,
            PlayerHitRegion region,
            int damage,
            DamageResult damageResult,
            bool pushApplied,
            Vector3 point)
        {
            Hit = hit;
            Target = target;
            Region = region;
            Damage = damage;
            DamageResult = damageResult;
            PushApplied = pushApplied;
            Point = point;
        }

        public bool Hit { get; }
        public GameObject Target { get; }
        public PlayerHitRegion Region { get; }
        public int Damage { get; }
        public DamageResult DamageResult { get; }
        public bool PushApplied { get; }
        public Vector3 Point { get; }
    }

    public static class FirearmHitResolver
    {
        public static FirearmHitReport Raycast(
            GameObject source,
            Vector3 origin,
            Vector3 direction,
            float range,
            int bodyDamage,
            Vector3 push)
        {
            if (direction.sqrMagnitude < 0.0001f || range <= 0f)
            {
                return default;
            }

            var hits = Physics.RaycastAll(
                origin,
                direction.normalized,
                range,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Collide);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            for (var index = 0; index < hits.Length; index++)
            {
                var collider = hits[index].collider;
                if (collider == null || IsPartOfSource(collider.transform, source))
                {
                    continue;
                }

                var zone = collider.GetComponent<PlayerHitZone>();
                if (zone != null)
                {
                    var damage = FirearmDamageRules.ApplyRegionMultiplier(
                        bodyDamage,
                        zone.Region);
                    var request = new DamageRequest(
                        damage,
                        DamageKind.Item,
                        source,
                        zone.Region);
                    var report = GameplayHitResolver.Resolve(zone.gameObject, request, push);
                    return new FirearmHitReport(
                        true,
                        zone.gameObject,
                        zone.Region,
                        damage,
                        report.DamageResult,
                        report.PushApplied,
                        hits[index].point);
                }

                // A zoned character's CharacterController only drives movement. Its
                // child hit regions are the authoritative firearm targets.
                if (collider.GetComponentInParent<PlayerHitZoneOwner>() != null)
                {
                    continue;
                }

                if (TryFindInParents<IDamageable>(collider.transform, out _))
                {
                    var request = new DamageRequest(bodyDamage, DamageKind.Item, source);
                    var report = GameplayHitResolver.Resolve(collider.gameObject, request, push);
                    return new FirearmHitReport(
                        true,
                        collider.gameObject,
                        PlayerHitRegion.Body,
                        Mathf.Max(0, bodyDamage),
                        report.DamageResult,
                        report.PushApplied,
                        hits[index].point);
                }

                // Ignore unrelated triggers, but let the nearest solid world surface
                // stop the shot so targets cannot be hit through walls.
                if (!collider.isTrigger)
                {
                    return new FirearmHitReport(
                        true,
                        collider.gameObject,
                        PlayerHitRegion.Body,
                        0,
                        DamageResult.Ignored,
                        false,
                        hits[index].point);
                }
            }

            return default;
        }

        private static bool IsPartOfSource(Transform candidate, GameObject source)
        {
            return source != null &&
                   (candidate == source.transform || candidate.IsChildOf(source.transform));
        }

        private static bool TryFindInParents<T>(Transform target, out T result)
            where T : class
        {
            var current = target;
            while (current != null)
            {
                var behaviours = current.GetComponents<MonoBehaviour>();
                for (var index = 0; index < behaviours.Length; index++)
                {
                    if (behaviours[index] is T match)
                    {
                        result = match;
                        return true;
                    }
                }

                current = current.parent;
            }

            result = null;
            return false;
        }
    }
}
