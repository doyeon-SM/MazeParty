using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>Additive migration only. Existing menu and privacy designs are preserved.</summary>
    public static class PrivacyMenuProjectSetup
    {
        public static void EnsureAdded()
        {
            var root = PrefabUtility.LoadPrefabContents(GameMenuProjectSetup.CanvasPrefabPath);
            try
            {
                var existing = root.GetComponent<PrivacyMenuBindings>();
                if (existing != null)
                {
                    if (!existing.HasRequiredReferences || root.GetComponent<PrivacyMenuView>()?.Bindings != existing)
                        throw new InvalidOperationException("Existing privacy menu bindings are incomplete.");
                    return;
                }
                var menu = root.GetComponent<GameMenuBindings>();
                var header = menu.MenuRoot.transform.Find("Menu Panel/Header");
                if (header == null) throw new InvalidOperationException("Settings header missing.");
                var bindings = root.AddComponent<PrivacyMenuBindings>();
                var view = root.AddComponent<PrivacyMenuView>();
                var open = Button(header, "Privacy Button", "Privacy & Support", false);
                var rect = (RectTransform)open.transform;
                rect.anchorMin = new Vector2(.42f, 0); rect.anchorMax = new Vector2(.9f, 1);
                rect.offsetMin = new Vector2(0, 6); rect.offsetMax = new Vector2(0, -6);
                var panelRoot = Blocker(root.transform, "Privacy & Support");
                var panel = Panel(panelRoot.transform, "Privacy Panel", new Vector2(1000, 940));
                Label(panel, "Title", "Privacy & Support", 32, 22, 46, true);
                Label(panel, "Scope", PrivacyMenuView.ScopeText, 24, 82, 245, true);
                Label(panel, "Email", PrivacyMenuView.SupportEmail, 24, 337, 36, false);
                var account = Label(panel, "Account", "", 22, 385, 140, false);
                var status = Label(panel, "Status", "", 23, 537, 95, false);
                var copy = Button(panel, "Copy Support", "Copy Support Details", false); Place(copy.transform, 24, 644, 466, 56);
                var email = Button(panel, "Email Support", "Email Support", false); Place(email.transform, 510, 644, 466, 56);
                var delete = Button(panel, "Delete Account", "Delete Unity Account", true); Place(delete.transform, 24, 716, 952, 56);
                var resume = Button(panel, "Resume Online", "Use Online Again", false); Place(resume.transform, 24, 788, 952, 56);
                var close = Button(panel, "Close Privacy", "Close", false); Place(close.transform, 24, 860, 952, 56);
                var confirmRoot = Blocker(root.transform, "Privacy Confirmation");
                var confirmPanel = Panel(confirmRoot.transform, "Confirm Panel", new Vector2(960, 620));
                var confirmText = Label(confirmPanel, "Message", "", 26, 30, 450, false);
                var confirm = Button(confirmPanel, "Confirm Privacy", "Delete Permanently", true);
                Place(confirm.transform, 24, 532, 446, 56);
                var cancel = Button(confirmPanel, "Cancel Privacy", "Cancel", false);
                Place(cancel.transform, 490, 532, 446, 56);
                // The action label is dynamic: the view owns translation for delete/resume.
                var confirmLabel = confirm.GetComponentInChildren<Text>(true);
                UnityEngine.Object.DestroyImmediate(confirmLabel.GetComponent<LocalizedText>());
                var so = new SerializedObject(bindings);
                Bind(so, "openButton", open); Bind(so, "panelRoot", panelRoot);
                Bind(so, "accountText", account); Bind(so, "statusText", status);
                Bind(so, "copyButton", copy); Bind(so, "emailButton", email);
                Bind(so, "deleteButton", delete); Bind(so, "resumeButton", resume); Bind(so, "closeButton", close);
                Bind(so, "confirmRoot", confirmRoot); Bind(so, "confirmText", confirmText);
                Bind(so, "confirmButtonText", confirmLabel); Bind(so, "confirmButton", confirm); Bind(so, "cancelButton", cancel);
                so.ApplyModifiedPropertiesWithoutUndo();
                var viewSo = new SerializedObject(view); Bind(viewSo, "bindings", bindings); viewSo.ApplyModifiedPropertiesWithoutUndo();
                panelRoot.SetActive(false); confirmRoot.SetActive(false);
                if (!bindings.HasRequiredReferences) throw new InvalidOperationException("Privacy binding migration failed.");
                PrefabUtility.SaveAsPrefabAsset(root, GameMenuProjectSetup.CanvasPrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static void Bind(SerializedObject so, string name, UnityEngine.Object value)
        { so.FindProperty(name).objectReferenceValue = value; }
        private static GameObject Rect(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go;
        }
        private static GameObject Blocker(Transform parent, string name)
        {
            var go = Rect(parent, name); var rect = (RectTransform)go.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero;
            go.AddComponent<Image>().color = new Color(0, 0, 0, .75f);
            return go;
        }
        private static RectTransform Panel(Transform parent, string name, Vector2 size)
        {
            var go = Rect(parent, name); var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f); rect.sizeDelta = size;
            go.AddComponent<Image>().color = new Color(.025f, .04f, .075f, 1);
            return rect;
        }
        private static void Place(Transform target, float x, float y, float width, float height)
        {
            var rect = (RectTransform)target;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height);
        }
        private static Text Label(Transform parent, string name, string source, int size, float y, float height, bool localized)
        {
            var go = Rect(parent, name); var text = go.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.text = source; text.fontSize = size; text.color = Color.white;
            text.alignment = TextAnchor.MiddleLeft; text.raycastTarget = false;
            text.supportRichText = false; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            Place(go.transform, 24, y, ((RectTransform)parent).sizeDelta.x - 48, height);
            if (localized) go.AddComponent<LocalizedText>().Configure(source);
            return text;
        }
        private static Button Button(Transform parent, string name, string source, bool danger)
        {
            var go = Rect(parent, name);
            var image = go.AddComponent<Image>(); image.color = danger ? new Color(.55f, .12f, .15f, 1) : new Color(.08f, .24f, .36f, 1);
            var button = go.AddComponent<Button>(); button.targetGraphic = image;
            var text = Label(go.transform, "Label", source, 24, 0, 40, true);
            var rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(12, 6); rect.offsetMax = new Vector2(-12, -6);
            text.alignment = TextAnchor.MiddleCenter; text.resizeTextForBestFit = true; text.resizeTextMinSize = 20; text.resizeTextMaxSize = 24;
            return button;
        }
    }
}
