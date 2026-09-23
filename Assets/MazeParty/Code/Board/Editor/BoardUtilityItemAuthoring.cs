using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static partial class BoardItemProjectSetup
    {
        private static void EnsureUtilityUi(GameObject canvas)
        {
            var view = canvas.GetComponent<BoardUtilityItemView>();
            if (view != null)
            {
                if (!view.HasRequiredReferences) throw new System.InvalidOperationException("Utility item UI bindings are incomplete.");
                return;
            }
            view = canvas.AddComponent<BoardUtilityItemView>();
            var panel = Rect("Position Swap Targets", canvas.transform, new Vector2(440, 360), Vector2.zero);
            var background = panel.gameObject.AddComponent<Image>();
            background.color = new Color(.035f, .055f, .085f, .98f);
            Label("Title", panel, "SELECT POSITION SWAP TARGET", new Vector2(410, 36), new Vector2(0, 145), 20);
            var buttons = new Button[4]; var labels = new Text[4];
            for (int slot = 0; slot < 4; slot++)
            {
                var row = Rect("Target P" + (slot + 1), panel, new Vector2(390, 42), new Vector2(0, 91 - slot * 49));
                row.gameObject.AddComponent<Image>().color = new Color(.12f, .23f, .32f);
                buttons[slot] = row.gameObject.AddComponent<Button>();
                labels[slot] = Label("Name", row, "P" + (slot + 1), new Vector2(370, 38), Vector2.zero, 18);
            }
            var cancel = Rect("Cancel", panel, new Vector2(180, 38), new Vector2(0, -142));
            cancel.gameObject.AddComponent<Image>().color = new Color(.27f, .16f, .18f);
            var cancelButton = cancel.gameObject.AddComponent<Button>();
            Label("Label", cancel, "CANCEL / ESC", new Vector2(170, 34), Vector2.zero, 16);
            var status = Label("Utility Item Status", canvas.transform, "", new Vector2(620, 32), new Vector2(0, -110), 18);
            status.color = new Color(.4f, .9f, 1f);
            var notice = Label("Position Swap Notice", canvas.transform, "", new Vector2(660, 54), new Vector2(0, 110), 20);
            notice.color = new Color(1f, .85f, .3f);
            var data = new SerializedObject(view);
            data.FindProperty("targetPanel").objectReferenceValue = panel.gameObject;
            data.FindProperty("cancelButton").objectReferenceValue = cancelButton;
            data.FindProperty("statusText").objectReferenceValue = status;
            data.FindProperty("noticeText").objectReferenceValue = notice;
            var buttonData = data.FindProperty("targetButtons"); buttonData.arraySize = 4;
            var labelData = data.FindProperty("targetLabels"); labelData.arraySize = 4;
            for (int i = 0; i < 4; i++)
            { buttonData.GetArrayElementAtIndex(i).objectReferenceValue = buttons[i]; labelData.GetArrayElementAtIndex(i).objectReferenceValue = labels[i]; }
            data.ApplyModifiedPropertiesWithoutUndo();
            panel.gameObject.SetActive(false);
        }
        private static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 position)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");
            var rect = (RectTransform)go.transform; rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one * .5f;
            rect.sizeDelta = size; rect.anchoredPosition = position;
            return rect;
        }
        private static Text Label(string name, Transform parent, string text, Vector2 size, Vector2 position, int fontSize)
        {
            var label = Rect(name, parent, size, position).gameObject.AddComponent<Text>();
            label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.text = text; label.fontSize = fontSize; label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white; label.raycastTarget = false;
            return label;
        }
    }
}
