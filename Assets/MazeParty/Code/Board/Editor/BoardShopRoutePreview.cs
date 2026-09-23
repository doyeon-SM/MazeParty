using System.IO;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class BoardShopRoutePreview
    {
        [MenuItem("MazeParty/Board/Capture Key Shop Route Preview")]
        public static void Capture()
        {
            const string path = "Assets/MazeParty/Scenes/Board/Board.unity";
            var scene = SceneManager.GetSceneByPath(path);
            var opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            var preview = EditorSceneManager.NewPreviewScene();
            Camera camera = null;
            RenderTexture texture = null;
            Texture2D pixels = null;
            var previous = RenderTexture.active;
            try
            {
                BoardTopology topology = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    topology = root.GetComponentInChildren<BoardTopology>();
                    if (topology != null) break;
                }
                topology.RebuildIndex();
                foreach (var tile in topology.Tiles)
                {
                    var floor = new GameObject("Floor", typeof(MeshFilter), typeof(MeshRenderer));
                    SceneManager.MoveGameObjectToScene(floor, preview);
                    floor.transform.SetPositionAndRotation(tile.transform.position, tile.transform.rotation);
                    floor.transform.localScale = tile.transform.lossyScale;
                    floor.GetComponent<MeshFilter>().sharedMesh = tile.GetComponent<MeshFilter>().sharedMesh;
                    floor.GetComponent<MeshRenderer>().sharedMaterial = tile.GetComponent<MeshRenderer>().sharedMaterial;
                }
                var guide = new GameObject("Local guide preview");
                SceneManager.MoveGameObjectToScene(guide, preview);
                var view = guide.AddComponent<BoardShopRouteView>();
                view.Configure(AssetDatabase.LoadAssetAtPath<GameObject>(BoardShopRouteProjectSetup.DotPath));
                topology.TryGetTile(new Vector2Int(1, 1), out var source);
                topology.TryGetTile(new Vector2Int(5, 2), out var shop);
                view.PresentRouteForPhase(topology, source, shop, BoardFlowState.TurnOverview);
                var lightObject = new GameObject("Preview Light", typeof(Light));
                SceneManager.MoveGameObjectToScene(lightObject, preview);
                var light = lightObject.GetComponent<Light>();
                light.type = LightType.Directional; light.intensity = 1.8f;
                light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
                var cameraObject = new GameObject("Preview Camera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                camera = cameraObject.GetComponent<Camera>(); camera.scene = preview;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.035f, .05f, .075f);
                var center = (source.WorldCenter + shop.WorldCenter) * .5f;
                camera.transform.position = center + new Vector3(-4f, 32f, -28f);
                camera.transform.LookAt(center);
                camera.fieldOfView = 55f;
                texture = new RenderTexture(1400, 900, 24);
                camera.targetTexture = texture; camera.Render();
                RenderTexture.active = texture;
                pixels = new Texture2D(texture.width, texture.height, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); pixels.Apply();
                Directory.CreateDirectory("Temp");
                File.WriteAllBytes("Temp/BoardShopRoutePreview.png", pixels.EncodeToPNG());
                // Verify the actual authored overview framing, including its culling mask.
                GameplayCameraDirector director = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    director = root.GetComponentInChildren<GameplayCameraDirector>();
                    if (director != null) break;
                }
                if (director == null || director.BoardFramingAnchor == null)
                    throw new System.InvalidOperationException("Board overview camera bindings are missing.");
                var pose = director.BoardFramingAnchor.Evaluate((float)texture.width / texture.height);
                camera.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
                camera.fieldOfView = pose.FieldOfView;
                camera.cullingMask = director.OutputCamera.cullingMask;
                camera.Render();
                pixels.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0); pixels.Apply();
                File.WriteAllBytes("Temp/BoardShopRouteTopViewPreview.png", pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                if (camera != null) camera.targetTexture = null;
                if (texture != null) Object.DestroyImmediate(texture);
                if (pixels != null) Object.DestroyImmediate(pixels);
                EditorSceneManager.ClosePreviewScene(preview);
                if (opened) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
