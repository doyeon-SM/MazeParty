using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace MazeParty.Gameplay.Tests
{
    public sealed class BoardEventCameraOcclusionTests
    {
        [Test]
        public void EventTerrain_CameraClearsHillWithoutHidingGroundOrBackgroundVegetation()
        {
            var root = new GameObject("Terrain camera contract");
            var data = new TerrainData { heightmapResolution = 33, size = new Vector3(8, 4, 8) };
            var heights = new float[33, 33];
            for (var z = 0; z < 33; z++) for (var x = 0; x < 33; x++) heights[z, x] = .5f;
            data.SetHeights(0, 0, heights);
            var terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.transform.SetParent(root.transform);
            terrainObject.transform.position = new Vector3(10000, 10000, 10000);
            var terrain = terrainObject.GetComponent<Terrain>();
            using var occlusion = new BoardEventCameraOcclusion();
            try
            {
                var camera = new GameObject("Event camera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.SetParent(root.transform); camera.enabled = false;
                camera.transform.position = terrainObject.transform.position + new Vector3(4, 1, 3);
                var player = new GameObject("Player").transform;
                player.SetParent(root.transform);
                player.position = terrainObject.transform.position + new Vector3(4, 3, 7);
                var other = new GameObject("Other camera", typeof(Camera)).GetComponent<Camera>();
                other.transform.SetParent(root.transform); other.enabled = false;
                void Render(string phase, Camera view) => typeof(BoardEventCameraOcclusion)
                    .GetMethod(phase + "CameraRendering", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(occlusion, new object[] { default(ScriptableRenderContext), view });
                foreach (var foliage in new[] { true, false })
                foreach (var finish in new[] { "End", "Cancel", "Dispose", "NextCamera" })
                {
                    terrain.drawHeightmap = true; terrain.drawTreesAndFoliage = foliage;
                    occlusion.SetFocus(camera, player); Render("Begin", camera);
                    Assert.That(terrain.drawHeightmap, Is.True);
                    Assert.That(terrain.drawTreesAndFoliage, Is.EqualTo(foliage));
                    var cleared = BoardEventCameraOcclusion.ClearTerrainSightLine(camera.transform.position, player.position);
                    Assert.That(cleared.y, Is.GreaterThan(terrainObject.transform.position.y + 2f));
                    Assert.That(terrain.terrainData, Is.SameAs(data));
                    Assert.That(terrainObject.GetComponent<TerrainCollider>().enabled, Is.True);
                    if (finish == "End") Render("End", camera);
                    else if (finish == "Cancel") occlusion.SetFocus(camera, null);
                    else if (finish == "Dispose") occlusion.Dispose();
                    else Render("Begin", other);
                    Assert.That(terrain.drawHeightmap, Is.True);
                    Assert.That(terrain.drawTreesAndFoliage, Is.EqualTo(foliage));
                }
            }
            finally { occlusion.Dispose(); Object.DestroyImmediate(root); Object.DestroyImmediate(data); }
        }
        [Test]
        public void EventViews_RestoreSceneryAfterRenderCancellationAndDisposal()
        {
            var root = new GameObject("Event occlusion contract");
            var material = new Material(Shader.Find("MazeParty/Toon Shader FX"));
            using var occlusion = new BoardEventCameraOcclusion();
            try
            {
                GameObject Child(string name)
                {
                    var value = new GameObject(name);
                    value.transform.SetParent(root.transform);
                    return value;
                }
                var player = Child("Focus player").transform;
                player.position = new Vector3(10000, 10000, 10000);
                var camera = Child("Event view").AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = player.position + new Vector3(0, .75f, 3.4f);
                var otherCamera = Child("Other view").AddComponent<Camera>();
                otherCamera.enabled = false;
                otherCamera.transform.position = player.position + new Vector3(3.4f, .75f, 0);
                var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                blocker.transform.SetParent(root.transform);
                blocker.transform.position = player.position + new Vector3(0, .75f, 1.5f);
                var renderer = blocker.GetComponent<Renderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.TwoSided;
                var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
                body.transform.SetParent(player, false);
                body.transform.localPosition = Vector3.up * .75f;
                body.GetComponent<Renderer>().sharedMaterial = material;
                var otherPlayer = Child("Other player");
                otherPlayer.AddComponent<PlayerAvatarVisual>();
                var otherBody = GameObject.CreatePrimitive(PrimitiveType.Cube);
                otherBody.transform.SetParent(otherPlayer.transform, false);
                var otherBodyRenderer = otherBody.GetComponent<Renderer>();
                var otherFace = Child("Other face sprite");
                otherFace.transform.SetParent(otherPlayer.transform);
                var otherFaceRenderer = otherFace.AddComponent<SpriteRenderer>();
                var originalColor = new Color(.3f, .7f, .9f, .8f);
                var block = new MaterialPropertyBlock();
                block.SetColor("_BaseColor", originalColor);
                block.SetFloat("_ContractSentinel", 123f);
                renderer.SetPropertyBlock(block);
                renderer.SetPropertyBlock(block, 0);

                void Render(string phase, Camera view) => typeof(BoardEventCameraOcclusion)
                    .GetMethod(phase + "CameraRendering", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(occlusion, new object[] { default(ScriptableRenderContext), view });
                void AssertRestored()
                {
                    Assert.That(renderer.sharedMaterial, Is.SameAs(material));
                    Assert.That(renderer.shadowCastingMode, Is.EqualTo(ShadowCastingMode.TwoSided));
                    Assert.That(renderer.forceRenderingOff, Is.False);
                    renderer.GetPropertyBlock(block);
                    Assert.That(Vector4.Distance(block.GetColor("_BaseColor"), originalColor), Is.LessThan(0.00001f));
                    Assert.That(block.GetFloat("_ContractSentinel"), Is.EqualTo(123f));
                    renderer.GetPropertyBlock(block, 0);
                    Assert.That(Vector4.Distance(block.GetColor("_BaseColor"), originalColor), Is.LessThan(0.00001f));
                    Assert.That(blocker.GetComponent<Collider>().enabled, Is.True);
                }

                occlusion.SetFocus(camera, player);
                occlusion.SetFocus(otherCamera, player);
                Render("Begin", otherCamera);
                AssertRestored();
                Render("End", otherCamera);
                foreach (var isolatePlayers in new[] { false, true })
                foreach (var alreadyHidden in new[] { false, true })
                foreach (var finish in new[] { "End", "Cancel", "Dispose", "NextCamera" })
                {
                    otherBodyRenderer.forceRenderingOff = alreadyHidden;
                    otherFaceRenderer.forceRenderingOff = alreadyHidden;
                    occlusion.SetFocus(camera, player, isolatePlayers);
                    Render("Begin", camera);
                    Assert.That(otherBodyRenderer.forceRenderingOff, Is.EqualTo(alreadyHidden || isolatePlayers));
                    Assert.That(otherFaceRenderer.forceRenderingOff, Is.EqualTo(alreadyHidden || isolatePlayers));
                    Assert.That(body.GetComponent<Renderer>().forceRenderingOff, Is.False);
                    Assert.That(renderer.forceRenderingOff, Is.True);
                    Assert.That(renderer.sharedMaterial, Is.SameAs(material));
                    Assert.That(body.GetComponent<Renderer>().sharedMaterial, Is.SameAs(material));
                    Assert.That(blocker.GetComponent<Collider>().enabled, Is.True);
                    if (finish == "End") Render("End", camera);
                    else if (finish == "Cancel") occlusion.SetFocus(camera, null);
                    else if (finish == "Dispose") occlusion.Dispose();
                    else Render("Begin", otherCamera);
                    AssertRestored();
                    Assert.That(otherBodyRenderer.forceRenderingOff, Is.EqualTo(alreadyHidden), "Player render state must restore");
                    Assert.That(otherFaceRenderer.forceRenderingOff, Is.EqualTo(alreadyHidden), "Face sprite state must restore");
                }
            }
            finally
            {
                occlusion.Dispose();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(material);
            }
        }
    }
}
