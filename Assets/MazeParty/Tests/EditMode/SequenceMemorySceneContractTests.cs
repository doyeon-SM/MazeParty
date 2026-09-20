using System.Reflection;
using System.Linq;
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
            "Assets/MazeParty/Scenes/Minigames/SequenceMemory.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/UI/Prefabs/SequenceMemoryHud.prefab";

        [Test]
        public void HudPrefab_HasCompleteSerializedFourPlayerContract()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                HudPrefabPath);
            Assert.That(prefab, Is.Not.Null, HudPrefabPath);
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));

            var hud = prefab.GetComponent<SequenceMemoryHudBindings>();
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.HasRequiredReferences, Is.True);
            Assert.That(hud.RootCanvas, Is.SameAs(prefab.GetComponent<Canvas>()));
            Assert.That(hud.Root, Is.Not.Null);
            Assert.That(hud.NpcSequenceText, Is.Not.Null);
            Assert.That(
                hud.PlayerRows,
                Has.Length.EqualTo(SequenceMemoryRules.PlayerCount));
            Assert.That(
                hud.PlayerNameTexts,
                Has.Length.EqualTo(SequenceMemoryRules.PlayerCount));
            Assert.That(
                hud.PlayerInputTexts,
                Has.Length.EqualTo(SequenceMemoryRules.PlayerCount));
            Assert.That(
                hud.PlayerStatusTexts,
                Has.Length.EqualTo(SequenceMemoryRules.PlayerCount));
            Assert.That(hud.PlayerRows, Has.All.Not.Null);
            Assert.That(hud.PlayerNameTexts, Has.All.Not.Null);
            Assert.That(hud.PlayerInputTexts, Has.All.Not.Null);
            Assert.That(hud.PlayerStatusTexts, Has.All.Not.Null);
        }

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
                             "npcToneSource",
                             "playerToneSource",
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

                foreach (var clipPropertyName in new[]
                         {
                             "highTone",
                             "middleTone",
                             "lowTone"
                         })
                {
                    var property = serializedView.FindProperty(
                        clipPropertyName);
                    Assert.That(property, Is.Not.Null, clipPropertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Null,
                        clipPropertyName +
                        " is intentionally ready for a future AudioClip.");
                }

                var audioSources = view.GetComponents<AudioSource>();
                Assert.That(audioSources, Has.Length.EqualTo(2));
                var npcToneSource = serializedView
                    .FindProperty("npcToneSource")
                    .objectReferenceValue as AudioSource;
                var playerToneSource = serializedView
                    .FindProperty("playerToneSource")
                    .objectReferenceValue as AudioSource;
                Assert.That(npcToneSource, Is.Not.Null);
                Assert.That(playerToneSource, Is.Not.Null);
                Assert.That(npcToneSource, Is.Not.SameAs(playerToneSource));
                Assert.That(audioSources, Does.Contain(npcToneSource));
                Assert.That(audioSources, Does.Contain(playerToneSource));
                Assert.That(audioSources.All(source => !source.playOnAwake));

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
                Assert.That(
                    npcAnchor.Find("NPC Placeholder Body"),
                    Is.Not.Null);
                Assert.That(
                    npcAnchor.Find("NPC Placeholder Head"),
                    Is.Not.Null);

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
        public void BuildSettings_EnableSequenceMemoryAfterRace()
        {
            var enabledScenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            var sequenceIndex = System.Array.IndexOf(
                enabledScenes,
                ScenePath);
            var raceIndex = System.Array.IndexOf(
                enabledScenes,
                "Assets/MazeParty/Scenes/Race.unity");
            Assert.That(sequenceIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(sequenceIndex, Is.EqualTo(raceIndex + 1));
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
