using System;
using System.IO;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>One-time additive upgrade; existing authored designs are preserved on rerun.</summary>
    public static class BoardChestProjectSetup
    {
        public const string ChestPath = "Assets/MazeParty/Prefabs/Board/Devices/BlueEpicChest.prefab";
        private const string SourcePath = "Assets/Ignore/Stylized 3D Animated Chests – FREE Demo/URP/Chsets/Epic Chest/EpicChest_PF_URP.prefab";
        private const string MaterialPath = "Assets/MazeParty/Materials/Board/BlueEpicChest.mat";

        [MenuItem("MazeParty/Board/Install Item Chests")]
        public static void Install()
        {
            EnsureChest();
            var world = Resources.Load<BoardWorldPrefabs>(BoardWorldPrefabs.ResourcePath);
            if (world == null) throw new InvalidOperationException("Missing BoardWorldPrefabs.");
            var worldData = new SerializedObject(world);
            if (worldData.FindProperty("chest").objectReferenceValue == null)
            {
                worldData.FindProperty("chest").objectReferenceValue =
                    AssetDatabase.LoadAssetAtPath<GameObject>(ChestPath).GetComponent<BoardChestVisual>();
                worldData.ApplyModifiedPropertiesWithoutUndo();
            }
            Upgrade("Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab", EnsureMaps);
            Upgrade("Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab", EnsureMaps);
            Upgrade("Assets/MazeParty/Prefabs/Board/UI/BoardItemCanvas.prefab", EnsureNotice);
            AssetDatabase.SaveAssets();
        }
        internal static void EnsureChest()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ChestPath) != null) return;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (source == null) throw new InvalidOperationException("EpicChest URP source is missing: " + SourcePath);
            Directory.CreateDirectory(Path.GetDirectoryName(ChestPath));
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            AssetDatabase.Refresh();
            var root = new GameObject("Blue Epic Chest");
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                var bounds = renderers[0].bounds;
                foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                var scale = 2.1f / Mathf.Max(.01f, bounds.size.x);
                model.transform.localScale *= scale;
                model.transform.localPosition -= new Vector3(bounds.center.x, bounds.min.y, bounds.center.z) * scale;
                var blue = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
                if (blue == null)
                {
                    var original = AssetDatabase.LoadAssetAtPath<Material>(
                        "Assets/Ignore/Stylized 3D Animated Chests – FREE Demo/URP/Chsets/Epic Chest/EpicChest_02_MAT_URP.mat");
                    blue = new Material(original) { name = "BlueEpicChest" };
                    blue.SetColor("_BaseColor", new Color(.045f, .3f, 1f));
                    blue.SetColor("_Color", new Color(.045f, .3f, 1f));
                    AssetDatabase.CreateAsset(blue, MaterialPath);
                }
                var bodyPath = "Assets/MazeParty/Materials/Board/BlueEpicChestBody.mat";
                var body = AssetDatabase.LoadAssetAtPath<Material>(bodyPath);
                if (body == null)
                {
                    body = new Material(AssetDatabase.LoadAssetAtPath<Material>(
                        "Assets/Ignore/Stylized 3D Animated Chests – FREE Demo/URP/Chsets/Epic Chest/EpicChest_01_MAT_URP.mat"));
                    body.name = "BlueEpicChestBody";
                    body.SetColor("_BaseColor", new Color(.055f, .6f, 1f));
                    body.SetColor("_Color", new Color(.055f, .6f, 1f));
                    AssetDatabase.CreateAsset(body, bodyPath);
                }
                foreach (var renderer in renderers)
                {
                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        if (materials[i] != null && materials[i].name.Contains("EpicChest_02")) materials[i] = blue;
                        else if (materials[i] != null && materials[i].name.Contains("EpicChest_01")) materials[i] = body;
                    }
                    renderer.sharedMaterials = materials;
                }
                var collider = root.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(2.3f, Mathf.Max(1.1f, bounds.size.y * scale),
                    Mathf.Max(1.2f, bounds.size.z * scale));
                collider.center = Vector3.up * (collider.size.y * .5f);
                var point = new GameObject("Observation Point").transform;
                point.SetParent(root.transform, false);
                point.localPosition = Vector3.up * collider.size.y * .65f;
                var visual = root.AddComponent<BoardChestVisual>();
                var animator = model.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = false;
                var data = new SerializedObject(visual);
                data.FindProperty("chestAnimator").objectReferenceValue = animator;
                data.FindProperty("interactionCollider").objectReferenceValue = collider;
                data.FindProperty("observationPoint").objectReferenceValue = point;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, ChestPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }
        private static void Upgrade(string path, Action<GameObject> update)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { update(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void EnsureMaps(GameObject root)
        {
            foreach (var map in root.GetComponentsInChildren<BoardMinimapView>(true))
            {
                var data = new SerializedObject(map);
                var markers = data.FindProperty("deviceIcons");
                markers.arraySize = BoardMinimapView.DeviceMarkerCount;
                var layer = (RectTransform)data.FindProperty("landingEffectLayer").objectReferenceValue;
                for (var i = 0; i < markers.arraySize; i++)
                {
                    if (markers.GetArrayElementAtIndex(i).objectReferenceValue != null) continue;
                    var obj = new GameObject("Device Marker " + i, typeof(RectTransform),
                        typeof(CanvasRenderer), typeof(BoardMapIcon));
                    obj.layer = layer.gameObject.layer;
                    obj.transform.SetParent(layer, false);
                    var icon = obj.GetComponent<BoardMapIcon>();
                    icon.raycastTarget = false;
                    icon.color = Color.white;
                    icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = Vector2.one * .5f;
                    icon.rectTransform.sizeDelta = Vector2.one * 17f;
                    obj.SetActive(false);
                    markers.GetArrayElementAtIndex(i).objectReferenceValue = icon;
                }
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }
        private static void EnsureNotice(GameObject root)
        {
            var bindings = root.GetComponent<BoardItemCanvasBindings>();
            if (bindings.ChestNotice != null)
            {
                if (!bindings.ChestNotice.HasRequiredReferences) throw new InvalidOperationException("Chest notice bindings are incomplete.");
                return;
            }
            var panel = Rect("Chest Acquisition Notice", root.transform, new Vector2(650, 70));
            panel.anchorMin = panel.anchorMax = new Vector2(.5f, .25f);
            var background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(.13f, .075f, .22f, .95f);
            background.raycastTarget = false;
            var icon = Rect("Item Icon", panel, new Vector2(48, 48)).gameObject.AddComponent<Image>();
            icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0, .5f);
            icon.rectTransform.anchoredPosition = new Vector2(42, 0);
            icon.raycastTarget = false;
            icon.preserveAspect = true;
            var text = Rect("Acquired Item", panel, new Vector2(550, 60)).gameObject.AddComponent<Text>();
            text.rectTransform.anchoredPosition = new Vector2(38, 0);
            text.font = bindings.TooltipText.font;
            text.fontSize = 23;
            text.fontStyle = FontStyle.Normal;
            text.alignment = TextAnchor.MiddleLeft;
            text.color = Color.white;
            text.raycastTarget = false;
            text.supportRichText = false;
            var notice = root.AddComponent<BoardChestNoticeView>();
            var data = new SerializedObject(notice);
            data.FindProperty("panel").objectReferenceValue = panel.gameObject;
            data.FindProperty("itemIcon").objectReferenceValue = icon;
            data.FindProperty("message").objectReferenceValue = text;
            data.ApplyModifiedPropertiesWithoutUndo();
            var bindingData = new SerializedObject(bindings);
            bindingData.FindProperty("chestNotice").objectReferenceValue = notice;
            bindingData.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.layer = parent.gameObject.layer;
            var rect = (RectTransform)obj.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = size;
            return rect;
        }
    }
}
