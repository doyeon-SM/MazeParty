using System;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static partial class BoardItemProjectSetup
    {
        public const string BombardmentDescription = "Select 3 ground positions on the map. After a 1s warning, strike every 0.5s: 80 damage within 3m, 50 within 7m, 20 within 10m. Consumed on start; cannot cancel. Incomplete targeting ends on death or timeout.";
        [MenuItem("MazeParty/Board/Install Bombardment")]
        public static void InstallBombardment()
        {
            EnsureBombardmentAsset();
            const string path = "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                EnsureBombardmentHint(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        private static void EnsureBombardmentAsset()
        {
            EnsureBombardmentSound();
            const string path = DataFolder + "/Bombardment.asset";
            if (AssetDatabase.LoadAssetAtPath<BoardItemDefinition>(path) != null) return;
            var grenade = AssetDatabase.LoadAssetAtPath<BoardItemDefinition>(DataFolder + "/Grenade.asset");
            var item = ScriptableObject.CreateInstance<BoardItemDefinition>();
            item.Id = PrototypeItemId.Bombardment;
            item.UseTiming = BoardItemUseTiming.TurnStart;
            item.DisplayName = "Bombardment";
            item.Description = BombardmentDescription;
            item.Price = 15;
            item.SpawnWeight = 1;
            item.Charges = 1;
            item.Damage = 50;
            item.BlastRadius = BoardBombardmentRules.Radius;
            item.HeldPrefab = EnsureBombardmentModel();
            item.WorldPrefab = item.HeldPrefab;
            item.Icon = RequireIcon(PrototypeItemId.Bombardment);
            item.ExplosionPrefab = grenade.ExplosionPrefab;
            item.ExplosionScaleMultiplier = grenade.ExplosionScaleMultiplier;
            AssetDatabase.CreateAsset(item, path);
        }


        private static void EnsureBombardmentSound()
        {
            const string path = "Assets/MazeParty/Audio/Cues/Item/item.use.bombardment.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
            if (cue == null)
            {
                if (!AssetDatabase.CopyAsset("Assets/MazeParty/Audio/Cues/Item/item.use.position_swapper.asset", path))
                    throw new InvalidOperationException("The switch activation sound is missing.");
                cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
                var data = new SerializedObject(cue);
                data.FindProperty("key").stringValue = SoundKeys.ItemUse(PrototypeItemId.Bombardment);
                data.ApplyModifiedPropertiesWithoutUndo();
                cue.name = "item.use.bombardment";
                EditorUtility.SetDirty(cue);
            }
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>("Assets/MazeParty/Audio/SoundLibrary.asset");
            var cues = new System.Collections.Generic.List<SoundCue>(library.Cues);
            if (cues.Contains(cue)) return;
            cues.Add(cue);
            library.SetCues(cues.ToArray());
            EditorUtility.SetDirty(library);
        }

        private static GameObject EnsureBombardmentModel()
        {
            const string path = VisualFolder + "/Bombardment.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var source = ItemVisualFeedbackProjectSetup.EnsurePositionSwapper();
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
                throw new InvalidOperationException("Could not copy the switch prefab.");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = "Bombardment";
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name != "switch_003" && renderer.name != "switch_004") continue;
                    var red = renderer.name == "switch_003";
                    var materialPath = VisualFolder + (red ? "/Bombardment Red.mat" : "/Bombardment Orange.mat");
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        material = new Material(renderer.sharedMaterial);
                        material.name = red ? "Bombardment Red" : "Bombardment Orange";
                        material.SetTexture("_BaseMap", null);
                        material.SetTexture("_MainTex", null);
                        var color = red ? new Color(1f, .06f, .025f) : new Color(1f, .36f, .015f);
                        material.SetColor("_BaseColor", color);
                        material.SetColor("_Color", color);
                        AssetDatabase.CreateAsset(material, materialPath);
                    }
                    renderer.sharedMaterial = material;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static void EnsureBombardmentHint(GameObject root)
        {
            var bindings = root.GetComponent<BoardMapCanvasBindings>();
            var data = new SerializedObject(bindings);
            if (data.FindProperty("bombardmentHintRoot").objectReferenceValue != null) return;
            var view = root.GetComponent<BoardMapView>();
            var mapData = new SerializedObject(view);
            var panel = (GameObject)mapData.FindProperty("fullMapPanel").objectReferenceValue;
            var itemRoot = AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardItemCanvas.prefab");
            var itemData = new SerializedObject(itemRoot.GetComponent<BoardItemCanvasBindings>());
            var template = (GameObject)itemData.FindProperty("references.UseHintRoot").objectReferenceValue;
            var hint = UnityEngine.Object.Instantiate(template, panel.transform);
            hint.name = "Bombardment Hint";
            var rect = (RectTransform)hint.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0f);
            rect.pivot = new Vector2(.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, -104f);
            rect.sizeDelta = new Vector2(470f, 80f);
            var backdrop = hint.AddComponent<Image>();
            backdrop.color = new Color(.025f, .035f, .05f, .94f);
            backdrop.raycastTarget = false;
            var icon = hint.GetComponentInChildren<RawImage>(true);
            icon.rectTransform.anchoredPosition = new Vector2(-180f, 0f);
            var text = hint.GetComponentInChildren<Text>(true);
            text.rectTransform.anchoredPosition = new Vector2(-20f, 0f);
            text.rectTransform.sizeDelta = new Vector2(240f, 80f);
            text.text = "Set Bombardment";
            text.fontSize = 32;
            var progress = UnityEngine.Object.Instantiate(text, hint.transform);
            progress.name = "Target Count";
            progress.text = "0/3";
            progress.rectTransform.anchoredPosition = new Vector2(175f, 0f);
            progress.rectTransform.sizeDelta = new Vector2(100f, 80f);
            data.FindProperty("bombardmentHintRoot").objectReferenceValue = hint;
            data.FindProperty("bombardmentHintIcon").objectReferenceValue = icon;
            data.FindProperty("bombardmentHintText").objectReferenceValue = text;
            data.FindProperty("bombardmentProgressText").objectReferenceValue = progress;
            data.ApplyModifiedPropertiesWithoutUndo();
            hint.SetActive(false);
        }
    }
}
