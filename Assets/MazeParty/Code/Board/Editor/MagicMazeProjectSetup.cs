using System;
using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    public static class MagicMazeProjectSetup
    {
        public const string MapFolder = "Assets/MazeParty/Resources/MazeParty/Board/Maps/";
        public const string DefinitionPath = MapFolder + "MagicMazeMap.asset";
        public const string PrefabPath = MapFolder + "MagicMazeMapRoot.prefab";
        public const string CloudPath = "Assets/MazeParty/Prefabs/Board/UI/MagicMazeCloudCanvas.prefab";
        public const string BoardScenePath = "Assets/MazeParty/Scenes/Board/Board.unity";

        [MenuItem("MazeParty/Board/Set Up Magic Maze")]
        public static void Setup()
        {
            var definition = AssetDatabase.LoadAssetAtPath<BoardMapDefinition>(DefinitionPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<BoardMapDefinition>();
                AssetDatabase.CreateAsset(definition, DefinitionPath);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null) prefab = CreateMap(definition);
            definition.Configure(MagicMazeRules.MapId, "Magic Maze", 1, prefab);
            var original = AssetDatabase.LoadAssetAtPath<BoardMapDefinition>(MapFolder + "MazeGrayboxMap.asset");
            var definitionSo = new SerializedObject(definition);
            definitionSo.FindProperty("outerRingTravelGuideOnly").boolValue = original.OuterRingTravelGuideOnly;
            definitionSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(definition);
            var catalog = AssetDatabase.LoadAssetAtPath<BoardMapCatalog>(MapFolder + "BoardMapCatalog.asset");
            if (!catalog.Maps.Contains(definition))
            { catalog.Configure(catalog.Maps.Concat(new[]{definition}).ToArray()); EditorUtility.SetDirty(catalog); }
            EnsureCloud();
            BindScene();
            AssetDatabase.SaveAssets();
            Debug.Log("Magic Maze map, purple cloud prefab and Board scene bindings are ready.");
        }

        private static GameObject CreateMap(BoardMapDefinition definition)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(MapFolder + "MazeGrayboxMapRoot.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                instance.name = "MagicMazeMapRoot";
                var rootSo = new SerializedObject(instance.GetComponent<BoardMapRoot>());
                rootSo.FindProperty("definition").objectReferenceValue = definition;
                rootSo.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.RecordPrefabInstancePropertyModifications(instance.GetComponent<BoardMapRoot>());
                var allBoxes = instance.transform.Find("Wall Colliders").GetComponentsInChildren<BoxCollider>(true);
                var groups = allBoxes.Where(c => !IsOuter(c.name)).GroupBy(c => GroupKey(c.name))
                    .OrderBy(g => g.Key, StringComparer.Ordinal).ToArray();
                var renderers = instance.transform.Find("Wall").GetComponentsInChildren<Renderer>(true);
                var extraColliders = instance.transform.Find("Wall").GetComponentsInChildren<Collider>(true);
                var material = EnsureOutlineMaterial();
                var walls = new List<MagicMazeWall>();
                foreach (var group in groups)
                {
                    var boxes = group.ToArray();
                    var solids = renderers.Where(r => GroupKey(ClosestBox(allBoxes, r.bounds).name) == group.Key).ToArray();
                    if (solids.Length == 0) throw new InvalidOperationException("Wall has no visual binding: " + group.Key);
                    var colliders = boxes.Cast<Collider>().Concat(extraColliders.Where(c =>
                        GroupKey(ClosestBox(allBoxes, c.bounds).name) == group.Key)).ToArray();
                    var holder = new GameObject("Magic " + group.Key);
                    holder.transform.SetParent(instance.transform, false);
                    var outline = new GameObject("Purple Outline"); outline.transform.SetParent(holder.transform, false);
                    foreach (var box in colliders.OfType<BoxCollider>()) CreateOutline(outline.transform, box, material);
                    var wall = holder.AddComponent<MagicMazeWall>();
                    wall.Configure(colliders, solids, outline); walls.Add(wall);
                }
                instance.AddComponent<MagicMazeMap>().Configure(walls.ToArray());
                return PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath);
            }
            finally { UnityEngine.Object.DestroyImmediate(instance); }
        }
        private static bool IsOuter(string name) => name.StartsWith("WallCollider_H00_") ||
            name.StartsWith("WallCollider_H08_") || name.StartsWith("WallCollider_V00_") || name.StartsWith("WallCollider_V08_");
        private static string GroupKey(string name)
        {
            var side = name.IndexOf("_Side", StringComparison.Ordinal);
            return side >= 0 ? name.Substring(0, side) : name;
        }
        private static BoxCollider ClosestBox(BoxCollider[] boxes, Bounds visual)
        {
            BoxCollider best = null; var bestScore = float.MaxValue;
            foreach (var box in boxes)
            {
                // Evaluate authored boxes geometrically; prefab assets can have empty physics bounds.
                var point = box.transform.InverseTransformPoint(visual.center) - box.center;
                var half = box.size * .5f;
                var dx = Mathf.Max(0f, Mathf.Abs(point.x) - half.x);
                var dz = Mathf.Max(0f, Mathf.Abs(point.z) - half.z);
                var score = dx * dx + dz * dz;
                var visualLongX = visual.size.x > visual.size.z;
                if (Mathf.Max(visual.size.x, visual.size.z) > 1f && visualLongX != (box.size.x > box.size.z)) score += 10f;
                if (score < bestScore) { best = box; bestScore = score; }
            }
            return best;
        }
        private static Material EnsureOutlineMaterial()
        {
            const string path = "Assets/MazeParty/Materials/MagicMazeOutline.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader missing.");
            material = new Material(shader) {name = "MagicMazeOutline"};
            material.SetColor("_BaseColor", new Color(.4f, .05f, .95f, 1f));
            AssetDatabase.CreateAsset(material, path); return material;
        }
        private static void CreateOutline(Transform parent, BoxCollider box, Material material)
        {
            var go = new GameObject(box.name + " Outline"); go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>(); line.useWorldSpace = false; line.sharedMaterial = material;
            line.widthMultiplier = .14f; line.numCornerVertices = 4; line.numCapVertices = 4;
            line.shadowCastingMode = ShadowCastingMode.Off; line.receiveShadows = false;
            var corners = new Vector3[8]; var h = box.size * .5f;
            for (var i=0;i<8;i++)
            {
                var x = (i == 1 || i == 2 || i == 5 || i == 6) ? h.x : -h.x;
                var z = (i == 2 || i == 3 || i == 6 || i == 7) ? h.z : -h.z;
                corners[i] = go.transform.InverseTransformPoint(box.transform.TransformPoint(box.center + new Vector3(x, i<4 ? -h.y+.04f : h.y, z)));
            }
            var order = new[]{0,1,2,3,0,4,5,1,5,6,2,6,7,3,7,4};
            line.positionCount = order.Length; line.SetPositions(order.Select(i => corners[i]).ToArray());
        }
        private static void EnsureCloud()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(CloudPath) != null) return;
            if (!AssetDatabase.CopyAsset("Assets/MazeParty/Prefabs/Board/UI/BoardCameraCloudCanvas.prefab", CloudPath))
                throw new InvalidOperationException("Could not copy the authored cloud prefab.");
            var instance = PrefabUtility.LoadPrefabContents(CloudPath);
            try
            {
                instance.name = "MagicMazeCloudCanvas";
                foreach (var image in instance.GetComponentsInChildren<Image>(true))
                    image.color = new Color(.62f, .28f, .84f, image.color.a);
                PrefabUtility.SaveAsPrefabAsset(instance, CloudPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(instance); }
        }
        private static void BindScene()
        {
            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(BoardScenePath, OpenSceneMode.Additive);
            try
            {
                var presenter = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BoardFlowCameraPresenter>(true)).Single();
                var so = new SerializedObject(presenter);
                so.FindProperty("magicMazeCloudPrefab").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(CloudPath).GetComponent<BoardCameraCloudView>();
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }
    }
}
