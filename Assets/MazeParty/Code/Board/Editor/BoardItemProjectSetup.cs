using System;
using System.IO;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static partial class BoardItemProjectSetup
    {
        public const string DataFolder = "Assets/MazeParty/Resources/MazeParty/Items";
        public const string VisualFolder = "Assets/MazeParty/Prefabs/Board/Items";
        [MenuItem("MazeParty/Board/Upgrade Board Items")]
        public static void Upgrade()
        {
            EnsureAssets();
            const string canvasPath = "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
            var canvas = PrefabUtility.LoadPrefabContents(canvasPath);
            try { EnsureMapBindings(canvas); PrefabUtility.SaveAsPrefabAsset(canvas, canvasPath); }
            finally { PrefabUtility.UnloadPrefabContents(canvas); }
            const string path = "Assets/MazeParty/Scenes/Board/Board.unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool loaded = scene.IsValid() && scene.isLoaded;
            if (!loaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                var dice = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkWorldDie>(true)).ToList();
                foreach (var first in dice.Where(d => d.DieIndex == 0).ToArray())
                {
                    if (dice.Any(d => d.ConfiguredSlot == first.ConfiguredSlot && d.DieIndex == 1)) continue;
                    var second = UnityEngine.Object.Instantiate(first.gameObject, first.transform.parent).GetComponent<NetworkWorldDie>();
                    SceneManager.MoveGameObjectToScene(second.gameObject, scene);
                    second.name = first.name + " Second";
                    var data = new SerializedObject(second);
                    data.FindProperty("dieIndex").intValue = 1;
                    data.ApplyModifiedPropertiesWithoutUndo();
                    dice.Add(second);
                }
                var coordinator = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<NetworkWorldDiceCoordinator>(true)).Single();
                var topology = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<BoardTopology>(true)).Single();
                coordinator.ConfigureSceneDice(dice.OrderBy(d => d.ConfiguredSlot).ThenBy(d => d.DieIndex).ToArray(), topology);
                EditorUtility.SetDirty(coordinator);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            finally { if (!loaded) EditorSceneManager.CloseScene(scene, true); }
            AssetDatabase.SaveAssets();
        }

        public static void EnsureMapBindings(GameObject canvas)
        {
            EnsureUtilityUi(canvas);
            foreach (var map in canvas.GetComponentsInChildren<BoardMinimapView>(true))
            {
                var data = new SerializedObject(map);
                if (data.FindProperty("mineGraphic").objectReferenceValue != null) continue;
                var route = data.FindProperty("shopRouteGraphic").objectReferenceValue as BoardMapRouteGraphic;
                if (route == null) continue;
                var go = new GameObject("Own Mines", typeof(RectTransform), typeof(CanvasRenderer), typeof(BoardMapMineGraphic));
                go.layer = route.gameObject.layer;
                go.transform.SetParent(route.transform.parent, false);
                var rect = (RectTransform)go.transform;
                rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                var graphic = go.GetComponent<BoardMapMineGraphic>();
                graphic.color = new Color(1f, .3f, .16f);
                graphic.raycastTarget = false;
                data.FindProperty("mineGraphic").objectReferenceValue = graphic;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        public static void EnsureAssets()
        {
            Directory.CreateDirectory(DataFolder);
            Directory.CreateDirectory(VisualFolder);
            Directory.CreateDirectory("Assets/MazeParty/Resources/MazeParty/ItemViews");
            AssetDatabase.Refresh();
            var explosion = EnsureSimple("Explosion", PrimitiveType.Sphere, new Color(1f, .45f, .08f), VisualFolder);
            EnsureSimple("Shot", PrimitiveType.Cube, new Color(1f, .85f, .2f), "Assets/MazeParty/Resources/MazeParty/ItemViews");
            Create(PrototypeItemId.DoubleDice, "Double Dice", "Roll two D12s with RMB. Move after both settle.", 8, 1, 0, 0, .25f, 1, 0, 0, 0, explosion);
            Create(PrototypeItemId.Pistol, "Pistol", "7 rounds / 20 damage / 2 tiles. LMB fires through board barriers.", 10, 7, 20, 16, .25f, 1, 0, 0, 0, explosion);
            Create(PrototypeItemId.Sniper, "Sniper", "5 rounds / 50 damage / 7 tiles. Hold RMB: 2x scope. LMB fires.", 15, 5, 50, 56, 1, 2, 0, 0, 0, explosion);
            Create(PrototypeItemId.Grenade, "Grenade", "Throw up to 2 tiles. 80 damage within 4m, including yourself.", 8, 1, 80, 16, .25f, 1, 4, 0, 0, explosion);
            Create(PrototypeItemId.Mine, "Mine", "Place on ground within 4m. Arms after 1s; 2m trigger / 4m blast / 50 damage.", 6, 1, 50, 4, .25f, 1, 4, 2, 1, explosion);
            Create(PrototypeItemId.LowDice, "Low Dice (1-6)", "One D12 rolls only 1-6, each equally likely. Applied automatically.", 5, 1, 0, 0, .25f, 1, 0, 0, 0, explosion, 1, 6);
            Create(PrototypeItemId.HighDice, "High Dice (7-12)", "One D12 rolls only 7-12, each equally likely. Applied automatically.", 8, 1, 0, 0, .25f, 1, 0, 0, 0, explosion, 7, 12);
            Create(PrototypeItemId.PositionSwapper, "Position Swapper", "LMB: select a player. Channel for 2s; damage interrupts. Swap positions without spending moves.", 12, 1, 0, 0, .25f, 1, 0, 0, 0, explosion);
            Create(PrototypeItemId.Cloak, "Invisibility Cloak", "LMB: hidden from opponents and minimaps until action ends. Ends before combat or on death.", 10, 1, 0, 0, .25f, 1, 0, 0, 0, explosion);
            AssetDatabase.SaveAssets();
        }

        private static void Create(PrototypeItemId id, string title, string description, int price, int charges,
            int damage, float range, float interval, float zoom, float blast, float trigger, float delay, GameObject explosion, int diceMinimum = 1, int diceMaximum = 12)
        {
            string path = DataFolder + "/" + id + ".asset";
            // Existing designer-authored balance and prefab edits always win.
            if (AssetDatabase.LoadAssetAtPath<BoardItemDefinition>(path) != null) return;
            var item = ScriptableObject.CreateInstance<BoardItemDefinition>();
            item.Id = id; item.DisplayName = title; item.Description = description; item.Price = price;
            item.Charges = charges; item.Damage = damage; item.Range = range; item.FireInterval = interval;
            item.AimMagnification = zoom; item.BlastRadius = blast; item.TriggerRadius = trigger; item.ArmingDelay = delay;
            item.DiceMinimum = diceMinimum; item.DiceMaximum = diceMaximum;
            item.HeldPrefab = EnsureModel(id);
            item.WorldPrefab = item.HeldPrefab;
            item.ExplosionPrefab = explosion;
            AssetDatabase.CreateAsset(item, path);
        }
        private static GameObject EnsureModel(PrototypeItemId id)
        {
            string path = VisualFolder + "/" + id + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject(id.ToString());
            var metal = Material("Item Metal", new Color(.14f, .2f, .25f));
            var accent = Material("Item Accent", new Color(1f, .63f, .12f));
            if (id == PrototypeItemId.Pistol || id == PrototypeItemId.Sniper)
            {
                bool sniper = id == PrototypeItemId.Sniper;
                Part(root, "Receiver", PrimitiveType.Cube, Vector3.zero, new Vector3(.16f, .15f, sniper ? .7f : .38f), metal);
                Part(root, "Grip", PrimitiveType.Cube, new Vector3(0, -.17f, -.1f), new Vector3(.13f, .25f, .14f), accent);
                Part(root, "Barrel", PrimitiveType.Cube, new Vector3(0, .02f, sniper ? .55f : .25f), new Vector3(.07f, .07f, sniper ? .5f : .16f), metal);
                if (sniper)
                {
                    Part(root, "Scope", PrimitiveType.Cube, new Vector3(0, .16f, .02f), new Vector3(.14f, .14f, .3f), accent);
                    Part(root, "Stock", PrimitiveType.Cube, new Vector3(0, -.04f, -.45f), new Vector3(.14f, .22f, .3f), metal);
                }
            }
            else if (id == PrototypeItemId.DoubleDice)
            {
                Part(root, "Die A", PrimitiveType.Cube, new Vector3(-.16f, 0, 0), Vector3.one * .25f, accent);
                Part(root, "Die B", PrimitiveType.Cube, new Vector3(.16f, 0, 0), Vector3.one * .25f, accent);
                for (int i = -1; i <= 1; i += 2) Part(root, "Pip", PrimitiveType.Sphere, new Vector3(i * .16f, .125f, 0), Vector3.one * .07f, metal);
            }
            else if (id == PrototypeItemId.LowDice || id == PrototypeItemId.HighDice)
            {
                var tint = Material(id.ToString(), id == PrototypeItemId.LowDice ? new Color(.25f, .8f, .4f) : new Color(.7f, .35f, 1f));
                Part(root, "Die", PrimitiveType.Cube, Vector3.zero, Vector3.one * .32f, tint);
                Part(root, "Pip", PrimitiveType.Sphere, new Vector3(0, .16f, 0), Vector3.one * .08f, metal);
            }
            else if (id == PrototypeItemId.PositionSwapper)
            {
                Part(root, "Device", PrimitiveType.Cube, Vector3.zero, new Vector3(.25f, .1f, .38f), metal);
                Part(root, "Display", PrimitiveType.Cube, new Vector3(0, .06f, 0), new Vector3(.19f, .02f, .25f), accent);
                Part(root, "Emitter", PrimitiveType.Cylinder, new Vector3(0, .11f, .15f), new Vector3(.09f, .09f, .09f), accent);
            }
            else if (id == PrototypeItemId.Cloak)
            {
                var cloth = Material("Cloak Cloth", new Color(.24f, .2f, .55f));
                Part(root, "Folded Cloth", PrimitiveType.Cube, Vector3.zero, new Vector3(.5f, .08f, .35f), cloth);
                Part(root, "Clasp", PrimitiveType.Sphere, new Vector3(0, .05f, .15f), Vector3.one * .12f, accent);
            }
            else if (id == PrototypeItemId.Grenade)
            {
                Part(root, "Body", PrimitiveType.Sphere, Vector3.zero, new Vector3(.25f, .32f, .25f), metal);
                Part(root, "Fuse", PrimitiveType.Cube, new Vector3(.04f, .17f, 0), new Vector3(.06f, .1f, .12f), accent);
            }
            else
            {
                Part(root, "Disc", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.42f, .035f, .42f), metal);
                Part(root, "Pressure Plate", PrimitiveType.Cylinder, new Vector3(0, .045f, 0), new Vector3(.26f, .025f, .26f), accent);
            }
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab;
        }
        private static GameObject EnsureSimple(string name, PrimitiveType primitive, Color color, string folder)
        {
            string path = folder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = new GameObject(name);
            Part(root, name + " Mesh", primitive, Vector3.zero, Vector3.one, Material(name, color));
            var result = PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return result;
        }
        private static Material Material(string name, Color color)
        {
            string path = VisualFolder + "/" + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        private static void Part(GameObject root, string name, PrimitiveType primitive, Vector3 position, Vector3 size, Material material)
        {
            var go = GameObject.CreatePrimitive(primitive);
            go.name = name; go.transform.SetParent(root.transform, false);
            go.transform.localPosition = position; go.transform.localScale = size;
            UnityEngine.Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = material;
        }
    }
}
