using System;
using System.IO;
using System.Linq;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    /// <summary>Bakes the actual equipped hat models; no runtime cameras or generated UI.</summary>
    public static class WardrobePreviewBaker
    {
        public const string PreviewFolder = "Assets/MazeParty/Art/Player/HatPreviews";
        const int Size = 256;

        [MenuItem("MazeParty/Player/Bake Hat Preview Sprites")]
        public static void Bake()
        {
            var catalog = PlayerExpressionCatalog.Instance;
            if (catalog == null) throw new InvalidOperationException("Player expression catalog is missing.");
            Directory.CreateDirectory(PreviewFolder);
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewPreviewScene();
            var previousTarget = RenderTexture.active;
            var target = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            try
            {
                var camera = new GameObject("Hat Preview Camera").AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene;
                camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.targetTexture = target;
                camera.allowHDR = false;
                camera.allowMSAA = false;
                var light = new GameObject("Hat Preview Light").AddComponent<Light>();
                SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                light.type = LightType.Directional;
                light.intensity = 1.6f;
                light.transform.rotation = Quaternion.Euler(35f, 155f, 0f);
                var fill = new GameObject("Hat Preview Fill").AddComponent<Light>();
                SceneManager.MoveGameObjectToScene(fill.gameObject, scene);
                fill.type = LightType.Directional;
                fill.intensity = .65f;
                fill.transform.rotation = Quaternion.Euler(10f, -30f, 0f);
                var angle = 15f * Mathf.Deg2Rad;
                var direction = new Vector3(0f, Mathf.Sin(angle), Mathf.Cos(angle));
                for (var index = 0; index < catalog.Hats.Length; index++)
                {
                    var hat = catalog.Hats[index];
                    if (hat == null || hat.Prefab == null) throw new InvalidOperationException("Missing hat " + index);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(hat.Prefab, scene);
                    try
                    {
                        model.transform.position = hat.LocalPosition;
                        model.transform.rotation = Quaternion.Euler(hat.LocalEulerAngles);
                        model.transform.localScale = hat.LocalScale;
                        // Match the equipped appearance, including the Party Pack's size blend shape.
                        foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                            for (var shape = 0; shape < skin.sharedMesh.blendShapeCount; shape++)
                                if (skin.sharedMesh.GetBlendShapeName(shape).Contains("Scale")) skin.SetBlendShapeWeight(shape, 0f);
                        var renderers = model.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToArray();
                        if (renderers.Length == 0) throw new InvalidOperationException("Hat has no renderer: " + hat.Name);
                        var bounds = renderers[0].bounds;
                        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                        camera.transform.position = bounds.center + direction * (bounds.size.magnitude * 2f + 3f);
                        camera.transform.LookAt(bounds.center);
                        var halfWidth = 0f;
                        var halfHeight = 0f;
                        for (var corner = 0; corner < 8; corner++)
                        {
                            var offset = Vector3.Scale(bounds.extents, new Vector3((corner & 1) == 0 ? -1 : 1, (corner & 2) == 0 ? -1 : 1, (corner & 4) == 0 ? -1 : 1));
                            halfWidth = Mathf.Max(halfWidth, Mathf.Abs(Vector3.Dot(offset, camera.transform.right)));
                            halfHeight = Mathf.Max(halfHeight, Mathf.Abs(Vector3.Dot(offset, camera.transform.up)));
                        }
                        camera.orthographicSize = Mathf.Max(.01f, halfWidth, halfHeight) * 1.12f;
                        camera.Render();
                        RenderTexture.active = target;
                        pixels.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
                        pixels.Apply();
                        var path = PreviewFolder + "/Hat" + (index + 1).ToString("00") + ".png";
                        File.WriteAllBytes(path, pixels.EncodeToPNG());
                        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                        importer.textureType = TextureImporterType.Sprite;
                        importer.spriteImportMode = SpriteImportMode.Single;
                        importer.spritePixelsPerUnit = Size;
                        importer.alphaIsTransparency = true;
                        importer.mipmapEnabled = false;
                        importer.textureCompression = TextureImporterCompression.Uncompressed;
                        importer.SaveAndReimport();
                        hat.Preview = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                    }
                    finally { UnityEngine.Object.DestroyImmediate(model); }
                }
                catalog.NoHatPreview = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Close.png");
                EditorUtility.SetDirty(catalog);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                RenderTexture.active = previousTarget;
                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(pixels);
            }
        }
    }
}
