using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static partial class BoardItemProjectSetup
    {
        public const string TileAmplifierDescription = "Magic Maze only. Amplifies everyone's gold, health and item tiles this turn; events excluded. Each different user adds +1 to the multiplier (2x, 3x, 4x...). Once per player per turn. Gain up to 3 random items in empty slots. Reshuffles tiles once after this turn, except the final turn.";
        public const string TileAmplifierIconPath = "Assets/MazeParty/Art/Icons/TileAmplifier.png";

        [MenuItem("MazeParty/Board/Install Tile Amplifier")]
        public static void InstallTileAmplifier()
        {
            EnsureTileAmplifierAsset();
            AssetDatabase.SaveAssets();
        }

        private static void EnsureTileAmplifierAsset()
        {
            EnsureTileAmplifierSound();
            const string path = DataFolder + "/TileAmplifier.asset";
            if (AssetDatabase.LoadAssetAtPath<BoardItemDefinition>(path) != null) return;
            var model = EnsureTileAmplifierModel();
            var item = ScriptableObject.CreateInstance<BoardItemDefinition>();
            item.Id = PrototypeItemId.TileAmplifier;
            item.UseTiming = BoardItemUseTiming.Immediate;
            item.RequiredMapId = MagicMazeRules.MapId;
            item.DisplayName = "Tile Amplifier";
            item.Description = TileAmplifierDescription;
            item.Price = 12; item.SpawnWeight = 1; item.Charges = 1;
            item.HeldPrefab = model; item.WorldPrefab = model;
            item.Icon = EnsureTileAmplifierIcon(model);
            AssetDatabase.CreateAsset(item, path);
        }

        private static void EnsureTileAmplifierSound()
        {
            const string path = "Assets/MazeParty/Audio/Cues/Item/item.use.tile_amplifier.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
            if (cue == null)
            {
                if (!AssetDatabase.CopyAsset("Assets/MazeParty/Audio/Cues/Item/item.use.position_swapper.asset", path))
                    throw new InvalidOperationException("The switch activation sound is missing.");
                cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
                var data = new SerializedObject(cue);
                data.FindProperty("key").stringValue = SoundKeys.ItemUse(PrototypeItemId.TileAmplifier);
                data.ApplyModifiedPropertiesWithoutUndo();
                cue.name = "item.use.tile_amplifier"; EditorUtility.SetDirty(cue);
            }
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>("Assets/MazeParty/Audio/SoundLibrary.asset");
            var cues = new List<SoundCue>(library.Cues);
            if (cues.Contains(cue)) return;
            cues.Add(cue); library.SetCues(cues.ToArray()); EditorUtility.SetDirty(library);
        }

        private static GameObject EnsureTileAmplifierModel()
        {
            const string path = VisualFolder + "/TileAmplifier.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(VisualFolder + "/PositionSwapper.prefab");
            if (source == null || !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
                throw new InvalidOperationException("The authored switch prefab is required.");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = "TileAmplifier";
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name != "switch_003" && renderer.name != "switch_004") continue;
                    var purple = renderer.name == "switch_003";
                    var materialPath = VisualFolder + (purple ? "/TileAmplifier Purple.mat" : "/TileAmplifier Blue.mat");
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        material = new Material(renderer.sharedMaterial);
                        material.name = purple ? "TileAmplifier Purple" : "TileAmplifier Blue";
                        material.SetTexture("_BaseMap", null); material.SetTexture("_MainTex", null);
                        var color = purple ? new Color(.63f, .10f, 1f) : new Color(.025f, .32f, 1f);
                        material.SetColor("_BaseColor", color); material.SetColor("_Color", color);
                        AssetDatabase.CreateAsset(material, materialPath);
                    }
                    renderer.sharedMaterial = material;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        // Bake an inventory sprite from the actual authored model. This runs only in the Editor.
        private static Sprite EnsureTileAmplifierIcon(GameObject prefab)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Sprite>(TileAmplifierIconPath);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(TileAmplifierIconPath));
            AssetDatabase.Refresh();
            var scene = EditorSceneManager.NewPreviewScene();
            var previous = RenderTexture.active;
            var target = new RenderTexture(512, 512, 24, RenderTextureFormat.ARGB32);
            var pixels = new Texture2D(512, 512, TextureFormat.RGBA32, false);
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                var renderers = model.GetComponentsInChildren<Renderer>();
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
                var camera = new GameObject("Tile Amplifier Icon Camera").AddComponent<Camera>();
                SceneManager.MoveGameObjectToScene(camera.gameObject, scene);
                camera.scene = scene; camera.orthographic = true;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.clear;
                camera.targetTexture = target; camera.allowHDR = false; camera.allowMSAA = false;
                camera.transform.position = bounds.center + new Vector3(1.2f, 2.4f, -1.5f).normalized * (bounds.size.magnitude * 2f + 3f);
                camera.transform.LookAt(bounds.center);
                camera.orthographicSize = bounds.extents.magnitude * 1.08f;
                foreach (var angle in new[] { new Vector3(40, -40, 0), new Vector3(25, 135, 0) })
                {
                    var light = new GameObject("Icon Light").AddComponent<Light>();
                    SceneManager.MoveGameObjectToScene(light.gameObject, scene);
                    light.type = LightType.Directional; light.intensity = 1.2f;
                    light.transform.rotation = Quaternion.Euler(angle);
                }
                camera.Render(); RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, 512, 512), 0, 0); pixels.Apply();
                File.WriteAllBytes(TileAmplifierIconPath, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                EditorSceneManager.ClosePreviewScene(scene);
                UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(pixels);
            }
            AssetDatabase.ImportAsset(TileAmplifierIconPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(TileAmplifierIconPath);
            importer.textureType = TextureImporterType.Sprite; importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 512; importer.alphaIsTransparency = true; importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed; importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(TileAmplifierIconPath);
        }
    }
}
