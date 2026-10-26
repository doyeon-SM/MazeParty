using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameNightLightingTests
    {
        [Test]
        public void NightEnvironment_HandoverAndDisableRestoreSharedLightingAndCamera()
        {
            var previousSky = RenderSettings.skybox;
            var previousSun = RenderSettings.sun;
            var previousMode = RenderSettings.ambientMode;
            var previousAmbient = RenderSettings.ambientIntensity;
            var previousReflection = RenderSettings.reflectionIntensity;
            var root = new GameObject("Night lighting transition contract");
            try
            {
                var external = Child(root, "Board moon").AddComponent<Light>();
                external.type = LightType.Directional;
                var disabled = Child(root, "Disabled sun").AddComponent<Light>();
                disabled.type = LightType.Directional; disabled.enabled = false;
                var point = Child(root, "Authored lamp").AddComponent<Light>();
                point.type = LightType.Point;
                var camera = Child(root, "Output").AddComponent<Camera>();
                camera.enabled = false; camera.clearFlags = CameraClearFlags.SolidColor;
                RenderSettings.sun = external;
                RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientIntensity = 0.43f;
                RenderSettings.reflectionIntensity = 0.67f;
                var first = CreateLighting(root, "First");
                var second = CreateLighting(root, "Second");
                second.gameObject.SetActive(false);

                first.RefreshPresentation(true, camera);
                Assert.That(RenderSettings.sun, Is.SameAs(first.Moonlight));
                Assert.That(RenderSettings.skybox, Is.SameAs(first.Skybox));
                Assert.That(external.enabled, Is.False);
                Assert.That(disabled.enabled, Is.False);
                Assert.That(point.enabled, Is.True);
                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.Skybox));

                second.gameObject.SetActive(true);
                second.RefreshPresentation(true, camera);
                first.RefreshPresentation(false, camera);
                Assert.That(RenderSettings.sun, Is.SameAs(second.Moonlight),
                    "The old presentation must not restore over the new owner.");
                Assert.That(external.enabled, Is.False);
                InvokeDisable(second); // EditMode does not dispatch runtime lifecycle callbacks.
                second.gameObject.SetActive(false);
                Assert.That(RenderSettings.sun, Is.SameAs(external));
                Assert.That(RenderSettings.skybox, Is.SameAs(previousSky));
                Assert.That(RenderSettings.ambientMode, Is.EqualTo(AmbientMode.Trilight));
                Assert.That(RenderSettings.ambientIntensity, Is.EqualTo(0.43f));
                Assert.That(RenderSettings.reflectionIntensity, Is.EqualTo(0.67f));
                Assert.That(external.enabled, Is.True);
                Assert.That(disabled.enabled, Is.False);
                Assert.That(point.enabled, Is.True);
                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));

                first.RefreshPresentation(true, camera);
                InvokeDisable(first);
                Object.DestroyImmediate(first.gameObject);
                Assert.That(RenderSettings.sun, Is.SameAs(external),
                    "Unloading an active minigame must release the environment.");
                Assert.That(camera.clearFlags, Is.EqualTo(CameraClearFlags.SolidColor));
            }
            finally
            {
                foreach (var lighting in root.GetComponentsInChildren<MinigameNightLighting>(true))
                    lighting.RefreshPresentation(false, null);
                Object.DestroyImmediate(root);
                RenderSettings.skybox = previousSky; RenderSettings.sun = previousSun;
                RenderSettings.ambientMode = previousMode;
                RenderSettings.ambientIntensity = previousAmbient;
                RenderSettings.reflectionIntensity = previousReflection;
            }
        }

        private static void InvokeDisable(MinigameNightLighting lighting) =>
            typeof(MinigameNightLighting).GetMethod("OnDisable",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .Invoke(lighting, null);
        private static GameObject Child(GameObject parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent.transform);
            return child;
        }

        private static MinigameNightLighting CreateLighting(GameObject root, string name)
        {
            var holder = Child(root, name);
            var moon = holder.AddComponent<Light>();
            moon.type = LightType.Directional;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Minigames/Common/Environment/MinigameNightLighting.prefab"),
                holder.transform);
            var lighting = instance.GetComponent<MinigameNightLighting>();
            var data = new SerializedObject(lighting);
            data.FindProperty("moonlight").objectReferenceValue = moon;
            data.ApplyModifiedPropertiesWithoutUndo();
            Assert.That(lighting.HasRequiredReferences, Is.True);
            return lighting;
        }
    }
}
