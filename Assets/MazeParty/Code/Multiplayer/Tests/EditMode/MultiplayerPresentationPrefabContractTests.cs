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
    public sealed class MultiplayerPresentationPrefabContractTests
    {
        private const string PlayerPresentationPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/PlayerAvatarPresentation.prefab";
        private const string PlayerWorldIndicatorPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/PlayerWorldIndicator.prefab";
        private const string NetworkPlayerPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/NetworkPlayer.prefab";
        private const string LobbyArenaPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/World/LobbyArena.prefab";
        private const string BootstrapScenePath =
            "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity";

        [Test]
        public void PlayerPresentationPrefab_HasCompleteAuthoredBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerPresentationPrefabPath);
            Assert.That(prefab, Is.Not.Null, PlayerPresentationPrefabPath);
            var bindings = prefab.GetComponent<
                PlayerAvatarPresentationBindings>();
            Assert.That(bindings, Is.Not.Null);
            Assert.That(bindings.HasRequiredReferences, Is.True);
            Assert.That(bindings.NameplateAnchor.gameObject.activeSelf, Is.True);
            Assert.That(bindings.NameText, Is.Not.Null);
            Assert.That(bindings.BodyTintRenderers, Is.Not.Empty);
            AssertHitZone(bindings.BodyHitbox, PlayerHitRegion.Body);
            AssertHitZone(bindings.HeadHitbox, PlayerHitRegion.Head);
            AssertHitZone(bindings.LeftHandHitbox, PlayerHitRegion.Hand);
            AssertHitZone(bindings.RightHandHitbox, PlayerHitRegion.Hand);
            Assert.That(
                prefab.GetComponentsInChildren<Transform>(true)
                    .Sum(child => GameObjectUtility
                        .GetMonoBehavioursWithMissingScriptCount(
                            child.gameObject)),
                Is.Zero,
                "The shared presentation must not contain missing scripts.");
            Assert.That(
                prefab.GetComponentsInChildren<NetworkObject>(true),
                Is.Empty,
                "The shared presentation must remain local-only.");
            Assert.That(
                prefab.GetComponentsInChildren<CharacterController>(true),
                Is.Empty);

            var assets = Resources.Load<PlayerAvatarPresentationAssets>(
                PlayerAvatarPresentationAssets.ResourcePath);
            Assert.That(assets, Is.Not.Null);
            Assert.That(assets.HasRequiredReferences, Is.True);
            Assert.That(
                AssetDatabase.GetAssetPath(assets.PresentationPrefab),
                Is.EqualTo(PlayerPresentationPrefabPath));
            Assert.That(
                AssetDatabase.GetAssetPath(assets.WorldIndicatorPrefab),
                Is.EqualTo(PlayerWorldIndicatorPrefabPath));
        }

        [Test]
        public void PlayerWorldIndicatorPrefab_HasAuthoredNameAndStartMarker()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerWorldIndicatorPrefabPath);
            Assert.That(prefab, Is.Not.Null, PlayerWorldIndicatorPrefabPath);
            var indicator = prefab.GetComponent<PlayerWorldIndicator>();
            Assert.That(indicator, Is.Not.Null);
            Assert.That(indicator.HasRequiredReferences, Is.True);
            Assert.That(indicator.NameplateAnchor, Is.Not.Null);
            Assert.That(indicator.NameText, Is.Not.Null);
            Assert.That(indicator.LocalStartHighlight, Is.Not.Null);
            Assert.That(indicator.LocalStartHighlight.activeSelf, Is.False);
            Assert.That(
                prefab.GetComponentsInChildren<Canvas>(true),
                Is.Empty,
                "The shared marker must remain an authored world-space prefab.");
        }

        [Test]
        public void PlayerAvatarVisual_InstantiatesOneSharedPresentation()
        {
            var root = new GameObject("Player Presentation Contract Root");
            try
            {
                var visual = root.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.EnsureBuilt();
                Assert.That(visual.Bindings, Is.Not.Null);
                Assert.That(visual.Bindings.HasRequiredReferences, Is.True);
                Assert.That(
                    root.GetComponentsInChildren<
                        PlayerAvatarPresentationBindings>(true),
                    Has.Length.EqualTo(1));
                var assets = Resources.Load<PlayerAvatarPresentationAssets>(
                    PlayerAvatarPresentationAssets.ResourcePath);
                Assert.That(assets, Is.Not.Null);
                Assert.That(
                    visual.Bindings.gameObject.name,
                    Is.EqualTo(assets.PresentationPrefab.gameObject.name));

                var authoredHighlight = visual.Bindings.TopViewHighlight;
                var authoredObjectCount = root
                    .GetComponentsInChildren<Transform>(true)
                    .Length;
                visual.SetTopViewHighlight(true);
                Assert.That(authoredHighlight.activeSelf, Is.True);
                Assert.That(
                    root.GetComponentsInChildren<Transform>(true).Length,
                    Is.EqualTo(authoredObjectCount),
                    "Highlight toggling must not create runtime geometry.");
                visual.SetTopViewHighlight(false);
                Assert.That(authoredHighlight.activeSelf, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NetworkPlayer_UsesNestedAuthoredPresentationAndEyePivot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NetworkPlayerPrefabPath);
            Assert.That(prefab, Is.Not.Null, NetworkPlayerPrefabPath);
            var visual = prefab.GetComponent<PlayerAvatarVisual>();
            var avatar = prefab.GetComponent<NetworkPlayerAvatar>();
            Assert.That(visual, Is.Not.Null);
            Assert.That(avatar, Is.Not.Null);
            Assert.That(visual.Bindings, Is.Not.Null);
            Assert.That(visual.Bindings.HasRequiredReferences, Is.True);
            var source = PrefabUtility.GetCorrespondingObjectFromSource(
                visual.Bindings);
            Assert.That(source, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(source),
                Is.EqualTo(PlayerPresentationPrefabPath));

            var eyePivot = prefab.transform.Find("CameraPivot");
            Assert.That(eyePivot, Is.Not.Null);
            var serializedAvatar = new SerializedObject(avatar);
            Assert.That(
                serializedAvatar.FindProperty("eyePivot").objectReferenceValue,
                Is.SameAs(eyePivot));
            Assert.That(prefab.GetComponent<PlayerHitZoneOwner>(), Is.Not.Null);
        }

        [Test]
        public void LobbyArenaPrefabAndBootstrap_HaveAuthoredContracts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LobbyArenaPrefabPath);
            Assert.That(prefab, Is.Not.Null, LobbyArenaPrefabPath);
            var prefabArena = prefab.GetComponent<LobbyArena>();
            Assert.That(prefabArena, Is.Not.Null);
            Assert.That(prefabArena.HasRequiredReferences, Is.True);
            Assert.That(prefabArena.Bindings.PresentationRoot, Is.Not.Null);
            Assert.That(
                prefabArena.Bindings.PresentationRoot
                    .GetComponentsInChildren<Renderer>(true),
                Is.Not.Empty,
                "Lobby room visuals must be authored in the prefab.");
            Assert.That(
                prefabArena.SpawnCount,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));
            Assert.That(prefabArena.Bindings.HorizontalBounds.enabled, Is.False);

            var scene = SceneManager.GetSceneByPath(BootstrapScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    BootstrapScenePath,
                    OpenSceneMode.Additive);
            }
            try
            {
                var arenas = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<LobbyArena>(true))
                    .ToArray();
                Assert.That(arenas, Has.Length.EqualTo(1));
                Assert.That(arenas[0].HasRequiredReferences, Is.True);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        arenas[0].gameObject),
                    Is.EqualTo(LobbyArenaPrefabPath));
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void AssertHitZone(
            Collider collider,
            PlayerHitRegion expectedRegion)
        {
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.isTrigger, Is.True);
            var zone = collider.GetComponent<PlayerHitZone>();
            Assert.That(zone, Is.Not.Null, collider.gameObject.name);
            Assert.That(zone.Region, Is.EqualTo(expectedRegion));
        }
    }
}
