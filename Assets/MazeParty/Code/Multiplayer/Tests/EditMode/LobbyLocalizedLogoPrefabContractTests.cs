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
                var intro = root.GetComponent<LobbyIntroPresentation>();
                Assert.That(intro, Is.Not.Null);
                Assert.That(intro.HasRequiredReferences, Is.True);
                var introBindings = new SerializedObject(intro);
                var introVideo = (UnityEngine.Video.VideoPlayer)introBindings
                    .FindProperty("introPlayer").objectReferenceValue;
                var loopVideo = (UnityEngine.Video.VideoPlayer)introBindings
                    .FindProperty("loopPlayer").objectReferenceValue;
                Assert.That(introVideo.playOnAwake || loopVideo.playOnAwake, Is.False,
                    "Only the presentation lifecycle may start video and audio.");
                Assert.That(introVideo.isLooping, Is.False);
                Assert.That(loopVideo.isLooping, Is.True);
                foreach (var video in new[] { introVideo, loopVideo })
                {
                    Assert.That(video.transform.IsChildOf(root.transform), Is.True);
                    Assert.That(video.sendFrameReadyEvents, Is.True);
                    Assert.That(video.GetTargetAudioSource(0).GetComponent<AudioChannelSource>().Channel,
                        Is.EqualTo(AudioChannel.Sfx));
                    Assert.That(video.clip, Is.Not.Null);
                    Assert.That(EditorUtility.IsPersistent(video.clip), Is.True);
                }
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

                AssertLogoAsset(englishLogo);
                AssertLogoAsset(koreanLogo);
                Assert.That(englishLogo.GetComponent<Image>().sprite,
                    Is.Not.SameAs(koreanLogo.GetComponent<Image>().sprite));
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

        private static void AssertLogoAsset(GameObject logoObject)
        {
            var image = logoObject.GetComponent<Image>();
            Assert.That(image, Is.Not.Null, logoObject.name);
            Assert.That(image.sprite, Is.Not.Null, logoObject.name);
            Assert.That(EditorUtility.IsPersistent(image.sprite), Is.True, logoObject.name);
        }

    }
}
