#if UNITY_EDITOR
using System;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class BoardLandingEffectFeedbackProjectSetup
    {
        internal const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardLandingEffectFeedback.prefab";
        internal const string HealingShieldPath =
            "Assets/MazeParty/Prefabs/Common/VFX/HealingShield.prefab";
        internal const string GiftPath =
            "Assets/MazeParty/Prefabs/Board/World/BoardLandingGift.prefab";

        [MenuItem("MazeParty/Board/Ensure Landing Effect Feedback Prefab")]
        public static void EnsureFromMenu()
        {
            EnsureInstalled();
            EnsureHealingShieldPrefab();
            EnsureGiftPrefab();
            AssetDatabase.SaveAssets();
            Debug.Log("Board landing-effect feedback prefab is ready.");
        }

        public static BoardLandingEffectFeedbackView EnsureInstalled()
        {
            var existingRoot = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            var existing = existingRoot != null
                ? existingRoot.GetComponent<BoardLandingEffectFeedbackView>()
                : null;
            if (existing != null)
            {
                Validate(existing);
                return existing;
            }

            EnsureFolder("Assets/MazeParty/Prefabs/Board/UI");
            var font = AssetDatabase.LoadAssetAtPath<Font>(
                GameFonts.EnglishKoreanAssetPath);
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Required localized board font is missing: '" +
                    GameFonts.EnglishKoreanAssetPath + "'.");
            }

            var root = new GameObject(
                "BoardLandingEffectFeedback",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler),
                typeof(LocalizedFontScope),
                typeof(BoardLandingEffectFeedbackView));
            try
            {
                var rootRect = (RectTransform)root.transform;
                rootRect.sizeDelta = new Vector2(300f, 84f);
                rootRect.localScale = Vector3.one * 0.01f;
                var canvas = root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 120;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.dynamicPixelsPerUnit = 10f;

                var labelObject = new GameObject(
                    "ValueLabel",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                labelObject.transform.SetParent(root.transform, false);
                var labelRect = (RectTransform)labelObject.transform;
                labelRect.anchorMin = new Vector2(0f, 0f);
                labelRect.anchorMax = new Vector2(1f, 1f);
                labelRect.offsetMin = Vector2.zero;
                labelRect.offsetMax = Vector2.zero;
                var label = labelObject.GetComponent<Text>();
                label.font = font;
                label.fontSize = 44;
                label.fontStyle = FontStyle.Normal;
                label.alignment = TextAnchor.MiddleCenter;
                label.color = Color.white;
                label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Overflow;
                label.verticalOverflow = VerticalWrapMode.Overflow;
                label.text = "+3";

                var view = root.GetComponent<BoardLandingEffectFeedbackView>();
                view.Configure(label);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException(
                        "Could not save board landing-effect feedback prefab.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            var createdRoot = AssetDatabase.LoadAssetAtPath<GameObject>(
                PrefabPath);
            var created = createdRoot != null
                ? createdRoot.GetComponent<BoardLandingEffectFeedbackView>()
                : null;
            Validate(created);
            return created;
        }

        public static GameObject EnsureGiftPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(GiftPath);
            if (existing != null)
                return existing;

            const string materialFolder = "Assets/MazeParty/Art/Minigames/GiftGrab/Materials/";
            var wrap = AssetDatabase.LoadAssetAtPath<Material>(materialFolder + "GiftGrabGiftWrap.mat");
            var ribbon = AssetDatabase.LoadAssetAtPath<Material>(materialFolder + "GiftGrabGiftRibbon.mat");
            if (wrap == null || ribbon == null)
                throw new InvalidOperationException("Gift wrap and ribbon materials are required.");

            EnsureFolder("Assets/MazeParty/Prefabs/Board/World");
            var root = new GameObject("BoardLandingGift");
            try
            {
                CreateGiftPart(root.transform, "Box", PrimitiveType.Cube, Vector3.zero,
                    new Vector3(0.7f, 0.55f, 0.7f), wrap);
                CreateGiftPart(root.transform, "Ribbon X", PrimitiveType.Cube, new Vector3(0f, 0.01f, 0f),
                    new Vector3(0.12f, 0.58f, 0.73f), ribbon);
                CreateGiftPart(root.transform, "Ribbon Z", PrimitiveType.Cube, new Vector3(0f, 0.01f, 0f),
                    new Vector3(0.73f, 0.58f, 0.12f), ribbon);
                CreateGiftPart(root.transform, "Bow Left", PrimitiveType.Sphere, new Vector3(-0.16f, 0.38f, 0f),
                    new Vector3(0.26f, 0.18f, 0.2f), ribbon);
                CreateGiftPart(root.transform, "Bow Right", PrimitiveType.Sphere, new Vector3(0.16f, 0.38f, 0f),
                    new Vector3(0.26f, 0.18f, 0.2f), ribbon);
                return PrefabUtility.SaveAsPrefabAsset(root, GiftPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void CreateGiftPart(Transform parent, string name, PrimitiveType shape,
            Vector3 position, Vector3 scale, Material material)
        {
            var part = GameObject.CreatePrimitive(shape);
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
        }

        public static GameObject EnsureHealingShieldPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(HealingShieldPath);
            if (existing != null)
                return existing;

            var source = SharedVfxProjectSetup.EnsureWaterShieldPrefab();
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            const string materialsFolder = "Assets/MazeParty/Materials/HealingShield";
            EnsureFolder(materialsFolder);
            try
            {
                root.name = "HealingShield";
                var copies = new System.Collections.Generic.Dictionary<Material, Material>();
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (var index = 0; index < materials.Length; index++)
                    {
                        var original = materials[index];
                        if (original == null)
                            continue;
                        if (!copies.TryGetValue(original, out var green))
                        {
                            var path = materialsFolder + "/HealingShield_" + copies.Count + ".mat";
                            green = AssetDatabase.LoadAssetAtPath<Material>(path);
                            if (green == null)
                            {
                                green = new Material(original) { name = "HealingShield_" + copies.Count };
                                foreach (var property in new[] { "_Color", "_RimColor",
                                             "_ColorGradingLight", "_ColorGradingMiddle", "_ColorGradingDark" })
                                {
                                    if (!green.HasProperty(property))
                                        continue;
                                    var color = green.GetColor(property);
                                    var intensity = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
                                    green.SetColor(property, new Color(
                                        intensity * 0.2f, intensity, intensity * 0.35f, color.a));
                                }
                                // The source shader applies its blue color ramp after tinting.
                                // Rotate that final ramp into green as well.
                                green.SetFloat("_HsvShift", 40f);
                                AssetDatabase.CreateAsset(green, path);
                            }
                            copies.Add(original, green);
                        }
                        materials[index] = green;
                    }
                    renderer.sharedMaterials = materials;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, HealingShieldPath);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Validate(BoardLandingEffectFeedbackView view)
        {
            if (view == null || !view.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "Board landing-effect feedback prefab has missing bindings.");
            }

            var canvas = view.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace)
            {
                throw new InvalidOperationException(
                    "Board landing-effect feedback must remain an authored world-space Canvas.");
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            var parent = path.Substring(0, slash);
            var name = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
