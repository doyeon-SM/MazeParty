using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    public static partial class BoardItemProjectSetup
    {
        public const string WallDescription = "Maze only. Aim at a two-way passage connected to your tile and click the blue preview to block it for 5 turns including this turn. Cannot seal off rooms or overlap players.";
        [MenuItem("MazeParty/Board/Install Wall")]
        public static void InstallWall()
        {
            EnsureWallAsset();
            AssetDatabase.SaveAssets();
        }

        private static void EnsureWallAsset()
        {
            EnsureWallSound();
            var world = EnsureWallWorld();
            var preview = EnsureWallPreview(world);
            const string heldPath = VisualFolder + "/Wall.prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(heldPath) == null &&
                !AssetDatabase.CopyAsset(VisualFolder + "/GhostPass.prefab", heldPath))
                throw new InvalidOperationException("Ghost Pass controller model is required.");
            const string path = DataFolder + "/Wall.asset";
            if (AssetDatabase.LoadAssetAtPath<BoardItemDefinition>(path) != null) return;
            var item = ScriptableObject.CreateInstance<BoardItemDefinition>();
            item.Id = PrototypeItemId.Wall; item.DisplayName = "Wall"; item.Description = WallDescription;
            item.RequiredMapId = BoardWallRules.MapId; item.Price = 8; item.Charges = 1; item.SpawnWeight = 1;
            item.UseTiming = BoardItemUseTiming.Immediate;
            item.HeldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(heldPath);
            item.WorldPrefab = world; item.PlacementPreviewPrefab = preview;
            item.Icon = RequireIcon(PrototypeItemId.Wall);
            AssetDatabase.CreateAsset(item, path);
        }

        private static GameObject EnsureWallWorld()
        {
            const string path = VisualFolder + "/WallWorld.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject("WallWorld");
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Ignore/Maze/Prefabs/Wall_3M.prefab");
                if (source == null) throw new InvalidOperationException("Maze wall model is required.");
                var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                model.transform.localPosition = Vector3.zero; model.transform.localRotation = Quaternion.identity;
                foreach (var collider in model.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                var bounds = WallModelBounds(model);
                model.transform.localScale = new Vector3(1f / bounds.size.x, BoardWallRules.Height / bounds.size.y,
                    BoardWallRules.Thickness / bounds.size.z);
                bounds = WallModelBounds(model);
                model.transform.localPosition += new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                var blocker = root.AddComponent<BoxCollider>();
                blocker.center = Vector3.up * BoardWallRules.Height * .5f;
                blocker.size = new Vector3(1f, BoardWallRules.Height, BoardWallRules.Thickness);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static Bounds WallModelBounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("Maze wall has no renderer.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static GameObject EnsureWallPreview(GameObject world)
        {
            const string path = VisualFolder + "/WallPreview.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            const string materialPath = "Assets/MazeParty/Materials/WallPreview.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) throw new InvalidOperationException("URP Unlit shader is required.");
                material = new Material(shader) { name = "WallPreview", renderQueue = 3000 };
                material.SetFloat("_Surface", 1f); material.SetFloat("_Blend", 0f);
                material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                material.SetFloat("_ZWrite", 0f); material.SetFloat("_Cull", 0f);
                material.SetColor("_BaseColor", new Color(.08f, .48f, 1f, .32f));
                material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                material.SetOverrideTag("RenderType", "Transparent");
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var root = (GameObject)PrefabUtility.InstantiatePrefab(world);
            try
            {
                root.name = "WallPreview";
                foreach (var collider in root.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++) materials[i] = material;
                    renderer.sharedMaterials = materials;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void EnsureWallSound()
        {
            const string path = "Assets/MazeParty/Audio/Cues/Item/item.use.wall.asset";
            var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
            if (cue == null)
            {
                if (!AssetDatabase.CopyAsset("Assets/MazeParty/Audio/Cues/Item/item.use.mine.asset", path))
                    throw new InvalidOperationException("Placement sound is required.");
                cue = AssetDatabase.LoadAssetAtPath<SoundCue>(path);
                var data = new SerializedObject(cue);
                data.FindProperty("key").stringValue = SoundKeys.ItemUse(PrototypeItemId.Wall);
                data.ApplyModifiedPropertiesWithoutUndo();
                cue.name = "item.use.wall"; EditorUtility.SetDirty(cue);
            }
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>("Assets/MazeParty/Audio/SoundLibrary.asset");
            var cues = new List<SoundCue>(library.Cues);
            if (cues.Contains(cue)) return;
            cues.Add(cue); library.SetCues(cues.ToArray()); EditorUtility.SetDirty(library);
        }
    }
}
