using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameReusableAssetContractTests
    {
        private static readonly string[] PresentationOnlyPrefabPaths =
        {
            "Assets/MazeParty/Prefabs/Minigames/Common/Environment/SharedFinishGate.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/Environment/SharedOutdoorFence.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/Environment/SharedNatureGroundTile.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/Environment/SharedTripleSwitch.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/Environment/SharedSciFiBlock.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/Environment/SharedFantasyCliff.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Minefield/ProximitySiren.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Minefield/DetectedMine.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BombPassing/Bomb.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BalloonBlow/Balloon1.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BalloonBlow/Balloon2.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BalloonBlow/Balloon3.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BalloonBlow/Balloon4.prefab",
            "Assets/MazeParty/Prefabs/Minigames/GiftGrab/Gift.prefab",
            "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/Npc.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Ball.prefab",
            "Assets/MazeParty/Prefabs/Minigames/CliffBarrage/Projectile.prefab",
            "Assets/MazeParty/Prefabs/Minigames/CliffBarrage/LaserRig.prefab",
            "Assets/MazeParty/Prefabs/Minigames/SnowySpin/IceArena.prefab",
            "Assets/MazeParty/Prefabs/Minigames/GiftGrab/Base1.prefab",
            "Assets/MazeParty/Prefabs/Minigames/GiftGrab/Base2.prefab",
            "Assets/MazeParty/Prefabs/Minigames/GiftGrab/Base3.prefab",
            "Assets/MazeParty/Prefabs/Minigames/GiftGrab/Base4.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Shield1.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Shield2.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Shield3.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Shield4.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Goal1.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Goal2.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Goal3.prefab",
            "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Goal4.prefab",
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/PlayerSpawn1.prefab",
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/PlayerSpawn2.prefab",
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/PlayerSpawn3.prefab",
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/PlayerSpawn4.prefab",
            "Assets/MazeParty/Prefabs/Minigames/SnowySpin/PlayerBall1.prefab",
            "Assets/MazeParty/Prefabs/Minigames/SnowySpin/PlayerBall2.prefab",
            "Assets/MazeParty/Prefabs/Minigames/SnowySpin/PlayerBall3.prefab",
            "Assets/MazeParty/Prefabs/Minigames/SnowySpin/PlayerBall4.prefab"
        };

        [Test]
        public void PresentationOnlyPrefabs_HaveNoEnabledColliders()
        {
            foreach (var prefabPath in PresentationOnlyPrefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    prefabPath);
                Assert.That(prefab, Is.Not.Null, prefabPath);

                var enabledColliders = prefab
                    .GetComponentsInChildren<Collider>(true)
                    .Where(collider => collider.enabled)
                    .Select(collider =>
                        collider.name + " (" +
                        collider.GetType().Name + ")")
                    .ToArray();
                Assert.That(
                    enabledColliders,
                    Is.Empty,
                    prefabPath +
                    " is presentation-only and must not participate in " +
                    "gameplay collision.\n" +
                    string.Join("\n", enabledColliders));
            }
        }

        [Test]
        public void AuthorityPrefabs_KeepImportedVisualSubtreesColliderFree()
        {
            const string crusherPath =
                "Assets/MazeParty/Prefabs/Minigames/Minefield/Crusher.prefab";
            var crusher = AssetDatabase.LoadAssetAtPath<GameObject>(
                crusherPath);
            Assert.That(crusher, Is.Not.Null, crusherPath);
            var authorityCollider = crusher.GetComponent<BoxCollider>();
            Assert.That(authorityCollider, Is.Not.Null, crusherPath);
            Assert.That(authorityCollider.isTrigger, Is.True, crusherPath);
            var crusherVisual = crusher.transform.Find(
                "Crusher Wall Source Visual");
            Assert.That(crusherVisual, Is.Not.Null, crusherPath);
            Assert.That(
                crusherVisual.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                crusherPath + " imported visual colliders are forbidden.");

            for (var lane = 1; lane <= 4; lane++)
            {
                var lanePath =
                    "Assets/MazeParty/Prefabs/Minigames/WrongWay/Lane" +
                    lane + ".prefab";
                var lanePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    lanePath);
                Assert.That(lanePrefab, Is.Not.Null, lanePath);
                var steps = lanePrefab.transform.Cast<Transform>()
                    .Where(item => item.name.StartsWith("Step "))
                    .ToArray();
                Assert.That(steps, Is.Not.Empty, lanePath);
                foreach (var step in steps)
                {
                    var visual = step.Find("Toy Block Visual");
                    Assert.That(
                        visual,
                        Is.Not.Null,
                        lanePath + " :: " + step.name);
                    Assert.That(
                        visual.GetComponentsInChildren<Collider>(true)
                            .Where(collider => collider.enabled),
                        Is.Empty,
                        lanePath + " :: " + step.name +
                        " imported visual colliders are forbidden.");
                }
            }
        }
    }
}
