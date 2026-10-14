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
    public sealed class SnowySpinSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/SnowySpin/SnowySpin.unity";
        private const string SnowExplosionPath =
            "Assets/MazeParty/Prefabs/Minigames/SnowySpin/VFX/" +
            "SnowExplosion.prefab";


        [Test]
        public void Scene_BindsNetworkStateFourBallsSharedCameraAndNoCanvas()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var state = roots.SelectMany(root =>
                    root.GetComponentsInChildren<NetworkSnowySpinState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);
                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<SnowySpinNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serialized = new SerializedObject(view);
                foreach (var field in new[]
                         {
                             "state", "sharedCamera", "arenaPresentation"
                         })
                {
                    var property = serialized.FindProperty(field);
                    Assert.That(property, Is.Not.Null, field);
                    Assert.That(property.objectReferenceValue,
                        Is.Not.Null, field);
                }
                var collisionVfx = serialized.FindProperty(
                    "collisionExplosionVfxPrefab");
                Assert.That(collisionVfx, Is.Not.Null);
                var collisionPrefab = collisionVfx.objectReferenceValue as
                    GameObject;
                Assert.That(collisionPrefab, Is.Not.Null);
                Assert.That(AssetDatabase.GetAssetPath(collisionPrefab),
                    Is.EqualTo(SnowExplosionPath));
                var pooled = collisionPrefab.GetComponent<
                    PooledOneShotVfx>();
                Assert.That(pooled, Is.Not.Null);
                Assert.That(pooled.ParticleSystems, Is.Not.Empty);
                Assert.That(collisionPrefab.GetComponentsInChildren<
                    Collider>(true), Is.Empty);
                Assert.That(collisionPrefab.GetComponentsInChildren<
                    Collider2D>(true), Is.Empty);
                Assert.That(collisionPrefab.GetComponentsInChildren<
                    NetworkObject>(true), Is.Empty);
                Assert.That(collisionPrefab.GetComponentsInChildren<
                        Transform>(true)
                    .Any(item => item.name.Contains("Distort") ||
                                 item.name.Contains("GrabPass")),
                    Is.False);

                var arena = serialized.FindProperty("arenaPresentation")
                    .objectReferenceValue as GameObject;
                Assert.That(arena, Is.Not.Null);
                foreach (var field in new[] { "playerBalls", "ballRenderers" })
                {
                    var property = serialized.FindProperty(field);
                    Assert.That(property, Is.Not.Null, field);
                    Assert.That(property.arraySize, Is.EqualTo(4), field);
                    for (var index = 0; index < 4; index++)
                    {
                        Assert.That(property.GetArrayElementAtIndex(index)
                            .objectReferenceValue, Is.Not.Null,
                            field + "[" + index + "]");
                    }
                }
                var playerBalls = serialized.FindProperty("playerBalls");
                for (var slot = 0; slot < 4; slot++)
                {
                    var ball = playerBalls.GetArrayElementAtIndex(slot)
                        .objectReferenceValue as Transform;
                    Assert.That(ball.IsChildOf(arena.transform), Is.True);
                    Assert.That(ball.GetComponent<Collider>(), Is.Null);
                }

                var cameras = roots.SelectMany(root =>
                    root.GetComponentsInChildren<Component>(true))
                    .Where(component => component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<Camera>(true)), Is.Empty);
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<AudioListener>(true)), Is.Empty);
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<Canvas>(true)), Is.Empty);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

    }
}
