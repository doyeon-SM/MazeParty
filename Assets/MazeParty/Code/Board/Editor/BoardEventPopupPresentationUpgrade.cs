using System;
using System.IO;
using System.Linq;
using MazeParty.Multiplayer;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>Explicit migration of event text and map colors; regular setup preserves authored design.</summary>
    public static class BoardEventPopupPresentationUpgrade
    {
        public const string SpriteAssetPath = "Assets/MazeParty/Fonts/BoardEventResources.asset";
        private const string TexturePath = "Assets/MazeParty/Fonts/BoardEventResources.png";
        private const string MapPath = "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab";

        [MenuItem("MazeParty/Board/Upgrade Event Icons And Map Guide Colors")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("This migration requires Edit Mode.");
            EnsureSpriteAsset();
            EnsureFontFallbacks();
            UpgradeTravelMarker();
            var path = BoardEventPopupProjectSetup.ModulePrefabPath;
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var old = root.GetComponentsInChildren<Text>(true)
                    .FirstOrDefault(text => text.name == BoardEventPopupProjectSetup.MessageObjectName);
                if (old != null)
                {
                    var target = old.gameObject;
                    var size = old.fontSize;
                    var color = old.color;
                    var bold = old.fontStyle == FontStyle.Bold || old.fontStyle == FontStyle.BoldAndItalic;
                    UnityEngine.Object.DestroyImmediate(old);
                    var message = target.AddComponent<TextMeshProUGUI>();
                    message.fontSize = size;
                    message.fontStyle = bold ? FontStyles.Bold : FontStyles.Normal;
                    message.color = color;
                    message.alignment = TextAlignmentOptions.Center;
                    message.textWrappingMode = TextWrappingModes.Normal;
                    message.overflowMode = TextOverflowModes.Overflow;
                    message.enableAutoSizing = true;
                    message.fontSizeMin = 18;
                    message.fontSizeMax = size;
                    message.richText = true;
                    message.raycastTarget = false;
                    message.text = string.Empty;
                    ConfigureNewMessage(root, message);
                }
                if (root.GetComponent<BoardEventPopupFonts>() == null)
                    throw new InvalidOperationException("Event popup font bindings are missing.");
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            // Reconnect the containing HUD's serialized reference after replacing Text with TMP.
            BoardEventPopupProjectSetup.AddSharedBoardEventPopup();

            root = PrefabUtility.LoadPrefabContents(MapPath);
            try
            {
                foreach (var map in root.GetComponentsInChildren<BoardMinimapView>(true))
                {
                    var data = new SerializedObject(map);
                    var branches = data.FindProperty("travelRouteGraphic").objectReferenceValue as Graphic;
                    var primary = data.FindProperty("preferredTravelRouteGraphic").objectReferenceValue as Graphic;
                    if (branches == null || primary == null)
                        throw new InvalidOperationException("Map travel bindings are missing.");
                    branches.color = new Color(.48f, .82f, .18f, .8f);
                    primary.color = new Color(.65f, 1f, .2f, 1f);
                }
                var legend = root.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(item => item.name == "Icon Info 13");
                if (legend != null)
                {
                    var dot = legend.GetComponentInChildren<BoardMapCircleGraphic>(true);
                    if (dot != null) dot.color = new Color(.65f, 1f, .2f, 1f);
                }
                PrefabUtility.SaveAsPrefabAsset(root, MapPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        public static void EnsureFontFallbacks()
        {
            var fonts = new[]
            {
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/MazeParty/Fonts/BoardReadyKorean SDF.asset"),
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/MazeParty/Fonts/BoardReadyJapanese SDF.asset"),
                AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/MazeParty/Fonts/BoardReadyChineseSimplified SDF.asset"),
                BoardUiCopyCleanup.EnsureTraditionalChineseFont()
            };
            if (fonts.Any(font => font == null))
                throw new InvalidOperationException("Event nickname fonts are missing.");
            foreach (var font in fonts)
            {
                if (font.fallbackFontAssetTable == null)
                    font.fallbackFontAssetTable = new System.Collections.Generic.List<TMP_FontAsset>();
                var changed = false;
                foreach (var fallback in fonts)
                    if (fallback != font && !font.fallbackFontAssetTable.Contains(fallback))
                    {
                        font.fallbackFontAssetTable.Add(fallback);
                        changed = true;
                    }
                if (!changed) continue;
                // Dynamic glyph caches are regenerated on demand, not stored by this migration.
                font.ClearFontAssetData(true);
                EditorUtility.SetDirty(font);
                AssetDatabase.SaveAssetIfDirty(font);
            }
        }

        private static void UpgradeTravelMarker()
        {
            const string path = "Assets/MazeParty/Prefabs/Board/World/TravelRouteHemisphere.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var lifetime = particle.colorOverLifetime;
                    var gradient = lifetime.color.gradient;
                    // This prefab was copied from the yellow guide. Its warm lifetime RGB
                    // multiplied away the blue channel even though the start color was blue.
                    var neutral = new Gradient();
                    neutral.SetKeys(new[] { new GradientColorKey(Color.white, 0f),
                        new GradientColorKey(Color.white, 1f) }, gradient.alphaKeys);
                    lifetime.color = new ParticleSystem.MinMaxGradient(neutral);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void ConfigureNewMessage(GameObject root, TMP_Text message)
        {
            EnsureFontFallbacks();
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/MazeParty/Fonts/BoardReadyKorean SDF.asset");
            message.font = font;
            message.spriteAsset = EnsureSpriteAsset();
            var binding = root.GetComponent<BoardEventPopupFonts>();
            if (binding == null) binding = root.AddComponent<BoardEventPopupFonts>();
            var data = new SerializedObject(binding);
            data.FindProperty("message").objectReferenceValue = message;
            data.FindProperty("englishKoreanFont").objectReferenceValue = font;
            data.FindProperty("japaneseFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/MazeParty/Fonts/BoardReadyJapanese SDF.asset");
            data.FindProperty("chineseFont").objectReferenceValue = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(
                "Assets/MazeParty/Fonts/BoardReadyChineseSimplified SDF.asset");
            data.FindProperty("chineseTraditionalFont").objectReferenceValue =
                BoardUiCopyCleanup.EnsureTraditionalChineseFont();
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        private static TMP_SpriteAsset EnsureSpriteAsset()
        {
            var existing = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(SpriteAssetPath);
            if (existing != null) return existing;
            RenderMapIcons();
            var importer = (TextureImporter)AssetImporter.GetAtPath(TexturePath);
            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var atlas = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            var asset = ScriptableObject.CreateInstance<TMP_SpriteAsset>();
            asset.name = "BoardEventResources";
            var serializedAsset = new SerializedObject(asset);
            serializedAsset.FindProperty("m_Version").stringValue = "1.1.0";
            serializedAsset.ApplyModifiedPropertiesWithoutUndo();
            asset.spriteSheet = atlas;
            for (var index = 0; index < 2; index++)
            {
                var glyph = new TMP_SpriteGlyph
                {
                    index = (uint)index,
                    glyphRect = new GlyphRect(index * 128, 0, 128, 128),
                    metrics = new GlyphMetrics(128, 128, 0, 108, 136),
                    scale = 1f
                };
                asset.spriteGlyphTable.Add(glyph);
                asset.spriteCharacterTable.Add(new TMP_SpriteCharacter(0xFFFE, glyph)
                {
                    name = index == 0 ? "gold" : "key", scale = 1f
                });
            }
            asset.material = new Material(Shader.Find("TextMeshPro/Sprite"))
            {
                name = "BoardEventResources", mainTexture = atlas
            };
            AssetDatabase.CreateAsset(asset, SpriteAssetPath);
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            asset.UpdateLookupTables();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            return asset;
        }

        private static void RenderMapIcons()
        {
            // Render the same authored map icon geometry and gold sprite into a TMP atlas.
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var cameraObject = new GameObject("Event icon atlas camera", typeof(Camera));
            var canvasObject = new GameObject("Event icon atlas", typeof(RectTransform), typeof(Canvas));
            var texture = new RenderTexture(256, 128, 24, RenderTextureFormat.ARGB32);
            Texture2D pixels = null;
            var previous = RenderTexture.active;
            try
            {
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(cameraObject, scene);
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvasObject, scene);
                var camera = cameraObject.GetComponent<Camera>();
                camera.scene = scene;
                camera.transform.position = new Vector3(0f, 0f, -10f);
                camera.orthographic = true;
                camera.orthographicSize = 64f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.clear;
                camera.targetTexture = texture;
                camera.enabled = false;
                var canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.worldCamera = camera;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(256, 128);
                var gold = AssetDatabase.LoadAssetAtPath<Sprite>(
                    "Assets/Ignore/Modern UI Pack/Textures/Icon/Business & Commerce/Money Filled.png");
                if (gold == null) throw new InvalidOperationException("Map gold sprite is missing.");
                for (var index = 0; index < 2; index++)
                {
                    var iconObject = new GameObject("Map icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(BoardMapIcon));
                    iconObject.transform.SetParent(canvas.transform, false);
                    var rect = (RectTransform)iconObject.transform;
                    rect.sizeDelta = new Vector2(112, 112);
                    rect.anchoredPosition = new Vector2(index == 0 ? -64 : 64, 0);
                    var icon = iconObject.GetComponent<BoardMapIcon>();
                    icon.color = Color.white;
                    icon.SetIcon(index == 0 ? BoardMapIconKind.GoldGain : BoardMapIconKind.Key,
                        index == 0 ? gold : null);
                }
                Canvas.ForceUpdateCanvases();
                camera.Render();
                RenderTexture.active = texture;
                pixels = new Texture2D(256, 128, TextureFormat.RGBA32, false);
                pixels.ReadPixels(new Rect(0, 0, 256, 128), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(TexturePath, pixels.EncodeToPNG());
                AssetDatabase.ImportAsset(TexturePath);
            }
            finally
            {
                RenderTexture.active = previous;
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                texture.Release();
                UnityEngine.Object.DestroyImmediate(texture);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }
    }
}
