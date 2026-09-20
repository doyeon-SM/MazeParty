using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class PlayerCharacterRulesTests
    {
        [Test]
        public void FirearmDamage_AppliesBodyRegionTable()
        {
            var cases = new[]
            {
                (Region: PlayerHitRegion.Body, Expected: 20),
                (Region: PlayerHitRegion.Head, Expected: 26),
                (Region: PlayerHitRegion.Hand, Expected: 14)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    FirearmDamageRules.ApplyRegionMultiplier(20, testCase.Region),
                    Is.EqualTo(testCase.Expected),
                    testCase.Region.ToString());
            }
        }

        [Test]
        public void FirearmResolver_AppliesLocalizedDamageThroughChildHitZone()
        {
            var source = new GameObject("Source");
            var target = new GameObject("Target");
            var hitbox = new GameObject("HeadHitbox");
            try
            {
                source.transform.position = new Vector3(1000f, 1000f, 1000f);
                target.transform.position = source.transform.position + Vector3.forward * 5f;
                var health = target.AddComponent<GameplayHealth>();
                target.AddComponent<PlayerHitZoneOwner>();
                hitbox.transform.SetParent(target.transform, false);
                hitbox.AddComponent<SphereCollider>().isTrigger = true;
                hitbox.AddComponent<PlayerHitZone>().Configure(PlayerHitRegion.Head);
                Physics.SyncTransforms();

                var report = FirearmHitResolver.Raycast(
                    source,
                    source.transform.position,
                    Vector3.forward,
                    10f,
                    20,
                    Vector3.zero);

                Assert.That(report.Hit, Is.True);
                Assert.That(report.Region, Is.EqualTo(PlayerHitRegion.Head));
                Assert.That(report.Damage, Is.EqualTo(26));
                Assert.That(report.DamageResult, Is.EqualTo(DamageResult.Applied));
                Assert.That(health.CurrentHealth, Is.EqualTo(74));
            }
            finally
            {
                Object.DestroyImmediate(hitbox);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(source);
            }
        }
    }
}
