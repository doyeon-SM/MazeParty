using System.Linq;
using MazeParty.Gameplay.Minigames.GiftGrab;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class GiftGrabSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/GiftGrab.unity";
        private const string LabelPrefabPath =
            "Assets/MazeParty/UI/Prefabs/GiftGrabBaseLabel.prefab";

        [Test]
        public void Scene_PreservesSymmetricArenaGiftAndPrefabPresentationContract()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var state = roots
                    .SelectMany(root => root.GetComponentsInChildren<
                        NetworkGiftGrabState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);
                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<GiftGrabNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serialized = new SerializedObject(view);
                foreach (var propertyName in new[]
                         {
                             "state", "sharedCamera", "playerRoot",
                             "giftRoot", "pushVfxAnchor", "throwVfxAnchor",
                             "dropVfxAnchor", "arenaPresentation",
                             "cueAudioSource", "hud"
                         })
                {
                    var property = serialized.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }
                foreach (var propertyName in new[]
                         {
                             "playerAnchors", "baseAnchors",
                             "depositedGiftAnchors", "playerLabels",
                             "baseLabels", "stunVfxAnchors"
                         })
                {
                    var property = serialized.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.arraySize,
                        Is.EqualTo(GiftGrabRules.PlayerCount),
                        propertyName);
                    for (var index = 0; index < property.arraySize; index++)
                    {
                        Assert.That(
                            property.GetArrayElementAtIndex(index)
                                .objectReferenceValue,
                            Is.Not.Null,
                            propertyName + "[" + index + "]");
                    }
                }

                var playerAnchors = FindDescendant(
                    state.transform,
                    "Player Anchors");
                var baseAnchors = FindDescendant(
                    state.transform,
                    "Base Anchors");
                var giftAnchors = FindDescendant(
                    state.transform,
                    "Gift Anchors");
                Assert.That(playerAnchors, Is.Not.Null);
                Assert.That(baseAnchors, Is.Not.Null);
                Assert.That(giftAnchors, Is.Not.Null);
                Assert.That(
                    playerAnchors.childCount,
                    Is.EqualTo(GiftGrabRules.PlayerCount));
                Assert.That(
                    baseAnchors.childCount,
                    Is.EqualTo(GiftGrabRules.PlayerCount));
                Assert.That(
                    giftAnchors.childCount,
                    Is.EqualTo(GiftGrabRules.TotalGiftCount));
                for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
                {
                    var baseAnchor = baseAnchors.Find(
                        "Base Anchor " + (slot + 1));
                    var playerAnchor = playerAnchors.Find(
                        "Player Anchor " + (slot + 1));
                    Assert.That(baseAnchor, Is.Not.Null);
                    Assert.That(playerAnchor, Is.Not.Null);
                    var expectedBase = NetworkGiftGrabState.GetBaseCenter(slot);
                    var expectedStart =
                        NetworkGiftGrabState.GetPlayerStartPosition(slot);
                    Assert.That(baseAnchor.position.x,
                        Is.EqualTo(expectedBase.x).Within(0.001f));
                    Assert.That(baseAnchor.position.z,
                        Is.EqualTo(expectedBase.y).Within(0.001f));
                    Assert.That(playerAnchor.position.x,
                        Is.EqualTo(expectedStart.x).Within(0.001f));
                    Assert.That(playerAnchor.position.z,
                        Is.EqualTo(expectedStart.y).Within(0.001f));
                }
                for (var giftId = 0;
                     giftId < GiftGrabRules.TotalGiftCount;
                     giftId++)
                {
                    var gift = giftAnchors.Find(
                        "Gift Anchor " + giftId.ToString("00"));
                    Assert.That(gift, Is.Not.Null, giftId.ToString());
                    Assert.That(gift.Find("Gift Visual"), Is.Not.Null);
                }

                var labels = state.GetComponentsInChildren<
                    GiftGrabBaseLabel>(true);
                Assert.That(
                    labels,
                    Has.Length.EqualTo(GiftGrabRules.PlayerCount * 2));
                foreach (var label in labels)
                {
                    Assert.That(label.HasRequiredReferences, Is.True);
                    Assert.That(
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                            label.gameObject),
                        Is.EqualTo(LabelPrefabPath));
                }

                var cameras = state.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(cameras[0].name, Is.EqualTo(
                    "CM_GiftGrabShared"));
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive scene must reuse Board's output Camera.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);
                Assert.That(
                    FindDescendant(state.transform, "Art Replacement Anchors"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "VFX Replacement Anchors"),
                    Is.Not.Null);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindDescendant(root.GetChild(index), childName);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }
}
