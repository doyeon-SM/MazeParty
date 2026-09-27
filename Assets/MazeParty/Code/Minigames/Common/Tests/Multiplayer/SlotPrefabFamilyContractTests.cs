using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class SlotPrefabFamilyContractTests
    {
        private const string BouncingScenePath =
            "Assets/MazeParty/Scenes/Minigames/BouncingBalls/BouncingBalls.unity";
        private const string CliffScenePath =
            "Assets/MazeParty/Scenes/Minigames/CliffBarrage/CliffBarrage.unity";

        [Test]
        public void IdenticalPoolMembers_ShareOneAuthoredPrefabPerFamily()
        {
            AssertConnectedFamily(
                BouncingScenePath,
                "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Ball.prefab",
                Enumerable.Range(1, 3)
                    .Select(index => "Ball " + index)
                    .ToArray());
            AssertConnectedFamily(
                CliffScenePath,
                "Assets/MazeParty/Prefabs/Minigames/CliffBarrage/Projectile.prefab",
                Enumerable.Range(
                        1,
                        NetworkCliffBarrageState.ProjectilePoolSize)
                    .Select(index => "Projectile " + index)
                    .ToArray());
            AssertConnectedFamily(
                CliffScenePath,
                "Assets/MazeParty/Prefabs/Minigames/CliffBarrage/LaserRig.prefab",
                Enumerable.Range(
                        1,
                        NetworkCliffBarrageState.LaserPoolSize)
                    .Select(index => "Laser Rig " + index)
                    .ToArray());
        }

        private static void AssertConnectedFamily(
            string scenePath,
            string prefabPath,
            string[] instanceNames)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            Assert.That(
                prefab.GetComponentsInChildren<Transform>(true)
                    .Sum(child =>
                        GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                            child.gameObject)),
                Is.Zero,
                prefabPath);

            var scene = SceneManager.GetSceneByPath(scenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    scenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var transforms = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<Transform>(true))
                    .ToArray();
                foreach (var instanceName in instanceNames)
                {
                    var instance = transforms.SingleOrDefault(candidate =>
                        candidate.name == instanceName &&
                        PrefabUtility.GetNearestPrefabInstanceRoot(
                            candidate.gameObject) == candidate.gameObject);
                    Assert.That(instance, Is.Not.Null, instanceName);
                    Assert.That(
                        PrefabUtility.GetPrefabInstanceStatus(
                            instance.gameObject),
                        Is.EqualTo(PrefabInstanceStatus.Connected),
                        instanceName);
                    Assert.That(
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                            instance.gameObject),
                        Is.EqualTo(prefabPath),
                        instanceName);
                    Assert.That(
                        PrefabUtility.GetAddedComponents(instance.gameObject),
                        Is.Empty,
                        instanceName);
                    Assert.That(
                        PrefabUtility.GetAddedGameObjects(instance.gameObject),
                        Is.Empty,
                        instanceName);
                }
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }
    }
}
