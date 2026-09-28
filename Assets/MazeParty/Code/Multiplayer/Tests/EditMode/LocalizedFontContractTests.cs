using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class LocalizedFontContractTests
    {
        private static readonly IReadOnlyDictionary<GameLanguage, string> ExpectedAssetPaths =
            new Dictionary<GameLanguage, string>
            {
                { GameLanguage.English, GameFonts.EnglishKoreanAssetPath },
                { GameLanguage.Korean, GameFonts.EnglishKoreanAssetPath },
                { GameLanguage.Japanese, GameFonts.JapaneseAssetPath },
                { GameLanguage.ChineseSimplified, GameFonts.ChineseSimplifiedAssetPath }
            };

        [TearDown]
        public void TearDown()
        {
            GameText.ResetForTests();
        }

        [Test]
        public void LanguageFonts_LoadExpectedAssetsAndRepresentativeGlyphs()
        {
            foreach (var pair in ExpectedAssetPaths)
            {
                var font = GameFonts.Get(pair.Key);
                Assert.That(font, Is.Not.Null, pair.Key.ToString());
                Assert.That(AssetDatabase.GetAssetPath(font), Is.EqualTo(pair.Value));
            }

            Assert.That(GameFonts.Get(GameLanguage.English).HasCharacter('A'), Is.True);
            Assert.That(GameFonts.Get(GameLanguage.Korean).HasCharacter('가'), Is.True);
            Assert.That(GameFonts.Get(GameLanguage.Japanese).HasCharacter('あ'), Is.True);
            Assert.That(GameFonts.Get(GameLanguage.Japanese).HasCharacter('日'), Is.True);
            Assert.That(GameFonts.Get(GameLanguage.ChineseSimplified).HasCharacter('简'), Is.True);
            Assert.That(GameFonts.Get(GameLanguage.ChineseSimplified).HasCharacter('中'), Is.True);
        }

        [Test]
        public void LanguageFonts_HaveCrossLanguageFallbacks()
        {
            AssertFallbacks(
                GameFonts.EnglishKoreanAssetPath,
                GameFonts.JapaneseAssetPath,
                GameFonts.ChineseSimplifiedAssetPath);
            AssertFallbacks(
                GameFonts.JapaneseAssetPath,
                GameFonts.EnglishKoreanAssetPath,
                GameFonts.ChineseSimplifiedAssetPath);
            AssertFallbacks(
                GameFonts.ChineseSimplifiedAssetPath,
                GameFonts.EnglishKoreanAssetPath,
                GameFonts.JapaneseAssetPath);

            AssertRenders(
                GameFonts.Get(GameLanguage.Korean),
                'A', '가', 'あ', '简');
            AssertRenders(
                GameFonts.Get(GameLanguage.Japanese),
                'A', '가', 'あ', '简');
            AssertRenders(
                GameFonts.Get(GameLanguage.ChineseSimplified),
                'A', '가', 'あ', '简');
        }

        [Test]
        public void FontScope_AppliesUiTextAndTextMeshMaterialForCurrentLanguage()
        {
            var root = new GameObject("Font Scope");
            LocalizedFontScope scope = null;
            try
            {
                var uiObject = new GameObject(
                    "UI Text",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                uiObject.transform.SetParent(root.transform, false);
                var uiText = uiObject.GetComponent<Text>();

                var meshObject = new GameObject("World Text", typeof(TextMesh));
                meshObject.transform.SetParent(root.transform, false);
                var textMesh = meshObject.GetComponent<TextMesh>();
                var meshRenderer = meshObject.GetComponent<MeshRenderer>();
                scope = root.AddComponent<LocalizedFontScope>();

                // MonoBehaviour lifetime methods do not run automatically in
                // EditMode, so invoke the authored enable/disable contract.
                InvokeLifecycle(scope, "OnDisable");
                InvokeLifecycle(scope, "OnEnable");
                GameText.SetLanguage(GameLanguage.Japanese);
                var japanese = GameFonts.Get(GameLanguage.Japanese);
                Assert.That(uiText.font, Is.SameAs(japanese));
                Assert.That(textMesh.font, Is.SameAs(japanese));
                Assert.That(meshRenderer.sharedMaterial, Is.SameAs(japanese.material));

                InvokeLifecycle(scope, "OnDisable");
                GameText.SetLanguage(GameLanguage.ChineseSimplified);
                Assert.That(uiText.font, Is.SameAs(japanese),
                    "A disabled scope must stop reacting to language changes.");

                InvokeLifecycle(scope, "OnEnable");
                var chinese = GameFonts.Get(GameLanguage.ChineseSimplified);
                Assert.That(uiText.font, Is.SameAs(chinese));
                Assert.That(textMesh.font, Is.SameAs(chinese));
                Assert.That(meshRenderer.sharedMaterial, Is.SameAs(chinese.material));
            }
            finally
            {
                if (scope != null)
                {
                    InvokeLifecycle(scope, "OnDisable");
                }

                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void EveryPlayerFacingTextPrefab_HasRootFontScope()
        {
            var paths = AssetDatabase.FindAssets(
                    "t:Prefab",
                    new[] { "Assets/MazeParty/Prefabs" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => !path.Contains("/Dev/"))
                .OrderBy(path => path)
                .ToArray();

            var missing = new List<string>();
            foreach (var path in paths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null ||
                    (prefab.GetComponentsInChildren<Text>(true).Length == 0 &&
                     prefab.GetComponentsInChildren<TextMesh>(true).Length == 0))
                {
                    continue;
                }

                if (prefab.GetComponent<LocalizedFontScope>() == null)
                {
                    missing.Add(path);
                }
            }

            Assert.That(missing, Is.Empty,
                "Text prefabs without a root LocalizedFontScope:\n" +
                string.Join("\n", missing));
        }

        private static void AssertFallbacks(string assetPath, params string[] expectedPaths)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TrueTypeFontImporter;
            Assert.That(importer, Is.Not.Null, assetPath);
            var actual = importer.fontReferences
                .Select(AssetDatabase.GetAssetPath)
                .ToArray();
            Assert.That(actual, Is.EqualTo(expectedPaths), assetPath);
        }

        private static void AssertRenders(Font font, params char[] characters)
        {
            const int Size = 32;
            var text = new string(characters);
            font.RequestCharactersInTexture(text, Size, FontStyle.Normal);

            foreach (var character in characters)
            {
                CharacterInfo info;
                Assert.That(
                    font.GetCharacterInfo(character, out info, Size, FontStyle.Normal),
                    Is.True,
                    font.name + " could not render '" + character + "'.");
            }
        }

        private static void InvokeLifecycle(LocalizedFontScope scope, string methodName)
        {
            var method = typeof(LocalizedFontScope).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(scope, null);
        }
    }
}
