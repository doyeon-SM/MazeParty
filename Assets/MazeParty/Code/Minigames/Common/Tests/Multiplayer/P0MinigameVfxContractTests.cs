using System;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
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
        private const string LightningStrikePath =
            "Assets/MazeParty/Prefabs/Common/VFX/LightningStrike.prefab";
        private static readonly TestCaseData[] SceneContracts =
        {
            SceneCase(
                "ArenaCombat", typeof(ArenaCombatNetworkView),
                ("hitSparkVfxPrefab", HitSparkPath)),
            SceneCase(
                "BalloonBlow", typeof(BalloonBlowNetworkView),
                ("popBurstVfxPrefab", ArrivalFireworksPath)),
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
                ("finishVfxPrefab", ArrivalFireworksPath),
                ("penaltyVfxPrefab", LightningStrikePath)),
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
