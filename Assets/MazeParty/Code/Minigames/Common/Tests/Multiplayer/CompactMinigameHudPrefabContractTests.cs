using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class CompactMinigameHudPrefabContractTests
    {
        [Test]
        public void StableFootingHud_ContainsOnlyEssentialInstructionPanel()
        {
            const string path =
                "Assets/MazeParty/Prefabs/Minigames/StableFooting/UI/" +
                "StableFootingHud.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);

            var binding = prefab.GetComponent<StableFootingHudBindings>();
            Assert.That(binding, Is.Not.Null, path);
            Assert.That(binding.HasRequiredReferences, Is.True, path);
            Assert.That(binding.InstructionText, Is.Not.Null, path);
            Assert.That(
                prefab.transform.Find("ResultPanel"),
                Is.Null,
                "Round completion uses the shared minigame flow instead of " +
                "a content-free duplicate panel.");
            Assert.That(
                prefab.GetComponentsInChildren<Canvas>(true),
                Has.Length.EqualTo(1));
        }
    }
}
