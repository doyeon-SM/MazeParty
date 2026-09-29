using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class LocalizedLogoPresentationTests
    {
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
        public void LanguageChange_SelectsTheExpectedAuthoredLogo()
        {
            var cases = new[]
            {
                (GameLanguage.English, true, false),
                (GameLanguage.Korean, false, true),
                (GameLanguage.Japanese, true, false),
                (GameLanguage.ChineseSimplified, true, false)
            };

            foreach (var testCase in cases)
            {
                var root = new GameObject("Localized Logo Contract Root");
                LocalizedLogo localizedLogo = null;
                try
                {
                    var englishLogo = new GameObject("English Logo");
                    var koreanLogo = new GameObject("Korean Logo");
                    englishLogo.transform.SetParent(root.transform, false);
                    koreanLogo.transform.SetParent(root.transform, false);

                    var initialLanguage =
                        testCase.Item1 == GameLanguage.Korean
                            ? GameLanguage.English
                            : GameLanguage.Korean;
                    GameText.SetLanguage(initialLanguage);

                    localizedLogo = root.AddComponent<LocalizedLogo>();
                    localizedLogo.Configure(englishLogo, koreanLogo);
                    Assert.That(
                        localizedLogo.HasRequiredReferences,
                        Is.True,
                        testCase.Item1.ToString());

                    // EditMode does not consistently dispatch MonoBehaviour
                    // lifecycle methods for temporary objects. Clear any
                    // automatic subscription, then explicitly simulate the
                    // runtime enable lifecycle.
                    InvokeLifecycle(localizedLogo, "OnDisable");
                    InvokeLifecycle(localizedLogo, "OnEnable");
                    GameText.SetLanguage(testCase.Item1);

                    Assert.That(
                        englishLogo.activeSelf,
                        Is.EqualTo(testCase.Item2),
                        testCase.Item1.ToString());
                    Assert.That(
                        koreanLogo.activeSelf,
                        Is.EqualTo(testCase.Item3),
                        testCase.Item1.ToString());
                }
                finally
                {
                    if (localizedLogo != null)
                    {
                        InvokeLifecycle(localizedLogo, "OnDisable");
                    }

                    Object.DestroyImmediate(root);
                }
            }
        }

        private static void InvokeLifecycle(
            LocalizedLogo localizedLogo,
            string methodName)
        {
            var method = typeof(LocalizedLogo).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(localizedLogo, null);
        }
    }
}
