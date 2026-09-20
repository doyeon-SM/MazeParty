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
    public sealed class ArenaCombatSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat.unity";

        [Test]
        public void Scene_HasBoundedArenaFourSpawnsAndBothCameraModes()
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
                    root.GetComponentsInChildren<NetworkArenaCombatState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);
                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash,
                    Is.Not.EqualTo(0u));

                var view = state.GetComponent<ArenaCombatNetworkView>();
                Assert.That(view, Is.Not.Null);
                Assert.That(view.ArenaPresentation, Is.Not.Null);
                var serialized = new SerializedObject(view);
                var firstPersonCamera = serialized.FindProperty(
                    "firstPersonCamera").objectReferenceValue;
                var spectatorCamera = serialized.FindProperty(
                    "spectatorCamera").objectReferenceValue;
                Assert.That(firstPersonCamera, Is.Not.Null);
                Assert.That(spectatorCamera, Is.Not.Null);
                Assert.That(firstPersonCamera,
                    Is.Not.SameAs(spectatorCamera));
                for (var slot = 0; slot < 4; slot++)
                {
                    var spawn = view.GetSpawnMarker(slot);
                    Assert.That(spawn, Is.Not.Null, "slot " + slot);
                    Assert.That(spawn.position.y, Is.EqualTo(1f)
                        .Within(0.01f));
                }

                var arena = view.ArenaPresentation;
                foreach (var wallName in new[]
                         {
                             "North Boundary", "South Boundary",
                             "West Boundary", "East Boundary"
                         })
                {
                    var wall = arena.GetComponentsInChildren<Transform>(true)
                        .Single(transform => transform.name == wallName);
                    Assert.That(wall.GetComponent<Collider>(), Is.Not.Null,
                        wallName);
                }
                Assert.That(arena.GetComponentsInChildren<Transform>(true)
                    .Single(transform => transform.name == "Arena Floor")
                    .GetComponent<Collider>(), Is.Not.Null);
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<Component>(true))
                    .Where(component => component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray(), Has.Length.EqualTo(2));
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<Camera>(true)), Is.Empty);
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);
                Assert.That(roots.SelectMany(root =>
                    root.GetComponentsInChildren<PlayerAvatarVisual>(true)),
                    Is.Empty,
                    "The arena must reuse authoritative network avatars.");
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
        public void BuildSettings_EnableArenaCombat()
        {
            var enabled = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            Assert.That(enabled, Does.Contain(ScenePath));
        }
    }
}
