using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BombPassingSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/BombPassing/BombPassing.unity";

        [Test]
        public void AttackPresentationRpc_IsReliablePerEvent()
        {
            var rpc = typeof(NetworkBombPassingState).GetMethod(
                "PlayAttackPresentationRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rpc, Is.Not.Null);
            Assert.That(rpc.GetCustomAttribute<RpcAttribute>()?.Delivery,
                Is.EqualTo(RpcDelivery.Reliable));
            var parameters = rpc.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(1));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(byte)));
        }

        [Test]
        public void Scene_HasCentralBombFourSpawnsSharedCameraAndNoCanvas()
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
                    root.GetComponentsInChildren<NetworkBombPassingState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);
                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<BombPassingNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serialized = new SerializedObject(view);
                foreach (var field in new[]
                         {
                             "state", "sharedCamera", "playerRoot",
                             "arenaPresentation", "bombTransform",
                             "bombRenderer", "bombLight",
                             "explosionFlashLight", "explosionVfxPrefab"
                         })
                {
                    var property = serialized.FindProperty(field);
                    Assert.That(property, Is.Not.Null, field);
                    Assert.That(property.objectReferenceValue,
                        Is.Not.Null, field);
                }

                var arena = FindDescendant(
                    state.transform, "Arena Presentation");
                Assert.That(arena, Is.Not.Null);
                Assert.That(arena.position,
                    Is.EqualTo(new Vector3(1380f, 0f, 0f)));
                Assert.That(FindDescendant(arena, "Boundary Walls")?.childCount,
                    Is.EqualTo(4));
                Assert.That(FindDescendant(arena,
                    "Player Spawn Markers")?.childCount, Is.EqualTo(4));
                Assert.That(FindDescendant(arena, "Player Visuals"),
                    Is.Not.Null);
                var spawnCoordinates = new[]
                {
                    new Vector2(-5f, -5f),
                    new Vector2(5f, -5f),
                    new Vector2(5f, 5f),
                    new Vector2(-5f, 5f)
                };
                for (var slot = 0; slot < spawnCoordinates.Length; slot++)
                {
                    var marker = FindDescendant(
                        arena, "Spawn " + (slot + 1));
                    Assert.That(marker, Is.Not.Null);
                    Assert.That(marker.localPosition.x,
                        Is.EqualTo(spawnCoordinates[slot].x));
                    Assert.That(marker.localPosition.z,
                        Is.EqualTo(spawnCoordinates[slot].y));
                }

                var bomb = FindDescendant(arena, "Bomb");
                Assert.That(bomb, Is.Not.Null);
                Assert.That(bomb.localPosition,
                    Is.EqualTo(new Vector3(0f, 0.76f, 0f)));
                Assert.That(bomb.GetComponent<Renderer>(), Is.Not.Null);
                var warningLight = FindDescendant(bomb,
                    "Bomb Warning Light")?.GetComponent<Light>();
                Assert.That(warningLight, Is.Not.Null);
                Assert.That(warningLight.type, Is.EqualTo(LightType.Point));
                Assert.That(warningLight.color.r,
                    Is.GreaterThan(warningLight.color.b));

                var explosionFlash = FindDescendant(
                    arena, "Bomb Explosion Flash")?.GetComponent<Light>();
                Assert.That(explosionFlash, Is.Not.Null);
                Assert.That(explosionFlash, Is.Not.SameAs(warningLight));
                Assert.That(explosionFlash.type, Is.EqualTo(LightType.Point));
                Assert.That(explosionFlash.enabled, Is.False);
                Assert.That(explosionFlash.range,
                    Is.GreaterThan(warningLight.range));
                Assert.That(explosionFlash.intensity,
                    Is.GreaterThan(warningLight.intensity));
                Assert.That(view.ExplosionFlashLight,
                    Is.SameAs(explosionFlash));
                Assert.That(view.ExplosionVfxPrefab,
                    Is.EqualTo(AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/MazeParty/Prefabs/Common/VFX/" +
                        "CartoonExplosion.prefab")));

                var cameras = roots.SelectMany(root =>
                    root.GetComponentsInChildren<Component>(true))
                    .Where(component => component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(cameras[0].name,
                    Is.EqualTo("CM_BombPassingShared"));
                Assert.That(Vector3.Distance(
                        cameras[0].transform.position,
                        BombPassingNetworkView.SharedCameraPosition),
                    Is.LessThan(0.001f));
                Assert.That(Quaternion.Angle(
                        cameras[0].transform.rotation,
                        BombPassingNetworkView.SharedCameraRotation),
                    Is.LessThan(0.01f));
                Assert.That(GetLensValue(
                        cameras[0],
                        "OrthographicSize"),
                    Is.EqualTo(BombPassingNetworkView
                        .SharedCameraOrthographicSize).Within(0.001f));
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
