using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>
    /// Adds the authored, shared landing-event popup without rebuilding or
    /// restyling the existing Board Canvas prefab.
    /// </summary>
    public static class BoardEventPopupProjectSetup
    {
        public const string ModuleObjectName = "BoardEventPopupPanel";
        public const string MessageObjectName = "BoardEventPopupMessage";
        public const string ModulePrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/Modules/BoardEventPopupPanel.prefab";

        private const string BoardCanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        private const string ModuleFolder =
            "Assets/MazeParty/Prefabs/Board/UI/Modules";

        [MenuItem("MazeParty/Board/Add Shared Board Event Popup")]
        public static void AddSharedBoardEventPopup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Board event popup migration requires Edit Mode.");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardCanvasPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab must exist before adding its event popup.");
            }

            EnsureInstalled(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "The shared board event popup was added without rebuilding " +
                "the existing Board Canvas design.");
        }

        public static GameObject EnsureInstalled(GameObject prefab)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            var modulePrefab = EnsureModulePrefab();
            if (HasInstalledContract(prefab))
            {
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(
                BoardCanvasPrefabPath);
            try
            {
                var panel = EnsureInstance(contents.transform, modulePrefab);
                Bind(contents, panel);
                if (PrefabUtility.SaveAsPrefabAsset(
                        contents,
                        BoardCanvasPrefabPath) == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save the Board Canvas event popup migration.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardCanvasPrefabPath);
        }

        public static GameObject EnsureTemplateInstance(Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            return EnsureInstance(parent, EnsureModulePrefab());
        }

        private static GameObject EnsureModulePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                ModulePrefabPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(ModuleFolder);
            var font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity LegacyRuntime.ttf was not found.");
            }

            var root = new GameObject(
                ModuleObjectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LocalizedFontScope));
            try
            {
                SetUiLayer(root);
                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot =
                    new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.sizeDelta = new Vector2(760f, 220f);

                var background = root.GetComponent<Image>();
                background.color = new Color(0.02f, 0.035f, 0.07f, 0.98f);
                background.raycastTarget = false;

                var messageObject = new GameObject(
                    MessageObjectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                SetUiLayer(messageObject);
                messageObject.transform.SetParent(root.transform, false);
                var messageRect = (RectTransform)messageObject.transform;
                messageRect.anchorMin = Vector2.zero;
                messageRect.anchorMax = Vector2.one;
                messageRect.pivot = new Vector2(0.5f, 0.5f);
                messageRect.offsetMin = new Vector2(36f, 28f);
                messageRect.offsetMax = new Vector2(-36f, -28f);

                var message = messageObject.GetComponent<Text>();
                message.font = font;
                message.text = string.Empty;
                message.fontSize = 28;
                message.color = new Color(0.93f, 0.96f, 1f, 1f);
                message.alignment = TextAnchor.MiddleCenter;
                message.horizontalOverflow = HorizontalWrapMode.Wrap;
                message.verticalOverflow = VerticalWrapMode.Overflow;
                message.raycastTarget = false;

                root.SetActive(false);
                var created = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ModulePrefabPath);
                if (created == null)
                {
                    throw new InvalidOperationException(
                        "Failed to create the Board event popup module prefab.");
                }

                return created;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject EnsureInstance(
            Transform parent,
            GameObject modulePrefab)
        {
            var existing = FindDescendant(parent, ModuleObjectName);
            if (existing != null)
            {
                if (!IsExpectedModule(existing.gameObject))
                {
                    throw new InvalidOperationException(
                        "BoardCanvas already contains '" + ModuleObjectName +
                        "', but it is not connected to " + ModulePrefabPath +
                        ". It was left untouched so authored design is not lost.");
                }

                return existing.gameObject;
            }

            var instance = PrefabUtility.InstantiatePrefab(
                modulePrefab,
                parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate the Board event popup module.");
            }

            instance.name = ModuleObjectName;
            instance.SetActive(false);
            var reconnect = FindDescendant(parent, "ReconnectOverlay");
            if (reconnect != null && reconnect.parent == parent)
            {
                instance.transform.SetSiblingIndex(
                    reconnect.GetSiblingIndex());
            }

            return instance;
        }

        private static void Bind(GameObject root, GameObject panel)
        {
            var bindings = root.GetComponent<BoardCanvasBindings>();
            var message = FindDescendant(
                panel.transform,
                MessageObjectName)?.GetComponent<Text>();
            if (bindings == null || message == null)
            {
                throw new InvalidOperationException(
                    "Board event popup requires BoardCanvasBindings and its " +
                    "authored message Text.");
            }

            var serializedBindings = new SerializedObject(bindings);
            var references = serializedBindings.FindProperty("references");
            var panelProperty = references?.FindPropertyRelative(
                "BoardEventPopupPanel");
            var messageProperty = references?.FindPropertyRelative(
                "BoardEventPopupMessage");
            if (panelProperty == null || messageProperty == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvasBindings is missing its event popup fields.");
            }

            panelProperty.objectReferenceValue = panel;
            messageProperty.objectReferenceValue = message;
            serializedBindings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bindings);
        }

        private static bool HasInstalledContract(GameObject prefab)
        {
            var panel = FindDescendant(
                prefab != null ? prefab.transform : null,
                ModuleObjectName);
            if (panel == null || !IsExpectedModule(panel.gameObject))
            {
                return false;
            }

            var message = FindDescendant(
                panel,
                MessageObjectName)?.GetComponent<Text>();
            var bindings = prefab.GetComponent<BoardCanvasBindings>();
            if (bindings == null || message == null)
            {
                return false;
            }

            var serializedBindings = new SerializedObject(bindings);
            var references = serializedBindings.FindProperty("references");
            var panelProperty = references?.FindPropertyRelative(
                "BoardEventPopupPanel");
            var messageProperty = references?.FindPropertyRelative(
                "BoardEventPopupMessage");
            return panelProperty != null &&
                   messageProperty != null &&
                   panelProperty.objectReferenceValue == panel.gameObject &&
                   messageProperty.objectReferenceValue == message;
        }

        private static bool IsExpectedModule(GameObject candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            var source = PrefabUtility.GetCorrespondingObjectFromSource(
                candidate);
            return source != null &&
                   AssetDatabase.GetAssetPath(source) == ModulePrefabPath &&
                   PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                       candidate) == ModulePrefabPath;
        }

        private static Transform FindDescendant(
            Transform root,
            string objectName)
        {
            if (root == null)
            {
                return null;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == objectName)
                {
                    return transforms[index];
                }
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            if (slash <= 0)
            {
                throw new InvalidOperationException(
                    "Invalid asset folder path: " + path);
            }

            var parent = path.Substring(0, slash);
            var name = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void SetUiLayer(GameObject target)
        {
            target.layer = LayerMask.NameToLayer("UI");
        }
    }

    /// <summary>
    /// Adds the authored, non-interactive board kill feed without rebuilding or
    /// restyling the existing Board Canvas prefab.
    /// </summary>
    public static class BoardKillFeedProjectSetup
    {
        public const string ModuleObjectName = "BoardKillFeedPanel";
        public const string MessageObjectName = "BoardKillFeedMessage";
        public const string ModulePrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/Modules/BoardKillFeedPanel.prefab";

        private const string BoardCanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        private const string ModuleFolder =
            "Assets/MazeParty/Prefabs/Board/UI/Modules";

        [MenuItem("MazeParty/Board/Add Shared Board Kill Feed")]
        public static void AddSharedBoardKillFeed()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Board kill feed migration requires Edit Mode.");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardCanvasPrefabPath);
            if (prefab == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab must exist before adding its kill feed.");
            }

            EnsureInstalled(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "The shared board kill feed was added without rebuilding " +
                "the existing Board Canvas design.");
        }

        public static GameObject EnsureInstalled(GameObject prefab)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            var modulePrefab = EnsureModulePrefab();
            if (HasInstalledContract(prefab))
            {
                return prefab;
            }

            var contents = PrefabUtility.LoadPrefabContents(
                BoardCanvasPrefabPath);
            try
            {
                var panel = EnsureInstance(contents.transform, modulePrefab);
                Bind(contents, panel);
                if (PrefabUtility.SaveAsPrefabAsset(
                        contents,
                        BoardCanvasPrefabPath) == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save the Board Canvas kill feed migration.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                BoardCanvasPrefabPath);
        }

        public static GameObject EnsureTemplateInstance(Transform parent)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            return EnsureInstance(parent, EnsureModulePrefab());
        }

        private static GameObject EnsureModulePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                ModulePrefabPath);
            if (existing != null)
            {
                return existing;
            }

            EnsureFolder(ModuleFolder);
            var font = Resources.GetBuiltinResource<Font>(
                "LegacyRuntime.ttf");
            if (font == null)
            {
                throw new InvalidOperationException(
                    "Unity LegacyRuntime.ttf was not found.");
            }

            var root = new GameObject(
                ModuleObjectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(LocalizedFontScope));
            try
            {
                SetUiLayer(root);
                var rect = (RectTransform)root.transform;
                rect.anchorMin = rect.anchorMax = rect.pivot =
                    new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f, -72f);
                rect.sizeDelta = new Vector2(720f, 64f);

                var background = root.GetComponent<Image>();
                background.color = new Color(0.02f, 0.035f, 0.07f, 0.94f);
                background.raycastTarget = false;

                var messageObject = new GameObject(
                    MessageObjectName,
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                SetUiLayer(messageObject);
                messageObject.transform.SetParent(root.transform, false);
                var messageRect = (RectTransform)messageObject.transform;
                messageRect.anchorMin = Vector2.zero;
                messageRect.anchorMax = Vector2.one;
                messageRect.pivot = new Vector2(0.5f, 0.5f);
                messageRect.offsetMin = new Vector2(22f, 8f);
                messageRect.offsetMax = new Vector2(-22f, -8f);

                var message = messageObject.GetComponent<Text>();
                message.font = font;
                message.text = string.Empty;
                message.fontSize = 24;
                message.fontStyle = FontStyle.Normal;
                message.color = new Color(0.93f, 0.96f, 1f, 1f);
                message.alignment = TextAnchor.MiddleCenter;
                message.supportRichText = true;
                message.horizontalOverflow = HorizontalWrapMode.Overflow;
                message.verticalOverflow = VerticalWrapMode.Truncate;
                message.raycastTarget = false;

                root.SetActive(false);
                var created = PrefabUtility.SaveAsPrefabAsset(
                    root,
                    ModulePrefabPath);
                if (created == null)
                {
                    throw new InvalidOperationException(
                        "Failed to create the Board kill feed module prefab.");
                }

                return created;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static GameObject EnsureInstance(
            Transform parent,
            GameObject modulePrefab)
        {
            var existing = FindDescendant(parent, ModuleObjectName);
            if (existing != null)
            {
                if (!IsExpectedModule(existing.gameObject))
                {
                    throw new InvalidOperationException(
                        "BoardCanvas already contains '" + ModuleObjectName +
                        "', but it is not connected to " + ModulePrefabPath +
                        ". It was left untouched so authored design is not lost.");
                }

                return existing.gameObject;
            }

            var instance = PrefabUtility.InstantiatePrefab(
                modulePrefab,
                parent) as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate the Board kill feed module.");
            }

            instance.name = ModuleObjectName;
            instance.SetActive(false);
            var reconnect = FindDescendant(parent, "ReconnectOverlay");
            if (reconnect != null && reconnect.parent == parent)
            {
                instance.transform.SetSiblingIndex(
                    reconnect.GetSiblingIndex());
            }

            return instance;
        }

        private static void Bind(GameObject root, GameObject panel)
        {
            var bindings = root.GetComponent<BoardCanvasBindings>();
            var message = FindDescendant(
                panel.transform,
                MessageObjectName)?.GetComponent<Text>();
            if (bindings == null || message == null)
            {
                throw new InvalidOperationException(
                    "Board kill feed requires BoardCanvasBindings and its " +
                    "authored message Text.");
            }

            var serializedBindings = new SerializedObject(bindings);
            var references = serializedBindings.FindProperty("references");
            var panelProperty = references?.FindPropertyRelative(
                "BoardKillFeedPanel");
            var messageProperty = references?.FindPropertyRelative(
                "BoardKillFeedMessage");
            if (panelProperty == null || messageProperty == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvasBindings is missing its kill feed fields.");
            }

            panelProperty.objectReferenceValue = panel;
            messageProperty.objectReferenceValue = message;
            serializedBindings.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bindings);
        }

        private static bool HasInstalledContract(GameObject prefab)
        {
            var panel = FindDescendant(
                prefab != null ? prefab.transform : null,
                ModuleObjectName);
            if (panel == null || !IsExpectedModule(panel.gameObject))
            {
                return false;
            }

            var message = FindDescendant(
                panel,
                MessageObjectName)?.GetComponent<Text>();
            var bindings = prefab.GetComponent<BoardCanvasBindings>();
            if (bindings == null || message == null)
            {
                return false;
            }

            var serializedBindings = new SerializedObject(bindings);
            var references = serializedBindings.FindProperty("references");
            var panelProperty = references?.FindPropertyRelative(
                "BoardKillFeedPanel");
            var messageProperty = references?.FindPropertyRelative(
                "BoardKillFeedMessage");
            return panelProperty != null &&
                   messageProperty != null &&
                   panelProperty.objectReferenceValue == panel.gameObject &&
                   messageProperty.objectReferenceValue == message;
        }

        private static bool IsExpectedModule(GameObject candidate)
        {
            if (candidate == null)
            {
                return false;
            }

            var source = PrefabUtility.GetCorrespondingObjectFromSource(
                candidate);
            return source != null &&
                   AssetDatabase.GetAssetPath(source) == ModulePrefabPath &&
                   PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                       candidate) == ModulePrefabPath;
        }

        private static Transform FindDescendant(
            Transform root,
            string objectName)
        {
            if (root == null)
            {
                return null;
            }

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == objectName)
                {
                    return transforms[index];
                }
            }

            return null;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var slash = path.LastIndexOf('/');
            if (slash <= 0)
            {
                throw new InvalidOperationException(
                    "Invalid asset folder path: " + path);
            }

            var parent = path.Substring(0, slash);
            var name = path.Substring(slash + 1);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static void SetUiLayer(GameObject target)
        {
            target.layer = LayerMask.NameToLayer("UI");
        }
    }
}
