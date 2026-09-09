using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class PlayerCharacterRulesTests
    {
        [TestCase(PlayerHitRegion.Body, 20)]
        [TestCase(PlayerHitRegion.Head, 26)]
        [TestCase(PlayerHitRegion.Hand, 14)]
        public void FirearmDamage_UsesConfiguredBodyRegionMultiplier(
            PlayerHitRegion region,
            int expected)
        {
            Assert.That(
                FirearmDamageRules.ApplyRegionMultiplier(20, region),
                Is.EqualTo(expected));
        }

        [Test]
        public void AvatarVisual_BuildsStableCustomizationAndHitboxAnchors()
        {
            var root = new GameObject("Player");
            try
            {
                var visual = root.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();

                Assert.That(root.transform.Find("VisualRoot/WorldModel/BodyAnchor"), Is.Not.Null);
                Assert.That(root.transform.Find("VisualRoot/WorldModel/HeadAnchor"), Is.Not.Null);
                Assert.That(root.transform.Find("VisualRoot/WorldModel/HatAnchor"), Is.Not.Null);
                Assert.That(root.transform.Find("VisualRoot/WorldModel/OutfitAnchor"), Is.Not.Null);
                Assert.That(
                    root.transform.Find(
                        "VisualRoot/WorldModel/ItemUseAnchor/PulseBlasterModel"),
                    Is.Not.Null);
                Assert.That(root.transform.Find("HitboxRoot/BodyHitbox"), Is.Not.Null);
                Assert.That(root.transform.Find("HitboxRoot/HeadHitbox"), Is.Not.Null);
                Assert.That(
                    root.GetComponentsInChildren<PlayerHitZone>(true),
                    Has.Length.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ItemUse_HidesWorldHandsAndShowsOnlySelectedPlaceholder()
        {
            var root = new GameObject("Player");
            try
            {
                var visual = root.AddComponent<PlayerAvatarVisual>();
                visual.TriggerItemUse(PrototypeItemId.PushMine);

                Assert.That(visual.IsUsingItem, Is.True);
                Assert.That(
                    root.transform.Find("VisualRoot/WorldModel/LeftHandAnchor").gameObject.activeSelf,
                    Is.False);
                Assert.That(
                    root.transform.Find("VisualRoot/WorldModel/RightHandAnchor").gameObject.activeSelf,
                    Is.False);
                Assert.That(
                    root.transform.Find(
                        "VisualRoot/WorldModel/ItemUseAnchor/PushMineModel").gameObject.activeSelf,
                    Is.True);
                Assert.That(
                    root.transform.Find(
                        "VisualRoot/WorldModel/ItemUseAnchor/PulseBlasterModel").gameObject.activeSelf,
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DamageRequest_PreservesLocalizedHitRegion()
        {
            var request = new DamageRequest(
                20,
                DamageKind.Item,
                null,
                PlayerHitRegion.Head);

            Assert.That(request.HitRegion, Is.EqualTo(PlayerHitRegion.Head));
        }

        [Test]
        public void FirearmResolver_AppliesHeadDamageThroughChildHitZone()
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
