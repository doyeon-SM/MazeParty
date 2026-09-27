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

        private static readonly TestCaseData[] EnvironmentContracts =
        {
            EnvironmentCase(
                "BombPassing", "Bomb Passing Environment",
                "BombPassingEnvironment.prefab",
                typeof(NetworkBombPassingState),
                "Player Spawn Markers", "Bomb"),
            EnvironmentCase(
                "BouncingBalls", "Bouncing Balls Environment",
                "BouncingBallsEnvironment.prefab",
                typeof(NetworkBouncingBallsState),
                "Goals", "Shields", "Balls"),
            EnvironmentCase(
                "GiftGrab", "Gift Grab Environment",
                "GiftGrabEnvironment.prefab",
                typeof(NetworkGiftGrabState),
                "Player Anchors", "Base Anchors", "Gift Anchors"),
            EnvironmentCase(
                "Minefield", "Minefield Environment",
                "MinefieldEnvironment.prefab",
                typeof(NetworkMinefieldState),
                "Crusher Placeholder"),
            EnvironmentCase(
                "RedLightGreenLight", "Red Light Green Light Environment",
                "RedLightGreenLightEnvironment.prefab",
                typeof(NetworkRedLightGreenLightState),
                "Observer Placeholder", "Signal Tower Placeholder"),
            EnvironmentCase(
                "TagChase", "Tag Chase Environment",
                "TagChaseEnvironment.prefab",
                typeof(NetworkTagChaseState),
                "Sight Blocker 1", "Tagger Start"),
            EnvironmentCase(
                "TerritoryPaint", "Territory Paint Environment",
                "TerritoryPaintEnvironment.prefab",
                typeof(NetworkTerritoryPaintState),
                "Paint Surface", "Start Marker 1"),
            EnvironmentCase(
                "BalloonBlow", "Balloon Blow Environment",
                "BalloonBlowEnvironment.prefab",
                typeof(NetworkBalloonBlowState),
                "Player Anchors", "Balloon Anchors"),
            EnvironmentCase(
                "Race", "Race Environment",
                "RaceEnvironment.prefab",
                typeof(NetworkRaceState),
                "Race Track", "Finish Line", "Start Marker 1"),
            EnvironmentCase(
                "SequenceMemory", "Sequence Memory Environment",
                "SequenceMemoryEnvironment.prefab",
                typeof(NetworkSequenceMemoryState),
                "Player Anchors", "Station Placeholders", "NPC Anchor"),
            EnvironmentCase(
                "WrongWay", "Wrong Way Environment",
                "WrongWayEnvironment.prefab",
                typeof(NetworkWrongWayState),
                "Lane 1", "Finish Arch"),
            EnvironmentCase(
                "StableFooting", "Stable Footing Environment",
                "StableFootingEnvironment.prefab",
                typeof(NetworkStableFootingState),
                "Tile Anchors", "Player Anchors", "Safe Symbol Display")
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

        [TestCaseSource(nameof(EnvironmentContracts))]
        public void StaticEnvironment_IsConnectedWithoutAbsorbingRuntimeOwnership(
            string game,
            string instanceName,
            string prefabFile,
            Type networkStateType,
            string[] sceneOwnedNames)
        {
            var scenePath = SceneFolder + game + "/" + game + ".unity";
            var prefabPath = PrefabFolder + game + "/" + prefabFile;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            Assert.That(
                prefab.GetComponentsInChildren<NetworkObject>(true),
                Is.Empty,
                prefabPath + " must remain presentation-only.");
            Assert.That(
                prefab.GetComponentsInChildren<NetworkBehaviour>(true),
                Is.Empty,
                prefabPath + " must not own authoritative runtime state.");
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                prefabPath + " must remain renderer-only; collision belongs " +
                "to the scene or a gameplay core prefab.");
            foreach (var sceneOwnedName in sceneOwnedNames)
            {
                Assert.That(
                    FindNamed(prefab.transform, sceneOwnedName),
                    Is.Null,
                    prefabPath + " absorbed scene-owned object " +
                    sceneOwnedName);
            }

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
                var sceneObjects = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<Transform>(true))
                    .ToArray();
                var environments = sceneObjects
                    .Where(item => item.name == instanceName)
                    .ToArray();
                Assert.That(
                    environments,
                    Has.Length.EqualTo(1),
                    scenePath + " :: " + instanceName);
                var environment = environments[0];
                Assert.That(
                    PrefabUtility.GetNearestPrefabInstanceRoot(
                        environment.gameObject),
                    Is.EqualTo(environment.gameObject),
                    scenePath + " :: " + instanceName);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        environment.gameObject),
                    Is.EqualTo(prefabPath),
                    scenePath + " :: " + instanceName);
                Assert.That(
                    environment.GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    scenePath + " :: " + instanceName +
                    " must not add collider overrides.");

                var authorityColliderNames =
                    GetAuthorityColliderNames(game);
                var authorityRoots = sceneObjects
                    .Where(item => item.name == "Authority Colliders")
                    .ToArray();
                Assert.That(
                    authorityRoots,
                    Has.Length.EqualTo(
                        authorityColliderNames.Length == 0 ? 0 : 1),
                    scenePath + " authority collider root count");
                if (authorityColliderNames.Length > 0)
                {
                    var authorityRoot = authorityRoots[0];
                    Assert.That(
                        authorityRoot.IsChildOf(environment),
                        Is.False,
                        scenePath + " authority colliders must remain " +
                        "outside the environment prefab.");
                    Assert.That(
                        PrefabUtility.GetNearestPrefabInstanceRoot(
                            authorityRoot.gameObject),
                        Is.Null,
                        scenePath + " authority colliders must be scene-owned.");
                    Assert.That(
                        authorityRoot.GetComponentsInChildren<Collider>(true),
                        Has.Length.EqualTo(authorityColliderNames.Length),
                        scenePath + " authority collider count");
                    foreach (var colliderName in authorityColliderNames)
                    {
                        var matches = authorityRoot
                            .GetComponentsInChildren<Transform>(true)
                            .Where(item => item.name == colliderName)
                            .ToArray();
                        Assert.That(
                            matches,
                            Has.Length.EqualTo(1),
                            scenePath + " :: " + colliderName);
                        Assert.That(
                            matches[0].GetComponent<Collider>(),
                            Is.Not.Null,
                            scenePath + " :: " + colliderName);
                        Assert.That(
                            matches[0].GetComponent<Renderer>(),
                            Is.Null,
                            scenePath + " :: " + colliderName +
                            " must remain non-visual.");
                        Assert.That(
                            PrefabUtility.GetNearestPrefabInstanceRoot(
                                matches[0].gameObject),
                            Is.Null,
                            scenePath + " :: " + colliderName +
                            " must retain scene ownership.");
                    }
                }

                var networkStates = sceneObjects
                    .Select(item => item.GetComponent(networkStateType))
                    .Where(component => component != null)
                    .ToArray();
                Assert.That(
                    networkStates,
                    Has.Length.EqualTo(1),
                    scenePath + " must keep exactly one " +
                    networkStateType.Name + " component.");
                var networkState = networkStates[0];
                var networkStateObject = networkState.gameObject;
                Assert.That(
                    PrefabUtility.GetNearestPrefabInstanceRoot(
                        networkStateObject),
                    Is.Null,
                    scenePath + " :: " + networkStateType.Name +
                    " must retain scene identity.");
                Assert.That(
                    networkStateObject.transform.IsChildOf(environment),
                    Is.False,
                    scenePath + " :: " + networkStateType.Name +
                    " must remain outside the environment prefab.");
                Assert.That(
                    networkState,
                    Is.InstanceOf<NetworkBehaviour>(),
                    scenePath + " :: " + networkStateType.Name);
                Assert.That(
                    networkStateObject.GetComponent<NetworkObject>(),
                    Is.Not.Null,
                    scenePath + " :: " + networkStateType.Name +
                    " must share its scene object with a NetworkObject.");

                foreach (var sceneOwnedName in sceneOwnedNames)
                {
                    var ownedObjects = sceneObjects
                        .Where(item => item.name == sceneOwnedName)
                        .ToArray();
                    Assert.That(
                        ownedObjects,
                        Is.Not.Empty,
                        scenePath + " :: " + sceneOwnedName);
                    Assert.That(
                        ownedObjects.All(item =>
                            !item.IsChildOf(environment)),
                        Is.True,
                        scenePath + " :: " + sceneOwnedName +
                        " must remain outside the environment prefab.");
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

        private static TestCaseData EnvironmentCase(
            string game,
            string instanceName,
            string prefabFile,
            Type networkStateType,
            params string[] sceneOwnedNames)
        {
            return new TestCaseData(
                    game,
                    instanceName,
                    prefabFile,
                    networkStateType,
                    sceneOwnedNames)
                .SetName(game + "_StaticEnvironmentContract");
        }

        private static Transform FindNamed(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(item => item.name == name);
        }

        private static string[] GetAuthorityColliderNames(string game)
        {
            switch (game)
            {
                case "GiftGrab":
                    return new[] { "Arena Floor" };
                case "Minefield":
                    return new[]
                    {
                        "Arena Floor", "West Wall", "East Wall"
                    };
                case "RedLightGreenLight":
                    return new[]
                    {
                        "Arena Floor", "West Wall", "East Wall",
                        "Start Wall", "Finish Wall"
                    };
                case "TagChase":
                    return new[]
                    {
                        "Arena Floor", "North Boundary", "South Boundary",
                        "West Boundary", "East Boundary"
                    };
                case "TerritoryPaint":
                    return new[]
                    {
                        "Arena Understructure", "North Boundary",
                        "South Boundary", "West Boundary", "East Boundary"
                    };
                case "BalloonBlow":
                case "SequenceMemory":
                    return new[] { "Stage Floor" };
                case "Race":
                    return new[]
                    {
                        "West Boundary", "East Boundary", "South Boundary"
                    };
                case "StableFooting":
                    return new[] { "Arena Understructure" };
                default:
                    return Array.Empty<string>();
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
