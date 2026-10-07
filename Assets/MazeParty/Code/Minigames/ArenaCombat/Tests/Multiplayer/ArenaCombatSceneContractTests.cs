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
            "Assets/MazeParty/Scenes/Minigames/ArenaCombat/ArenaCombat.unity";

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
                    "spectatorCamera").objectReferenceValue as Component;
                Assert.That(firstPersonCamera, Is.Not.Null);
                Assert.That(spectatorCamera, Is.Not.Null);
                Assert.That(firstPersonCamera,
                    Is.Not.SameAs(spectatorCamera));
                Assert.That(Vector3.Distance(
                        spectatorCamera.transform.position,
                        ArenaCombatNetworkView
                            .SharedSpectatorCameraPosition),
                    Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(
                        spectatorCamera.transform.rotation,
                        ArenaCombatNetworkView
                            .SharedSpectatorCameraRotation),
                    Is.LessThan(0.01f));
                Assert.That(GetLensValue(
                        spectatorCamera,
                        "FieldOfView"),
                    Is.EqualTo(ArenaCombatNetworkView
                        .SpectatorFieldOfView).Within(0.001f));
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

        private static float GetLensValue(
            Component camera,
            string fieldName)
        {
            var lensField = camera.GetType().GetField("Lens");
            Assert.That(lensField, Is.Not.Null);
            var lens = lensField.GetValue(camera);
            Assert.That(lens, Is.Not.Null);
            var field = lens.GetType().GetField(fieldName);
            Assert.That(field, Is.Not.Null);
            return (float)field.GetValue(lens);
        }

    }
}
