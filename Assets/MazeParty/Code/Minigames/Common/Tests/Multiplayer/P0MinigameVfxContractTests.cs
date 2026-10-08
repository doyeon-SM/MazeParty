using System;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    /// <summary>
    /// One durable authored-asset contract for the production minigame VFX pass.
    /// Individual games keep their gameplay/state contracts elsewhere.
    /// </summary>
    public sealed class P0MinigameVfxContractTests
    {
        private const string HitSparkPath =
            "Assets/MazeParty/Prefabs/Common/VFX/HitSpark.prefab";
        private const string ExplosionPath =
            "Assets/MazeParty/Prefabs/Common/VFX/CartoonExplosion.prefab";
        private const string TaggerAuraPath =
            "Assets/MazeParty/Prefabs/Minigames/Common/VFX/TaggerAura.prefab";
        private const string ArrivalFireworksPath =
            "Assets/MazeParty/Prefabs/Minigames/Common/VFX/" +
            "ArrivalFireworks.prefab";
        private const string GrabPassShaderPath =
            "Assets/Ignore/AllIn1VfxToolkit/Shaders/" +
            "AllIn1VfxGrabPass.shader";

        private static readonly TestCaseData[] SceneContracts =
        {
            SceneCase(
                "ArenaCombat", typeof(ArenaCombatNetworkView),
                ("hitSparkVfxPrefab", HitSparkPath)),
            SceneCase(
                "BalloonBlow", typeof(BalloonBlowNetworkView),
                ("popBurstVfxPrefab", ExplosionPath)),
            SceneCase(
                "BouncingBalls", typeof(BouncingBallsNetworkView),
                ("goalBurstVfxPrefab", HitSparkPath)),
            SceneCase(
                "CliffBarrage", typeof(CliffBarrageNetworkView),
                ("hitSparkVfxPrefab", HitSparkPath)),
            SceneCase(
                "GiftGrab", typeof(GiftGrabNetworkView),
                ("actionBurstVfxPrefab", HitSparkPath)),
            SceneCase(
                "Minefield", typeof(MinefieldNetworkView),
                ("mineExplosionVfxPrefab", ExplosionPath),
                ("hitSparkVfxPrefab", HitSparkPath),
                ("finishVfxPrefab", ArrivalFireworksPath)),
            SceneCase(
                "Race", typeof(RaceNetworkView),
                ("progressVfxPrefab", HitSparkPath),
                ("finishVfxPrefab", ArrivalFireworksPath)),
            SceneCase(
                "RedLightGreenLight",
                typeof(RedLightGreenLightNetworkView),
                ("signalPulseVfxPrefab", HitSparkPath),
                ("finishVfxPrefab", ArrivalFireworksPath)),
            SceneCase(
                "SequenceMemory", typeof(SequenceMemoryNetworkView),
                ("tonePulseVfxPrefab", HitSparkPath)),
            SceneCase(
                "SnowySpin", typeof(SnowySpinNetworkView),
                ("fallImpactVfxPrefab", HitSparkPath)),
            SceneCase(
                "StableFooting", typeof(StableFootingNetworkView),
                ("interactionVfxPrefab", HitSparkPath)),
            SceneCase(
                "TagChase", typeof(TagChaseNetworkView),
                ("hitSparkVfxPrefab", HitSparkPath),
                ("taggerAuraPrefab", TaggerAuraPath)),
            SceneCase(
                "TerritoryPaint", typeof(TerritoryPaintNetworkView),
                ("paintSplashVfxPrefab", HitSparkPath)),
            SceneCase(
                "WrongWay", typeof(WrongWayNetworkView),
                ("progressVfxPrefab", HitSparkPath),
                ("finishVfxPrefab", ArrivalFireworksPath))
        };

        [Test]
        public void ArrivalFireworks_IsPooledAllIn1PresentationOnlyVfx()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                ArrivalFireworksPath);
            Assert.That(prefab, Is.Not.Null, ArrivalFireworksPath);

            var pooled = prefab.GetComponent<PooledOneShotVfx>();
            Assert.That(pooled, Is.Not.Null, ArrivalFireworksPath);
            Assert.That(pooled.ParticleSystems, Is.Not.Empty,
                ArrivalFireworksPath);
            Assert.That(pooled.ParticleSystems.All(item => item != null),
                Is.True, ArrivalFireworksPath);
            Assert.That(pooled.FlashLights, Has.Length.GreaterThanOrEqualTo(2),
                ArrivalFireworksPath);
            Assert.That(pooled.FlashLights.All(item => item != null),
                Is.True, ArrivalFireworksPath);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty, ArrivalFireworksPath);
            Assert.That(prefab.GetComponentsInChildren<Collider2D>(true),
                Is.Empty, ArrivalFireworksPath);
            Assert.That(prefab.GetComponentsInChildren<NetworkObject>(true),
                Is.Empty, ArrivalFireworksPath);

            var behaviours = prefab
                .GetComponentsInChildren<MonoBehaviour>(true);
            Assert.That(behaviours.Any(item => item == null),
                Is.False,
                ArrivalFireworksPath + " contains a missing script.");
            Assert.That(behaviours.Where(item =>
                    item is not PooledOneShotVfx),
                Is.Empty,
                ArrivalFireworksPath +
                " must not carry vendor or gameplay scripts.");

            var transforms = prefab.GetComponentsInChildren<Transform>(true);
            Assert.That(transforms.Any(item =>
                    item.name.IndexOf(
                        "Distort", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.name.IndexOf(
                        "GrabPass", StringComparison.OrdinalIgnoreCase) >= 0),
                Is.False,
                ArrivalFireworksPath +
                " must remove distortion and GrabPass objects.");

            var dependencies = AssetDatabase.GetDependencies(
                    ArrivalFireworksPath,
                    true)
                .Select(path => path.Replace('\\', '/'))
                .ToArray();
            Assert.That(dependencies.Any(path => path.StartsWith(
                    "Assets/Ignore/AllIn1VfxToolkit/",
                    StringComparison.OrdinalIgnoreCase)),
                Is.True,
                ArrivalFireworksPath +
                " must retain authored AllIn1 VFX dependencies.");
            Assert.That(dependencies.Any(path => string.Equals(
                    path,
                    GrabPassShaderPath,
                    StringComparison.OrdinalIgnoreCase)),
                Is.False,
                ArrivalFireworksPath +
                " must not depend on AllIn1VfxGrabPass.shader.");
        }

        [Test]
        public void TaggerAura_IsAuthoredVfxAndPresentationOnly()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                TaggerAuraPath);
            Assert.That(prefab, Is.Not.Null, TaggerAuraPath);
            Assert.That(prefab.GetComponentsInChildren<ParticleSystem>(true),
                Is.Not.Empty, TaggerAuraPath);
            Assert.That(prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty, TaggerAuraPath);
            Assert.That(prefab.GetComponentsInChildren<Collider2D>(true),
                Is.Empty, TaggerAuraPath);
            Assert.That(prefab.GetComponentsInChildren<NetworkObject>(true),
                Is.Empty, TaggerAuraPath);
            Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true),
                Is.Empty,
                TaggerAuraPath + " must not keep vendor runtime scripts.");
            var transforms = prefab.GetComponentsInChildren<Transform>(true);
            var unsupported = transforms
                .Where(item =>
                    item.name.IndexOf(
                        "Distort", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    item.name.IndexOf(
                        "GrabPass", StringComparison.OrdinalIgnoreCase) >= 0)
                .ToArray();
            Assert.That(
                unsupported,
                Is.Empty,
                TaggerAuraPath +
                " must remove GrabPass/distortion objects for URP.");

            var materials = prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(item => item.sharedMaterials)
                .Where(item => item != null)
                .ToArray();
            Assert.That(materials.Any(item =>
                    item.shader != null &&
                    (item.shader.name.IndexOf(
                         "Distort", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     item.shader.name.IndexOf(
                         "GrabPass", StringComparison.OrdinalIgnoreCase) >= 0)),
                Is.False,
                TaggerAuraPath + " has an unsupported material shader.");
            Assert.That(AssetDatabase.GetDependencies(TaggerAuraPath, true)
                    .Any(path => string.Equals(
                        path.Replace('\\', '/'),
                        GrabPassShaderPath,
                        StringComparison.OrdinalIgnoreCase)),
                Is.False,
                TaggerAuraPath +
                " must not depend on AllIn1VfxGrabPass.shader.");
        }

        [TestCaseSource(nameof(SceneContracts))]
        public void ProductionScenes_SerializeAuthoredVfxBindings(
            string game,
            Type viewType,
            string[] fieldNames,
            string[] expectedAssetPaths)
        {
            var scenePath = "Assets/MazeParty/Scenes/Minigames/" + game +
                            "/" + game + ".unity";
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
                var views = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren(viewType, true))
                    .Cast<Component>()
                    .ToArray();
                Assert.That(
                    views,
                    Has.Length.EqualTo(1),
                    scenePath + " must contain exactly one " + viewType.Name);

                var serializedView = new SerializedObject(views[0]);
                for (var index = 0; index < fieldNames.Length; index++)
                {
                    var property = serializedView.FindProperty(
                        fieldNames[index]);
                    Assert.That(
                        property,
                        Is.Not.Null,
                        viewType.Name + "." + fieldNames[index]);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        scenePath + " :: " + fieldNames[index]);
                    Assert.That(
                        AssetDatabase.GetAssetPath(
                            property.objectReferenceValue),
                        Is.EqualTo(expectedAssetPaths[index]),
                        scenePath + " :: " + fieldNames[index]);
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

        private static TestCaseData SceneCase(
            string game,
            Type viewType,
            params (string field, string assetPath)[] bindings)
        {
            return new TestCaseData(
                    game,
                    viewType,
                    bindings.Select(item => item.field).ToArray(),
                    bindings.Select(item => item.assetPath).ToArray())
                .SetName(game + "_VfxBindings");
        }
    }
}
