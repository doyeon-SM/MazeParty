#if UNITY_EDITOR
using System;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MazeParty.Editor
{
    [InitializeOnLoad]
    public sealed class FeedbackUiProjectSetup : IProcessSceneWithReport
    {
        public const string DiagnosticsPath = "Assets/MazeParty/Prefabs/Multiplayer/UI/DevelopmentDiagnostics.prefab";
        public const string MarkersPath = "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigamePlayerMarkers.prefab";
        public int callbackOrder => 500;
        static FeedbackUiProjectSetup() { EditorApplication.playModeStateChanged += OnPlayMode; }
        private static void OnPlayMode(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && !PlatformContext.IsStoreBuild)
                InstallDiagnosticsForScene(SceneManager.GetActiveScene(), true, false);
        }
        public void OnProcessScene(Scene scene, BuildReport report)
        {
            if (report == null || scene.path != "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity") return;
            InstallDiagnosticsForScene(scene, (report.summary.options & BuildOptions.Development) != 0, PlatformContext.IsStoreBuild);
        }
        public static void InstallDiagnosticsForScene(Scene scene, bool development, bool store)
        {
            var existing = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DevelopmentDiagnosticsPanel>(true)).ToArray();
            if (store || !development)
            {
                foreach (var item in existing) Object.DestroyImmediate(item.gameObject);
                return;
            }
            if (existing.Length > 0 || (Application.isPlaying && Object.FindAnyObjectByType<DevelopmentDiagnosticsPanel>() != null)) return;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DiagnosticsPath);
            if (prefab == null || !prefab.GetComponent<DevelopmentDiagnosticsPanel>().HasRequiredReferences)
                throw new BuildFailedException("Development diagnostics prefab is missing or incomplete.");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "DevelopmentDiagnostics";
        }

        [MenuItem("MazeParty/Feedback/Install Minigame Markers And Development Panel")]
        public static void Install()
        {
            EnsureDiagnostics();
            EnsureMarkers();
            UpgradeLegend();
            var path = "Assets/MazeParty/Scenes/Board/Board.unity";
            var scene = SceneManager.GetSceneByPath(path);
            var opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                if (!scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MinigameLocalPlayerHighlight>(true)).Any())
                {
                    PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(MarkersPath), scene);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.SaveAssets();
        }
        private static void EnsureMarkers()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(MarkersPath) != null) return;
            BoardLocalPlayerMarkerProjectSetup.EnsurePrefab();
            AssetDatabase.CopyAsset(BoardLocalPlayerMarkerProjectSetup.PrefabPath, MarkersPath);
            var root = PrefabUtility.LoadPrefabContents(MarkersPath);
            try
            {
                root.name = "MinigamePlayerMarkers";
                root.AddComponent<MinigameLocalPlayerHighlight>().Configure(root.GetComponent<BoardLocalPlayerMarkerView>());
                var data = new SerializedObject(root.GetComponent<BoardLocalPlayerMarkerView>());
                data.FindProperty("screenOffset").floatValue = 32f;
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, MarkersPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void EnsureDiagnostics()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(DiagnosticsPath) != null) return;
            var root = new GameObject("DevelopmentDiagnostics", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(DevelopmentDiagnosticsPanel));
            try
            {
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 1000; canvas.enabled = false;
                var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
                var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                panel.transform.SetParent(root.transform, false);
                var rect = (RectTransform)panel.transform; rect.anchorMin = rect.anchorMax = new Vector2(1, 1); rect.pivot = new Vector2(1, 1);
                rect.anchoredPosition = new Vector2(-24, -24); rect.sizeDelta = new Vector2(1060, 760);
                panel.GetComponent<Image>().color = new Color(.025f, .035f, .055f, .96f); panel.GetComponent<Image>().raycastTarget = false;
                Label(rect, "Title", "DEVELOPMENT DIAGNOSTICS", 24, 24, 1012, 40, 26, new Color(.4f, .86f, 1f));
                var metrics = Label(rect, "Metrics", "Waiting for frame samples...", 24, 80, 1012, 210, 22, Color.white);
                Label(rect, "LogHeading", "RECENT LOGS", 24, 302, 1012, 32, 22, new Color(.4f, .86f, 1f));
                var logs = Label(rect, "Logs", "", 24, 344, 1012, 344, 17, new Color(.8f, .85f, .9f));
                logs.supportRichText = false;
                var status = Label(rect, "Status", "L  Close     C  Copy logs + metrics", 24, 712, 1012, 32, 19, new Color(.65f, .75f, .85f));
                root.GetComponent<DevelopmentDiagnosticsPanel>().Configure(canvas, metrics, logs, status);
                PrefabUtility.SaveAsPrefabAsset(root, DiagnosticsPath);
            }
            finally { Object.DestroyImmediate(root); }
        }
        private static Text Label(RectTransform parent, string name, string copy, float x, float y, float w, float h, int size, Color color)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            root.transform.SetParent(parent, false);
            var rect = (RectTransform)root.transform; rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h);
            var text = root.GetComponent<Text>(); text.text = copy; text.font = GameFonts.Current; text.fontSize = size;
            text.fontStyle = FontStyle.Normal; text.color = color; text.raycastTarget = false; text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        private static void UpgradeLegend()
        {
            const string path = "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var panel = root.GetComponentsInChildren<RectTransform>(true).Single(item => item.name == "BoardMapIconInfo");
                var full = root.GetComponentsInChildren<BoardMinimapView>(true).First(item => item.name == "Board Full Map");
                var data = new SerializedObject(full);
                var sprite = data.FindProperty("homeFilledIcon").objectReferenceValue as Sprite;
                var key = panel.Find("Icon Info 3");
                var keyIcon = key.Find("Icon").GetComponent<BoardMapIcon>();
                keyIcon.SetIcon(BoardMapIconKind.Respawn, sprite); keyIcon.color = data.FindProperty("keyShopDeviceColor").colorValue;
                var rows = new[] { new { Index = 14, Copy = ": ITEM SHOP", Kind = BoardMapIconKind.Respawn, Color = data.FindProperty("itemShopDeviceColor").colorValue },
                    new { Index = 15, Copy = ": CHEST", Kind = BoardMapIconKind.Chest, Color = data.FindProperty("chestDeviceColor").colorValue } };
                foreach (var row in rows)
                {
                    if (panel.Find("Icon Info " + row.Index) != null) continue;
                    var instance = Object.Instantiate(key.gameObject, panel, false); instance.name = "Icon Info " + row.Index;
                    ((RectTransform)instance.transform).anchoredPosition = new Vector2(6, -5 - row.Index * 35);
                    var icon = instance.GetComponentInChildren<BoardMapIcon>(true); icon.SetIcon(row.Kind, row.Kind == BoardMapIconKind.Respawn ? sprite : null); icon.color = row.Color;
                    var label = instance.GetComponentInChildren<Text>(true); label.text = row.Copy;
                    label.GetComponent<LocalizedText>().Configure(row.Copy);
                }
                panel.sizeDelta = new Vector2(panel.sizeDelta.x, Mathf.Max(panel.sizeDelta.y, 570));
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
#endif
