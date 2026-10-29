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
        public const string GhostPassDescription = "Forest only. This turn's ghost steals from a random eligible opponent and delivers the stolen resource to the first user. Later uses are consumed without changing the recipient. Stolen health heals living users only. Available from turn 2.";
        [MenuItem("MazeParty/Board/Install Ghost Pass")]
        public static void InstallGhostPass()
        {
            EnsureGhostPassAsset();
            const string path = "Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (EnsureGhostPassOutlines(root)) PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            AssetDatabase.SaveAssets();
        }

        private static void EnsureGhostPassAsset()
        {
            EnsureGhostPassSound();
            const string path = DataFolder + "/GhostPass.asset";
            if (AssetDatabase.LoadAssetAtPath<BoardItemDefinition>(path) != null) return;
            var item = ScriptableObject.CreateInstance<BoardItemDefinition>();
            item.Id = PrototypeItemId.GhostPass;
            item.UseTiming = BoardItemUseTiming.Immediate;
            item.RequiredMapId = BoardForestGhostRules.MapId;
            item.DisplayName = "Ghost Pass";
            item.Description = GhostPassDescription;
            item.Price = 12;
            item.SpawnWeight = 1;
            item.Charges = 1;
            item.HeldPrefab = EnsureGhostPassModel();
            item.WorldPrefab = item.HeldPrefab;
            item.Icon = RequireIcon(PrototypeItemId.GhostPass);
            AssetDatabase.CreateAsset(item, path);
        }

        private static void EnsureGhostPassSound()
        {
            const string path = "Assets/MazeParty/Audio/Cues/Item/item.use.ghost_pass.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
            if (cue == null)
            {
                if (!AssetDatabase.CopyAsset("Assets/MazeParty/Audio/Cues/Item/item.use.position_swapper.asset", path))
                    throw new InvalidOperationException("The switch activation sound is missing.");
                cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
                var data = new SerializedObject(cue);
                data.FindProperty("key").stringValue = SoundKeys.ItemUse(PrototypeItemId.GhostPass);
                data.ApplyModifiedPropertiesWithoutUndo();
                cue.name = "item.use.ghost_pass";
                EditorUtility.SetDirty(cue);
            }
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>("Assets/MazeParty/Audio/SoundLibrary.asset");
            var cues = new System.Collections.Generic.List<SoundCue>(library.Cues);
            if (cues.Contains(cue)) return;
            cues.Add(cue);
            library.SetCues(cues.ToArray());
            EditorUtility.SetDirty(library);
        }

        private static GameObject EnsureGhostPassModel()
        {
            const string path = VisualFolder + "/GhostPass.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var source = ItemVisualFeedbackProjectSetup.EnsurePositionSwapper();
            if (!AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
                throw new InvalidOperationException("Could not copy the switch prefab.");
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                root.name = "GhostPass";
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer.name != "switch_003" && renderer.name != "switch_004") continue;
                    var blue = renderer.name == "switch_003";
                    var materialPath = VisualFolder + (blue ? "/GhostPass Blue.mat" : "/GhostPass Sky.mat");
                    var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                    if (material == null)
                    {
                        material = new Material(renderer.sharedMaterial);
                        material.name = blue ? "GhostPass Blue" : "GhostPass Sky";
                        material.SetTexture("_BaseMap", null);
                        material.SetTexture("_MainTex", null);
                        var color = blue ? new Color(.025f, .22f, 1f) : new Color(.2f, .78f, 1f);
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

        private static bool EnsureGhostPassOutlines(GameObject root)
        {
            var bindings = root.GetComponent<BoardHudCanvasBindings>();
            if (bindings.HasGhostPassReferences) return false;
            var data = new SerializedObject(bindings);
            var sliders = data.FindProperty("references.PlayerHealthSliders");
            var outlines = data.FindProperty("ghostPassOutlines");
            outlines.arraySize = MultiplayerConstants.MaxPlayers;
            for (var slot = 0; slot < outlines.arraySize; slot++)
            {
                if (outlines.GetArrayElementAtIndex(slot).objectReferenceValue != null) continue;
                var slider = (Slider)sliders.GetArrayElementAtIndex(slot).objectReferenceValue;
                var outline = slider.gameObject.AddComponent<Outline>();
                outline.effectColor = new Color(.68f, .2f, 1f, 1f);
                outline.effectDistance = new Vector2(4f, -4f);
                outline.useGraphicAlpha = true;
                outline.enabled = false;
                outlines.GetArrayElementAtIndex(slot).objectReferenceValue = outline;
            }
            data.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }
    }
}
