using System;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class SharedVfxContractTests
    {
        private const string ExplosionPath =
            "Assets/MazeParty/Prefabs/Common/VFX/CartoonExplosion.prefab";
        private const string HitSparkPath =
            "Assets/MazeParty/Prefabs/Common/VFX/HitSpark.prefab";
        private const string FireImpactPath =
            "Assets/MazeParty/Prefabs/Common/VFX/FireImpact.prefab";
        private const string LightningStrikePath =
            "Assets/MazeParty/Prefabs/Common/VFX/LightningStrike.prefab";
        private const string WaterShieldPath =
            "Assets/MazeParty/Prefabs/Common/VFX/WaterShield.prefab";
        private const string WaterShieldSourcePath =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/" +
            "Water Shield.prefab";
        private const string SandShieldPath =
            "Assets/MazeParty/Prefabs/Common/VFX/SandShield.prefab";
        private const string SandShieldSourcePath =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/" +
            "Sand Shield.prefab";

        [Test]
        public void MeshOneShot_WaitsForAuthoredLifetimeAndCanBeReplayed()
        {
            const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Common/VFX/FireTornado.prefab");
            Assert.That(source, Is.Not.Null);
            var instance = UnityEngine.Object.Instantiate(source);
            try
            {
                var pooled = instance.GetComponent<PooledOneShotVfx>();
                var type = typeof(PooledOneShotVfx);
                Assert.That((float)type.GetField("minimumPlaybackSeconds", flags).GetValue(pooled), Is.GreaterThan(0f));
                Assert.That(type.GetField("playbackAnimator", flags).GetValue(pooled), Is.Not.Null);
                Assert.That(instance.GetComponentsInChildren<Collider>(true), Is.Empty);
                Assert.That(instance.GetComponentsInChildren<MonoBehaviour>(true), Has.Length.EqualTo(1));
                var returns = 0;
                type.GetMethod("PrepareForPool", flags).Invoke(pooled,
                    new object[] { new Action<PooledOneShotVfx>(_ => returns++) });
                for (var cycle = 0; cycle < 2; cycle++)
                {
                    instance.SetActive(true);
                    type.GetMethod("PlayFromPool", flags).Invoke(pooled, null);
                    pooled.StopPresentation(pooled.PlaybackRevision - 1);
                    type.GetMethod("Update", flags).Invoke(pooled, null);
                    Assert.That(returns, Is.EqualTo(cycle), "Mesh effects must not finish as an empty particle system.");
                    type.GetField("_timeoutAt", flags).SetValue(pooled, Time.unscaledTime);
                    type.GetMethod("Update", flags).Invoke(pooled, null);
                    Assert.That(returns, Is.EqualTo(cycle + 1));
                    pooled.StopPresentation(pooled.PlaybackRevision);
                    Assert.That(returns, Is.EqualTo(cycle + 1), "Returning is idempotent.");
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }

        [Test]
        public void SharedOneShots_AreAuthoredPooledAndPresentationOnly()
        {
            foreach (var path in new[]
                     {
                         ExplosionPath,
                         HitSparkPath,
                         FireImpactPath,
                         LightningStrikePath
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                var pooled = prefab.GetComponent<PooledOneShotVfx>();
                Assert.That(pooled, Is.Not.Null, path);
                Assert.That(pooled.ParticleSystems, Is.Not.Empty, path);
                Assert.That(pooled.ParticleSystems.All(item => item != null),
                    Is.True, path);
                Assert.That(pooled.FlashLights, Is.Not.Empty, path);
                Assert.That(pooled.FlashLights.All(item => item != null),
                    Is.True, path);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true),
                    Is.Empty, path);
                Assert.That(prefab.GetComponentsInChildren<Transform>(true)
                    .Any(item =>
                        item.name.IndexOf(
                            "Distort",
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        item.name.IndexOf(
                            "GrabPass",
                            StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False, path);

                var activeMaterials = prefab
                    .GetComponentsInChildren<Renderer>(false)
                    .SelectMany(item => item.sharedMaterials)
                    .Where(item => item != null)
                    .ToArray();
                Assert.That(activeMaterials.Any(item =>
                        item.shader != null &&
                        item.shader.name.IndexOf(
                            "GrabPass",
                            StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False,
                    path + " has an active GrabPass material.");
                Assert.That(AssetDatabase.GetDependencies(path, true)
                        .Any(item => item.EndsWith(
                            "/AllIn1VfxGrabPass.shader",
                            StringComparison.OrdinalIgnoreCase)),
                    Is.False,
                    path + " still depends on the GrabPass shader.");

                var behaviours = prefab
                    .GetComponentsInChildren<MonoBehaviour>(true);
                Assert.That(behaviours.Any(item => item == null),
                    Is.False, path + " contains a missing script.");
                Assert.That(behaviours.Where(item =>
                        item is not PooledOneShotVfx),
                    Is.Empty,
                    path + " must not carry vendor, gameplay, or network scripts.");
                Assert.That(behaviours.Any(item =>
                        item.GetType().FullName == "Unity.Netcode.NetworkObject"),
                    Is.False, path);
            }
        }

        [Test]
        public void BoardItems_UseSharedExplosionAndNonGoreImpactFamilies()
        {
            var explosion = AssetDatabase.LoadAssetAtPath<GameObject>(
                ExplosionPath);
            var hitSpark = AssetDatabase.LoadAssetAtPath<GameObject>(
                HitSparkPath);
            var grenade = PrototypeItemCatalog.Get(PrototypeItemId.Grenade);
            var mine = PrototypeItemCatalog.Get(PrototypeItemId.Mine);
            var pistol = PrototypeItemCatalog.Get(PrototypeItemId.Pistol);
            var sniper = PrototypeItemCatalog.Get(PrototypeItemId.Sniper);

            Assert.That(grenade.ExplosionPrefab, Is.SameAs(explosion));
            Assert.That(mine.ExplosionPrefab, Is.SameAs(explosion));
            Assert.That(pistol.ImpactPrefab, Is.SameAs(hitSpark));
            Assert.That(sniper.ImpactPrefab, Is.SameAs(hitSpark));
            var playerImpact = AssetDatabase.LoadAssetAtPath<GameObject>(FireImpactPath);
            Assert.That(playerImpact, Is.Not.Null);
            Assert.That(pistol.PlayerImpactPrefab, Is.SameAs(playerImpact));
            Assert.That(sniper.PlayerImpactPrefab, Is.SameAs(playerImpact));

            foreach (var id in new[]
                     {
                         PrototypeItemId.DoubleDice,
                         PrototypeItemId.Pistol,
                         PrototypeItemId.Sniper,
                         PrototypeItemId.LowDice,
                         PrototypeItemId.HighDice,
                         PrototypeItemId.PositionSwapper,
                         PrototypeItemId.Cloak
                     })
            {
                Assert.That(
                    PrototypeItemCatalog.Get(id).ExplosionPrefab,
                    Is.Null,
                    id.ToString());
            }

            foreach (var id in new[]
                     {
                         PrototypeItemId.DoubleDice,
                         PrototypeItemId.Grenade,
                         PrototypeItemId.Mine,
                         PrototypeItemId.LowDice,
                         PrototypeItemId.HighDice,
                         PrototypeItemId.PositionSwapper,
                         PrototypeItemId.Cloak
                     })
            {
                Assert.That(
                    PrototypeItemCatalog.Get(id).ImpactPrefab,
                    Is.Null,
                    id.ToString());
                Assert.That(PrototypeItemCatalog.Get(id).PlayerImpactPrefab, Is.Null, id.ToString());
            }
        }

        [Test]
        public void SharedShields_AreTrackedPersistentPresentationWrappers()
        {
            foreach (var shield in new[]
                     {
                         (Path: WaterShieldPath,
                             SourcePath: WaterShieldSourcePath),
                         (Path: SandShieldPath,
                             SourcePath: SandShieldSourcePath)
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    shield.Path);
                Assert.That(prefab, Is.Not.Null, shield.Path);
                Assert.That(prefab.GetComponent<PooledOneShotVfx>(), Is.Null,
                    "The shield lifetime is owned by presentation state, not a one-shot timer.");
                Assert.That(
                    prefab.GetComponentsInChildren<Renderer>(true),
                    Is.Not.Empty,
                    shield.Path);
                Assert.That(
                    prefab.GetComponentsInChildren<Animator>(true),
                    Is.Not.Empty,
                    shield.Path);
                Assert.That(
                    prefab.GetComponentsInChildren<Animator>(true).Any(item => item.enabled),
                    Is.False,
                    "Persistent shields must not run demo controllers that fade them out on a timer.");
                Assert.That(
                    prefab.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "The shared shield must never participate in gameplay physics.");
                var behaviours = prefab.GetComponentsInChildren<MonoBehaviour>(
                    true);
                Assert.That(behaviours.Any(item => item == null), Is.False,
                    "The tracked wrapper must not contain missing scripts.");
                Assert.That(
                    behaviours.Where(item => item != null),
                    Is.Empty,
                    "The tracked wrapper must not import vendor or gameplay scripts.");
                Assert.That(
                    AssetDatabase.GetDependencies(shield.Path, true)
                        .Select(item => item.Replace('\\', '/')),
                    Does.Contain(shield.SourcePath),
                    "The tracked wrapper must keep the requested toolkit source prefab.");
            }
        }
    }
}
