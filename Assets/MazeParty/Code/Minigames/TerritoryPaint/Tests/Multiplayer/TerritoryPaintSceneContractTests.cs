using System.Linq;
using MazeParty.Gameplay.Minigames.TerritoryPaint;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class TerritoryPaintSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/TerritoryPaint/TerritoryPaint.unity";
        private const string HudPrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/TerritoryPaint/UI/TerritoryPaintHud.prefab";

        [Test]
        public void Scene_PreservesPaintArenaCameraAndPrefabHudContract()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest =
                !scene.IsValid() || !scene.isLoaded;
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
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            NetworkTerritoryPaintState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);
                var networkObject =
                    state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(
                    networkObject.PrefabIdHash,
                    Is.Not.EqualTo(0u));

                var view =
                    state.GetComponent<TerritoryPaintNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serialized = new SerializedObject(view);
                foreach (var propertyName in new[]
                         {
                             "state",
                             "sharedCamera",
                             "runnerRoot",
                             "arenaPresentation",
                             "paintSurfaceRenderer",
                             "hud"
                         })
                {
                    var property =
                        serialized.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }

                var hud = state.GetComponentInChildren<
                    TerritoryPaintHudBindings>(true);
                Assert.That(hud, Is.Not.Null);
                Assert.That(hud.HasRequiredReferences, Is.True);
                Assert.That(
                    PrefabUtility
                        .GetPrefabAssetPathOfNearestInstanceRoot(
                            hud.gameObject),
                    Is.EqualTo(HudPrefabPath));

                var paintSurface = FindDescendant(
                    state.transform,
                    "Paint Surface");
                var runnerRoot = FindDescendant(
                    state.transform,
                    "Runtime Runners");
                Assert.That(paintSurface, Is.Not.Null);
                Assert.That(
                    paintSurface.GetComponent<Renderer>(),
                    Is.Not.Null);
                Assert.That(runnerRoot, Is.Not.Null);
                Assert.That(
                    FindDescendant(
                        state.transform,
                        "North Boundary"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(
                        state.transform,
                        "South Boundary"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(
                        state.transform,
                        "West Boundary"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(
                        state.transform,
                        "East Boundary"),
                    Is.Not.Null);
                for (var slot = 0;
                     slot < TerritoryPaintRules.PlayerCount;
                     slot++)
                {
                    Assert.That(
                        FindDescendant(
                            state.transform,
                            "Start Marker " + (slot + 1)),
                        Is.Not.Null);
                }

                var cameras = state
                    .GetComponentsInChildren<Component>(true)
                    .Where(component =>
                        component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(
                    cameras[0].name,
                    Is.EqualTo("CM_TerritoryPaintShared"));
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive scene must reuse Board's output Camera.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<
                            AudioListener>(true)),
                    Is.Empty);
            }
            finally
            {
                if (openedForTest &&
                    scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0;
                 index < root.childCount;
                 index++)
            {
                var result = FindDescendant(
                    root.GetChild(index),
                    name);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }
    }
}
