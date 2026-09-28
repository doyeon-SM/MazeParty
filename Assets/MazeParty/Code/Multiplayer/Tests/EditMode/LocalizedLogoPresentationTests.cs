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

        [TestCase(GameLanguage.English, true, false)]
        [TestCase(GameLanguage.Korean, false, true)]
        [TestCase(GameLanguage.Japanese, true, false)]
        [TestCase(GameLanguage.ChineseSimplified, true, false)]
        public void LanguageChange_SelectsTheExpectedAuthoredLogo(
            GameLanguage language,
            bool englishVisible,
            bool koreanVisible)
        {
            var root = new GameObject("Localized Logo Contract Root");
            LocalizedLogo localizedLogo = null;
            try
            {
                var englishLogo = new GameObject("English Logo");
                var koreanLogo = new GameObject("Korean Logo");
                englishLogo.transform.SetParent(root.transform, false);
                koreanLogo.transform.SetParent(root.transform, false);

                var initialLanguage = language == GameLanguage.Korean
                    ? GameLanguage.English
                    : GameLanguage.Korean;
                GameText.SetLanguage(initialLanguage);

                localizedLogo = root.AddComponent<LocalizedLogo>();
                localizedLogo.Configure(englishLogo, koreanLogo);
                Assert.That(localizedLogo.HasRequiredReferences, Is.True);

                // EditMode does not consistently dispatch MonoBehaviour lifecycle
                // methods for temporary objects. Clear any automatic subscription,
                // then explicitly simulate the runtime enable lifecycle.
                InvokeLifecycle(localizedLogo, "OnDisable");
                InvokeLifecycle(localizedLogo, "OnEnable");
                GameText.SetLanguage(language);

                Assert.That(englishLogo.activeSelf, Is.EqualTo(englishVisible));
                Assert.That(koreanLogo.activeSelf, Is.EqualTo(koreanVisible));
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
