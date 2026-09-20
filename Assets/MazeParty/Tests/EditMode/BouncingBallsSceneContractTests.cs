using System.Linq;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BouncingBallsSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/BouncingBalls.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/UI/Prefabs/BouncingBallsHud.prefab";

        [Test]
        public void HudPrefab_HasFourPlayerScoresAndSharedTimer()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            Assert.That(prefab, Is.Not.Null, HudPrefabPath);
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            var hud = prefab.GetComponent<BouncingBallsHudBindings>();
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.HasRequiredReferences, Is.True);
            Assert.That(hud.RootCanvas, Is.SameAs(prefab.GetComponent<Canvas>()));
            Assert.That(hud.TimerDial, Is.Not.Null);
            Assert.That(hud.PlayerNameTexts, Has.Length.EqualTo(4));
            Assert.That(hud.PlayerScoreTexts, Has.Length.EqualTo(4));
            Assert.That(hud.PlayerConcededTexts, Has.Length.EqualTo(4));
            Assert.That(hud.PlayerNameTexts, Has.All.Not.Null);
            Assert.That(hud.PlayerScoreTexts, Has.All.Not.Null);
            Assert.That(hud.PlayerConcededTexts, Has.All.Not.Null);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    hud.TimerDial.gameObject),
                Is.EqualTo("Assets/MazeParty/UI/Prefabs/MinigameTimerDial.prefab"));
        }

        [Test]
        public void Scene_HasFourGoalsShieldsThreeBallsAndOneSharedCamera()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var state = roots.SelectMany(root =>
                    root.GetComponentsInChildren<NetworkBouncingBallsState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);
                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<BouncingBallsNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serialized = new SerializedObject(view);
                foreach (var name in new[]
                         {
                             "state", "sharedCamera", "arenaPresentation", "hud"
                         })
                {
                    var property = serialized.FindProperty(name);
                    Assert.That(property, Is.Not.Null, name);
                    Assert.That(property.objectReferenceValue, Is.Not.Null, name);
                }
                AssertArray(serialized, "shieldTransforms", 4);
                AssertArray(serialized, "shieldRenderers", 4);
                AssertArray(serialized, "ballTransforms", 3);
                AssertArray(serialized, "ballRenderers", 3);

                var arena = FindDescendant(state.transform, "Arena Presentation");
                Assert.That(arena, Is.Not.Null);
                Assert.That(arena.position, Is.EqualTo(new Vector3(1260f, 0f, 0f)));
                Assert.That(FindDescendant(arena, "Goals")?.childCount,
                    Is.EqualTo(4));
                Assert.That(FindDescendant(arena, "Shields")?.childCount,
                    Is.EqualTo(4));
                Assert.That(FindDescendant(arena, "Balls")?.childCount,
                    Is.EqualTo(3));
                var expectedShieldPositions = new[]
                {
                    new Vector3(0f, -7.35f, 0.38f),
                    new Vector3(7.35f, 0f, 0.38f),
                    new Vector3(0f, 7.35f, 0.38f),
                    new Vector3(-7.35f, 0f, 0.38f)
                };
                for (var slot = 0; slot < expectedShieldPositions.Length; slot++)
                {
                    var shield = FindDescendant(arena,
                        "Shield " + (slot + 1));
                    Assert.That(shield, Is.Not.Null);
                    Assert.That(shield.localPosition,
                        Is.EqualTo(expectedShieldPositions[slot]));
                }
                Assert.That(FindDescendant(state.transform,
                    "Art Replacement Anchors"), Is.Not.Null);

                var hud = state.GetComponentInChildren<BouncingBallsHudBindings>(true);
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.HasRequiredReferences, Is.True);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        hud.gameObject), Is.EqualTo(HudPrefabPath));
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<Canvas>(true)).Count(),
                    Is.EqualTo(1));

                var cameras = state.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(cameras[0].name, Is.EqualTo("CM_BouncingBallsShared"));
                Assert.That(cameras[0].transform.position,
                    Is.EqualTo(new Vector3(1260f, 0f, -20f)));
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<Camera>(true)), Is.Empty);
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<AudioListener>(true)), Is.Empty);
                Assert.That(state.GetComponentInChildren<Light>(true), Is.Not.Null);
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
        public void BuildSettings_EnableBouncingBallsAfterSequenceMemory()
        {
            var enabled = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            var bouncingIndex = System.Array.IndexOf(enabled, ScenePath);
            var sequenceIndex = System.Array.IndexOf(enabled,
                "Assets/MazeParty/Scenes/Minigames/SequenceMemory.unity");
            Assert.That(bouncingIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(bouncingIndex, Is.EqualTo(sequenceIndex + 1));
        }

        private static void AssertArray(
            SerializedObject serialized, string name, int length)
        {
            var property = serialized.FindProperty(name);
            Assert.That(property, Is.Not.Null, name);
            Assert.That(property.isArray, Is.True, name);
            Assert.That(property.arraySize, Is.EqualTo(length), name);
            for (var index = 0; index < length; index++)
            {
                Assert.That(property.GetArrayElementAtIndex(index)
                    .objectReferenceValue, Is.Not.Null, name + "[" + index + "]");
            }
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }
            return null;
        }
    }
}
