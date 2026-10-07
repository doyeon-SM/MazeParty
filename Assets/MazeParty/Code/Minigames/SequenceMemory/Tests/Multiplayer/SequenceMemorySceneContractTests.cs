using System.Reflection;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class SequenceMemorySceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/SequenceMemory/SequenceMemory.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/UI/SequenceMemoryHud.prefab";
        private const string PlayerPresentationPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/PlayerAvatarPresentation.prefab";

        [Test]
        public void Scene_PreservesFixedStationsSharedCameraAudioAndPrefabUi()
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
                        NetworkSequenceMemoryState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);

                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<SequenceMemoryNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serializedView = new SerializedObject(view);
                foreach (var propertyName in new[]
                         {
                             "state",
                             "sharedCamera",
                             "playerRoot",
                             "npcAnchor",
                             "arenaPresentation",
                             "hud"
                         })
                {
                    var property = serializedView.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }

                var playerAnchors = serializedView.FindProperty(
                    "playerAnchors");
                Assert.That(playerAnchors, Is.Not.Null);
                Assert.That(
                    playerAnchors.arraySize,
                    Is.EqualTo(SequenceMemoryRules.PlayerCount));
                for (var slot = 0;
                     slot < SequenceMemoryRules.PlayerCount;
                     slot++)
                {
                    Assert.That(
                        playerAnchors.GetArrayElementAtIndex(slot)
                            .objectReferenceValue,
                        Is.Not.Null,
                        "playerAnchors[" + slot + "]");
                }

                var audioSources = view.GetComponents<AudioSource>();
                Assert.That(audioSources, Is.Empty,
                    "Sequence tones use independent shared SoundSystem voices.");

                var playerAnchorRoot = FindDescendant(
                    state.transform,
                    "Player Anchors");
                var stationRoot = FindDescendant(
                    state.transform,
                    "Station Placeholders");
                Assert.That(playerAnchorRoot, Is.Not.Null);
                Assert.That(stationRoot, Is.Not.Null);
                Assert.That(
                    playerAnchorRoot.childCount,
                    Is.EqualTo(SequenceMemoryRules.PlayerCount));
                Assert.That(
                    stationRoot.childCount,
                    Is.EqualTo(SequenceMemoryRules.PlayerCount));
                for (var slot = 1;
                     slot <= SequenceMemoryRules.PlayerCount;
                     slot++)
                {
                    Assert.That(
                        playerAnchorRoot.Find("Player Anchor " + slot),
                        Is.Not.Null);
                    Assert.That(
                        stationRoot.Find("Station Placeholder " + slot),
                        Is.Not.Null);
                }

                var npcAnchor = FindDescendant(
                    state.transform,
                    "NPC Anchor");
                Assert.That(npcAnchor, Is.Not.Null);
                Assert.That(npcAnchor.Find("NPC Podium"), Is.Not.Null);
                Assert.That(
                    npcAnchor.Find("NPC Placeholder Body"),
                    Is.Null);
                Assert.That(
                    npcAnchor.Find("NPC Placeholder Head"),
                    Is.Null);
                var npcPresentation = npcAnchor.Find(
                    "NPC Player Presentation");
                Assert.That(npcPresentation, Is.Not.Null);
                Assert.That(
                    npcPresentation.GetComponent<
                        PlayerAvatarPresentationBindings>(),
                    Is.Not.Null);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        npcPresentation.gameObject),
                    Is.EqualTo(PlayerPresentationPrefabPath));
                Assert.That(
                    npcPresentation.GetComponentsInChildren<Collider>(true)
                        .Any(collider => collider.enabled),
                    Is.False,
                    "The NPC reuses player visuals without player hitboxes.");

                var hud = state.GetComponentInChildren<
                    SequenceMemoryHudBindings>(true);
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.HasRequiredReferences, Is.True);
                Assert.That(hud.transform.localScale, Is.EqualTo(Vector3.one));
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        hud.gameObject),
                    Is.EqualTo(HudPrefabPath));
                var canvases = roots.SelectMany(root =>
                    root.GetComponentsInChildren<Canvas>(true)).ToArray();
                Assert.That(canvases, Has.Length.EqualTo(1));
                Assert.That(canvases[0], Is.SameAs(hud.RootCanvas));

                var cameras = state.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(
                    cameras[0].name,
                    Is.EqualTo("CM_SequenceMemoryShared"));
                Assert.That(
                    cameras[0].transform.position,
                    Is.EqualTo(new Vector3(1140f, 12f, -14f)));
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive scene must reuse Board's output Camera.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void NetworkInput_UsesOwnerAuthorityAndReliableSharedTones()
        {
            const BindingFlags privateInstance =
                BindingFlags.Instance | BindingFlags.NonPublic;
            var serverMatchField = typeof(NetworkSequenceMemoryState)
                .GetField("_serverMatch", privateInstance);
            Assert.That(serverMatchField, Is.Not.Null);
            Assert.That(
                serverMatchField.FieldType,
                Is.EqualTo(typeof(SequenceMemoryMatchState)),
                "The unrevealed problem set must remain server-local.");

            var inputRpc = typeof(NetworkPlayerAvatar).GetMethod(
                "SubmitSequenceMemoryInputRpc",
                privateInstance);
            Assert.That(inputRpc, Is.Not.Null);
            var inputRpcAttribute = inputRpc.GetCustomAttribute<
                RpcAttribute>();
            Assert.That(inputRpcAttribute, Is.Not.Null);
            Assert.That(
                inputRpcAttribute.InvokePermission,
                Is.EqualTo(RpcInvokePermission.Owner));

            var toneRpc = typeof(NetworkSequenceMemoryState).GetMethod(
                "PlayToneRpc",
                privateInstance);
            Assert.That(toneRpc, Is.Not.Null);
            var toneRpcAttribute = toneRpc.GetCustomAttribute<RpcAttribute>();
            Assert.That(toneRpcAttribute, Is.Not.Null);
            Assert.That(
                toneRpcAttribute.Delivery,
                Is.EqualTo(RpcDelivery.Reliable),
                "Every accepted player tone must reach every client in order.");
            var toneParameters = toneRpc.GetParameters();
            Assert.That(toneParameters, Has.Length.EqualTo(3));
            Assert.That(toneParameters[2].ParameterType, Is.EqualTo(typeof(int)));
            Assert.That(toneParameters[2].Name, Is.EqualTo("actorSlot"),
                "Shared tone presentation must identify the authoritative actor.");
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
                var found = FindDescendant(
                    root.GetChild(index),
                    childName);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
