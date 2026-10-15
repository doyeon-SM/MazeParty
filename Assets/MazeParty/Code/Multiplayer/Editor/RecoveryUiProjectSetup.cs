using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MazeParty.Editor
{
    public static class RecoveryUiProjectSetup
    {
        private const string LobbyPath = "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";
        private const string MenuPath = "Assets/MazeParty/Prefabs/Multiplayer/UI/GameMenuCanvas.prefab";

        [MenuItem("MazeParty/Setup/Upgrade Recovery UI")]
        public static void Upgrade()
        {
            EditPrefab(LobbyPath, EnsureLobby);
            EditPrefab(MenuPath, EnsureWait);
            AssetDatabase.SaveAssets();
            Debug.Log("Recovery UI prefabs upgraded without rebuilding existing designs.");
        }
        private static void EditPrefab(string path, Action<GameObject> action)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { action(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        public static void EnsureLobby(GameObject root)
        {
            if (root.GetComponent<SessionRecoveryView>() != null) return;
            var lobby = root.GetComponent<OnlineLobbyView>();
            var serialized = new SerializedObject(lobby);
            var create = (Button)serialized.FindProperty("createButton").objectReferenceValue;
            var font = create.GetComponentInChildren<Text>(true).font;
            var view = root.AddComponent<SessionRecoveryView>();
            var restart = CloneButton(create, "Restart Saved Room", "Restart", 1);
            var reconnect = CloneButton(create, "Reconnect Entry", "Reconnect", 2);
            restart.transform.SetSiblingIndex(create.transform.GetSiblingIndex());
            reconnect.transform.SetSiblingIndex(restart.transform.GetSiblingIndex());
            var details = Panel("Saved Game Details", restart.transform, new Vector2(360, 210));
            details.anchorMin = details.anchorMax = new Vector2(1, 1);
            details.pivot = new Vector2(0, 1);
            details.anchoredPosition = new Vector2(16, 0);
            var detailsText = Label("Saved Players and Date", details, font, "", 22, new Vector2(0, 0), new Vector2(324, 180), false);
            detailsText.alignment = TextAnchor.UpperLeft;
            detailsText.supportRichText = false;
            Bind(restart.gameObject.AddComponent<RecoveryDetailsHover>(), "detailsRoot", details.gameObject);

            var popup = Modal(root.transform, "Reconnect Popup", font, "Reconnect to the game", out var body, out var buttons);
            var reconnectButton = Button("Reconnect Button", buttons, font, "Reconnect", new Vector2(-140, 0));
            var close = Button("Close Reconnect", buttons, font, "Close", new Vector2(140, 0));
            var confirm = Modal(root.transform, "Create Room Confirmation", font, "Create a new room?", out var confirmBody, out var confirmButtons);
            confirmBody.text = "You can reconnect to the previous game. Creating a new room ends the previous room and discards its saved game. Create a new room?";
            confirmBody.gameObject.AddComponent<LocalizedText>().Configure(confirmBody.text);
            var createConfirmed = Button("Confirm Create", confirmButtons, font, "Create", new Vector2(-140, 0));
            var cancel = Button("Cancel Create", confirmButtons, font, "Cancel", new Vector2(140, 0));
            Bind(view, "restartButton", restart); Bind(view, "reconnectEntryButton", reconnect);
            Bind(view, "reconnectPopup", popup); Bind(view, "reconnectButton", reconnectButton);
            Bind(view, "closeReconnectButton", close); Bind(view, "reconnectMessage", body);
            Bind(view, "detailsRoot", details.gameObject); Bind(view, "detailsText", detailsText);
            Bind(view, "createConfirmationPopup", confirm); Bind(view, "confirmCreateButton", createConfirmed);
            Bind(view, "cancelCreateButton", cancel); Bind(lobby, "recoveryView", view);
            popup.SetActive(false); confirm.SetActive(false); details.gameObject.SetActive(false);
            restart.gameObject.SetActive(false); reconnect.gameObject.SetActive(false);
        }
        public static void EnsureWait(GameObject root)
        {
            if (root.GetComponent<ReconnectWaitView>() != null) return;
            var font = root.GetComponentInChildren<Text>(true).font;
            var view = root.AddComponent<ReconnectWaitView>();
            var panel = Panel("Reconnect Wait Actions", root.transform, new Vector2(640, 150));
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0);
            panel.pivot = new Vector2(0.5f, 0);
            panel.anchoredPosition = new Vector2(0, 36);
            var text = Label("Reconnect Timer", panel, font, "", 24, new Vector2(0, 40), new Vector2(600, 45), false);
            var button = Button("Return to Waiting Room", panel, font, "Go to Waiting Room", new Vector2(0, -28));
            ((RectTransform)button.transform).sizeDelta = new Vector2(360, 52);
            Bind(view, "panel", panel.gameObject); Bind(view, "message", text); Bind(view, "returnToLobbyButton", button);
            panel.gameObject.SetActive(false);
        }
        private static Button CloneButton(Button source, string name, string label, int row)
        {
            var copy = Object.Instantiate(source, source.transform.parent);
            copy.name = name;
            var rect = (RectTransform)copy.transform;
            var layout = copy.transform.parent.GetComponent<LayoutGroup>();
            if (layout == null || !layout.enabled)
                rect.anchoredPosition += new Vector2(0, (rect.rect.height + 12) * row);
            var text = copy.GetComponentInChildren<Text>(true);
            text.text = label;
            var localized = text.GetComponent<LocalizedText>() ?? text.gameObject.AddComponent<LocalizedText>();
            localized.Configure(label);
            copy.onClick = new Button.ButtonClickedEvent();
            return copy;
        }
        private static GameObject Modal(Transform parent, string name, Font font, string title, out Text body, out RectTransform buttons)
        {
            var dim = Rect(name, parent);
            dim.anchorMin = Vector2.zero; dim.anchorMax = Vector2.one; dim.offsetMin = dim.offsetMax = Vector2.zero;
            var shade = dim.gameObject.AddComponent<Image>(); shade.color = new Color(0, 0, 0, 0.75f);
            var panel = Panel(name + " Panel", dim, new Vector2(650, 360));
            Label("Title", panel, font, title, 30, new Vector2(0, 126), new Vector2(600, 48), true);
            body = Label("Message", panel, font, "", 24, new Vector2(0, 25), new Vector2(586, 142), false);
            body.resizeTextForBestFit = true; body.resizeTextMinSize = 18; body.resizeTextMaxSize = 24;
            buttons = Rect("Actions", panel); buttons.anchoredPosition = new Vector2(0, -120); buttons.sizeDelta = new Vector2(590, 56);
            return dim.gameObject;
        }
        private static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>(); rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
            return rect;
        }
        private static RectTransform Panel(string name, Transform parent, Vector2 size)
        {
            var rect = Rect(name, parent); rect.sizeDelta = size;
            rect.gameObject.AddComponent<Image>().color = new Color(0.065f, 0.09f, 0.14f, 0.98f);
            return rect;
        }
        private static Text Label(string name, Transform parent, Font font, string value, int size, Vector2 position, Vector2 dimensions, bool localized)
        {
            var rect = Rect(name, parent); rect.anchoredPosition = position; rect.sizeDelta = dimensions;
            var label = rect.gameObject.AddComponent<Text>(); label.font = font; label.fontSize = size;
            label.text = value; label.color = Color.white; label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            if (localized) label.gameObject.AddComponent<LocalizedText>().Configure(value);
            return label;
        }
        private static Button Button(string name, Transform parent, Font font, string caption, Vector2 position)
        {
            var rect = Rect(name, parent); rect.anchoredPosition = position; rect.sizeDelta = new Vector2(260, 54);
            var image = rect.gameObject.AddComponent<Image>(); image.color = new Color(0.14f, 0.31f, 0.5f, 1);
            var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            Label("Label", rect, font, caption, 24, Vector2.zero, new Vector2(244, 46), true);
            return button;
        }
        private static void Bind(Object target, string field, Object value)
        {
            var serialized = new SerializedObject(target);
            var property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException(target.name + " missing field " + field);
            property.objectReferenceValue = value; serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
