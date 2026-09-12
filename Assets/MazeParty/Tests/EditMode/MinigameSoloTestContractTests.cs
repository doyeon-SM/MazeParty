using System.Collections.Generic;
using MazeParty.Dev.MinigameSoloTest;
using NUnit.Framework;
using UnityEditor;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameSoloTestContractTests
    {
        [Test]
        public void Catalog_HasUniqueStableIdsAndValidProductionScenes()
        {
            var ids = new HashSet<MinigameSoloTestId>();
            var descriptors = MinigameSoloTestCatalog.All;

            Assert.That(descriptors, Is.Not.Empty);
            foreach (var descriptor in descriptors)
            {
                Assert.That(
                    ids.Add(descriptor.Id),
                    Is.True,
                    descriptor.Id.ToString());
                Assert.That(descriptor.DisplayName, Is.Not.Empty);
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(
                        descriptor.ScenePath),
                    Is.Not.Null,
                    descriptor.ScenePath);
            }

            Assert.That(
                (byte)MinigameSoloTestId.RedLightGreenLight,
                Is.EqualTo(3),
                "Serialized solo-test IDs must remain stable.");
            Assert.That(
                MinigameSoloTestCatalog.TryGet(
                    MinigameSoloTestId.RedLightGreenLight,
                    out var redLightGreenLight),
                Is.True);
            Assert.That(
                redLightGreenLight.ScenePath,
                Is.EqualTo(
                    MinigameSoloTestCatalog.RedLightGreenLightScenePath));
        }
    }
}
