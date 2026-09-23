using System.IO;
using System.Collections.Generic;
using Arikan;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class BoardMinimapPreview
    {
        [MenuItem("MazeParty/Board/Capture Minimap Preview")]
        public static void Capture()
        {
            const string scenePath = "Assets/MazeParty/Scenes/Board/Board.unity";
            var scene = SceneManager.GetSceneByPath(scenePath);
            var opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            var preview = EditorSceneManager.NewPreviewScene();
            RenderTexture texture = null;
            Camera camera = null;
            Texture2D pixels = null;
            var previous = RenderTexture.active;
            var effects = new Dictionary<BoardTile, BoardLandingEffectType>();
            try
            {
                BoardTopology topology = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    topology = root.GetComponentInChildren<BoardTopology>(true);
                    if (topology != null) break;
                }
                topology.RebuildIndex();
                for (var i = 0; i < topology.Tiles.Count; i++)
                {
                    var tile = topology.Tiles[i];
                    effects.Add(tile, tile.LandingEffect);
                    tile.ApplyLandingEffectPresentation((BoardLandingEffectType)(1 + i % 4));
                }
                var ui = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>("Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab"), preview);
                // This is an editor-only visual fixture, not an online session.
                foreach (Transform child in ui.transform) child.gameObject.SetActive(false);
                var panel = ui.transform.Find("Board Action Minimap");
                panel.gameObject.SetActive(true);
                var canvas = ui.GetComponent<Canvas>();
                var cameraObject = new GameObject("Preview Camera");
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                camera = cameraObject.AddComponent<Camera>();
                camera.scene = preview;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(0.055f, 0.075f, 0.1f);
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1f;
                texture = new RenderTexture(1440, 900, 24);
                camera.targetTexture = texture;
                Canvas.ForceUpdateCanvases();
                var view = ui.GetComponent<BoardMinimapView>();
                var local = topology.Tiles[0];
                var focus = local.WorldCenter + new Vector3(1.5f, 0f, -0.7f);
                view.PrepareMap(topology, local.Coordinate, 3, topology.Tiles[4].Coordinate, focus);
                var markerTargets = new Transform[4];
                for (var slot = 0; slot < 4; slot++)
                {
                    var marker = new GameObject("Preview player " + slot);
                    SceneManager.MoveGameObjectToScene(marker, preview);
                    marker.transform.position = slot == 0 ? focus : topology.Tiles[slot * 4].WorldCenter;
                    markerTargets[slot] = marker.transform;
                    view.PresentPlayer(slot, marker.transform, LobbyColorPalette.GetColor(slot), slot == 0);
                }
                Directory.CreateDirectory("Temp");
                foreach (var yaw in new[] { 0f, 270f })
                {
                    view.SetHeading(yaw);
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    RenderTexture.active = texture;
                    pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                    pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                    pixels.Apply();
                    File.WriteAllBytes("Temp/BoardMinimapPreview" + yaw + ".png", pixels.EncodeToPNG());
                    Object.DestroyImmediate(pixels);
                    pixels = null;
                }
                ui.GetComponent<BoardMapView>().UpdateFullMapState(true, true);
                panel.gameObject.SetActive(false);
                var fullView = ui.transform.Find("Board Full Map").GetComponent<BoardMinimapView>();
                fullView.PrepareMap(topology, local.Coordinate, 3, topology.Tiles[4].Coordinate, focus);
                for (var slot = 0; slot < markerTargets.Length; slot++)
                    fullView.PresentPlayer(slot, markerTargets[slot], LobbyColorPalette.GetColor(slot), slot == 0);
                fullView.SetHeading(270f);
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes("Temp/BoardFullMapPreview.png", pixels.EncodeToPNG());
                Debug.Log("Minimap preview saved to Temp/BoardMinimapPreview0.png and 270.png");
            }
            finally
            {
                RenderTexture.active = previous;
                foreach (var pair in effects)
                    if (pair.Key != null) pair.Key.ApplyLandingEffectPresentation(pair.Value);
                if (camera != null) camera.targetTexture = null;
                if (pixels != null) Object.DestroyImmediate(pixels);
                if (texture != null) Object.DestroyImmediate(texture);
                EditorSceneManager.ClosePreviewScene(preview);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
