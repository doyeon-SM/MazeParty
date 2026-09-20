using System;
using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    /// <summary>
    /// One shared serialization contract for the authored world objects of
    /// every minigame. UI prefabs have a separate project-wide policy test.
    /// </summary>
    public sealed class MinigameCorePrefabContractTests
    {
        private const string SceneFolder =
            "Assets/MazeParty/Scenes/Minigames/";
        private const string PrefabFolder =
            "Assets/MazeParty/Prefabs/Minigames/";

        private static readonly string[] Games =
        {
            "Minefield", "WrongWay", "RedLightGreenLight",
            "StableFooting", "BalloonBlow", "GiftGrab",
            "TerritoryPaint", "TagChase", "Race",
            "SequenceMemory", "BouncingBalls", "BombPassing",
            "SnowySpin", "ArenaCombat", "CliffBarrage"
        };

        [Test]
        public void MinigameScenes_UseConnectedCoreWorldPrefabAssets()
        {
            foreach (var game in Games)
            {
                var scenePath = SceneFolder + game + "/" + game + ".unity";
                var prefabPrefix = PrefabFolder + game + "/";
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath),
                    Is.Not.Null,
                    scenePath);

                var previousActive = SceneManager.GetActiveScene();
                var scene = SceneManager.GetSceneByPath(scenePath);
                var openedForTest = !scene.IsValid() || !scene.isLoaded;
                if (openedForTest)
                {
                    scene = EditorSceneManager.OpenScene(
                        scenePath, OpenSceneMode.Additive);
                }

                try
                {
                    var instances = scene.GetRootGameObjects()
                        .SelectMany(root =>
                            root.GetComponentsInChildren<Transform>(true))
                        .Select(transform => transform.gameObject)
                        .Where(gameObject =>
                            PrefabUtility.GetNearestPrefabInstanceRoot(
                                gameObject) == gameObject)
                        // In-scene NGO state roots must keep their scene
                        // identity and are not world-art prefab candidates.
                        .Where(gameObject =>
                            gameObject.GetComponent<NetworkObject>() == null)
                        .Select(gameObject => new
                        {
                            Instance = gameObject,
                            Path = PrefabUtility
                                .GetPrefabAssetPathOfNearestInstanceRoot(
                                    gameObject)
                        })
                        .Where(item => item.Path != null &&
                            item.Path.StartsWith(
                                prefabPrefix, StringComparison.Ordinal) &&
                            !item.Path.Substring(prefabPrefix.Length)
                                .StartsWith("UI/", StringComparison.Ordinal))
                        .ToArray();

                    Assert.That(
                        instances,
                        Is.Not.Empty,
                        scenePath + " has no scene-connected core world " +
                        "prefab under " + prefabPrefix);

                    var stagePrefab = game == "ArenaCombat"
                        ? "ArenaStructure.prefab"
                        : game == "SnowySpin"
                            ? "IceArena.prefab"
                            : game == "CliffBarrage"
                                ? "CliffArena.prefab"
                                : null;
                    if (stagePrefab != null)
                    {
                        Assert.That(
                            instances.Any(item =>
                                item.Path == prefabPrefix + stagePrefab),
                            Is.True,
                            scenePath + " must connect its editable arena " +
                            "structure prefab.");
                    }

                    foreach (var item in instances)
                    {
                        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                            item.Path);
                        Assert.That(prefab, Is.Not.Null, item.Path);
                        Assert.That(
                            PrefabUtility.IsPartOfPrefabAsset(prefab),
                            Is.True,
                            item.Path);
                        Assert.That(
                            PrefabUtility.GetPrefabInstanceStatus(
                                item.Instance),
                            Is.EqualTo(PrefabInstanceStatus.Connected),
                            scenePath + " :: " + item.Instance.name);

                        var source = PrefabUtility
                            .GetCorrespondingObjectFromSource(
                                item.Instance);
                        Assert.That(
                            source,
                            Is.Not.Null,
                            scenePath + " :: " + item.Instance.name);
                        Assert.That(
                            AssetDatabase.GetAssetPath(source),
                            Is.EqualTo(item.Path),
                            scenePath + " :: " + item.Instance.name);

                        AssertNoMissingScripts(prefab, item.Path);
                        AssertNoMissingScripts(
                            item.Instance,
                            scenePath + " :: " + item.Instance.name);
                    }
                }
                finally
                {
                    if (previousActive.IsValid() &&
                        previousActive.isLoaded &&
                        !SceneManager.GetActiveScene().Equals(previousActive))
                    {
                        SceneManager.SetActiveScene(previousActive);
                    }
                    if (openedForTest && scene.IsValid() && scene.isLoaded)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
        }

        private static void AssertNoMissingScripts(
            GameObject root, string context)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                Assert.That(
                    GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(
                        child.gameObject),
                    Is.Zero,
                    context + " :: " + child.name);
            }
        }
    }
}
