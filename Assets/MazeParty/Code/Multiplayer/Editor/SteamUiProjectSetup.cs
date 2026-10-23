using System;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Editor
{
    public static class SteamUiProjectSetup
    {
        private const string LobbyPath = "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";
        [MenuItem("MazeParty/Setup/Upgrade Steam Invite UI")]
        public static void Upgrade()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPath);
            try { EnsureLobby(root); PrefabUtility.SaveAsPrefabAsset(root, LobbyPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }
        public static void EnsureLobby(GameObject root)
        {
            var view = root.GetComponent<OnlineLobbyView>();
            if (view == null) throw new InvalidOperationException("Lobby view is missing.");
            var serialized = new SerializedObject(view);
            var binding = serialized.FindProperty("steamInviteButton");
            if (binding.objectReferenceValue != null) return; // Preserve authored design.
            var template = (Button)serialized.FindProperty("createButton").objectReferenceValue;
            if (template == null) throw new InvalidOperationException("Lobby button template is missing.");
            var go = new GameObject("Steam Friend Invite", typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
            rect.anchoredPosition = new Vector2(-24, -24);
            rect.sizeDelta = new Vector2(240, 52);
            var image = go.GetComponent<Image>();
            var source = template.GetComponent<Image>();
            image.sprite = source.sprite; image.type = source.type; image.color = source.color;
            var button = go.GetComponent<Button>();
            button.targetGraphic = image; button.colors = template.colors;
            var label = new GameObject("Label", typeof(RectTransform), typeof(Text), typeof(LocalizedText));
            var labelRect = label.GetComponent<RectTransform>();
            labelRect.SetParent(rect, false);
            labelRect.anchorMin = Vector2.zero; labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = new Vector2(8, 4); labelRect.offsetMax = new Vector2(-8, -4);
            var text = label.GetComponent<Text>();
            var sourceText = template.GetComponentInChildren<Text>(true);
            text.font = sourceText.font; text.fontSize = 22; text.fontStyle = FontStyle.Normal;
            text.color = sourceText.color; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            text.text = "Invite Steam Friends";
            label.GetComponent<LocalizedText>().Configure(text.text);
            go.SetActive(false);
            binding.objectReferenceValue = button;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
