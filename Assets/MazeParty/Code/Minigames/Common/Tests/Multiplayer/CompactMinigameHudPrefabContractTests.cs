using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class CompactMinigameHudPrefabContractTests
    {
        private const string PrefabDirectory =
            "Assets/MazeParty/Prefabs/Minigames/";

        [TestCase("StableFooting")]
        [TestCase("BalloonBlow")]
        [TestCase("GiftGrab")]
        public void ResultPanel_UsesSeparatePrefabOwnedCanvasAndStartsHidden(
            string minigame)
        {
            var path = PrefabDirectory + minigame + "/UI/" + minigame + "Hud.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(prefab, Is.Not.Null, path);
            Assert.That(AssetDatabase.GetAssetPath(prefab), Is.EqualTo(path));
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(prefab), Is.True);

            MonoBehaviour binding;
            GameObject resultPanel;
            switch (minigame)
            {
                case "StableFooting":
                    var stable = prefab.GetComponent<StableFootingHudBindings>();
                    binding = stable;
                    resultPanel = stable != null ? stable.ResultPanel : null;
                    break;
                case "BalloonBlow":
                    var balloon = prefab.GetComponent<BalloonBlowHudBindings>();
                    binding = balloon;
                    resultPanel = balloon != null ? balloon.ResultPanel : null;
                    break;
                default:
                    var gift = prefab.GetComponent<GiftGrabHudBindings>();
                    binding = gift;
                    resultPanel = gift != null ? gift.ResultPanel : null;
                    break;
            }

            Assert.That(binding, Is.Not.Null, path);
            var serializedResult = new SerializedObject(binding)
                .FindProperty("resultPanel");
            Assert.That(serializedResult, Is.Not.Null, path);
            Assert.That(serializedResult.objectReferenceValue,
                Is.SameAs(resultPanel), path);
            Assert.That(resultPanel, Is.Not.Null, path);
            Assert.That(resultPanel.transform.IsChildOf(prefab.transform),
                Is.True, path);
            Assert.That(resultPanel.activeSelf, Is.False, path);

            var hudCanvas = prefab.GetComponent<Canvas>();
            var resultCanvas = resultPanel.GetComponent<Canvas>();
            Assert.That(hudCanvas, Is.Not.Null, path);
            Assert.That(resultCanvas, Is.Not.Null, path);
            Assert.That(resultCanvas, Is.Not.SameAs(hudCanvas), path);
            Assert.That(resultCanvas.overrideSorting, Is.True, path);
        }
    }
}
