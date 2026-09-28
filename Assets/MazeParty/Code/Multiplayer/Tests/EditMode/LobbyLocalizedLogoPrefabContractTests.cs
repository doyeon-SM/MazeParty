using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class LobbyLocalizedLogoPrefabContractTests
    {
        private const string LobbyPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";
        private const string EnglishLogoAssetPath =
            "Assets/Ignore/AIImage/MazePartyLogo_EN.png";
        private const string KoreanLogoAssetPath =
            "Assets/Ignore/AIImage/MazePartyLogo_KO.png";

        [SetUp]
        public void SetUp()
        {
            GameText.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            GameText.ResetForTests();
        }

        [Test]
        public void LobbyPrefab_AuthorsLocalizedLogosOnAnAlwaysActiveParent()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var localizedLogos =
                    root.GetComponentsInChildren<LocalizedLogo>(true);
                Assert.That(localizedLogos, Has.Length.EqualTo(1),
                    LobbyPrefabPath);

                var localizedLogo = localizedLogos[0];
                Assert.That(localizedLogo.enabled, Is.True);
                Assert.That(localizedLogo.HasRequiredReferences, Is.True);
                AssertAlwaysActiveThroughRoot(localizedLogo.transform, root);

                var serializedLogo = new SerializedObject(localizedLogo);
                var englishLogo = serializedLogo.FindProperty("englishLogo")
                    .objectReferenceValue as GameObject;
                var koreanLogo = serializedLogo.FindProperty("koreanLogo")
                    .objectReferenceValue as GameObject;

                Assert.That(englishLogo, Is.Not.Null);
                Assert.That(koreanLogo, Is.Not.Null);
                Assert.That(englishLogo, Is.Not.SameAs(koreanLogo));
                Assert.That(englishLogo.transform.parent,
                    Is.SameAs(localizedLogo.transform),
                    "The language selector must remain active while either " +
                    "authored logo is hidden.");
                Assert.That(koreanLogo.transform.parent,
                    Is.SameAs(localizedLogo.transform),
                    "Both authored logos must share the selector parent.");

                AssertLogoAsset(englishLogo, EnglishLogoAssetPath);
                AssertLogoAsset(koreanLogo, KoreanLogoAssetPath);
                AssertMatchingLogoLayout(englishLogo, koreanLogo);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void AssertAlwaysActiveThroughRoot(
            Transform localizedLogoParent,
            GameObject prefabRoot)
        {
            for (var current = localizedLogoParent;
                 current != null;
                 current = current.parent)
            {
                Assert.That(current.gameObject.activeSelf, Is.True,
                    current.name +
                    " must stay authored active so language changes are " +
                    "observed even when one logo is hidden.");
                if (current.gameObject == prefabRoot)
                {
                    return;
                }
            }

            Assert.Fail("LocalizedLogo must be authored inside LobbyCanvas.");
        }

        private static void AssertLogoAsset(
            GameObject logoObject,
            string expectedAssetPath)
        {
            var image = logoObject.GetComponent<Image>();
            Assert.That(image, Is.Not.Null, logoObject.name);
            Assert.That(image.sprite, Is.Not.Null, logoObject.name);
            Assert.That(
                AssetDatabase.GetAssetPath(image.sprite),
                Is.EqualTo(expectedAssetPath),
                logoObject.name);
        }

        private static void AssertMatchingLogoLayout(
            GameObject englishLogo,
            GameObject koreanLogo)
        {
            var englishRect = englishLogo.GetComponent<RectTransform>();
            var koreanRect = koreanLogo.GetComponent<RectTransform>();
            Assert.That(englishRect, Is.Not.Null, englishLogo.name);
            Assert.That(koreanRect, Is.Not.Null, koreanLogo.name);
            Assert.That(koreanRect.anchorMin, Is.EqualTo(englishRect.anchorMin));
            Assert.That(koreanRect.anchorMax, Is.EqualTo(englishRect.anchorMax));
            Assert.That(koreanRect.pivot, Is.EqualTo(englishRect.pivot));
            Assert.That(
                koreanRect.anchoredPosition,
                Is.EqualTo(englishRect.anchoredPosition));
            Assert.That(koreanRect.sizeDelta, Is.EqualTo(englishRect.sizeDelta));
            Assert.That(koreanRect.localScale, Is.EqualTo(englishRect.localScale));
        }
    }
}
