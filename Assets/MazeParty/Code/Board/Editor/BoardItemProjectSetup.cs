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
        public const string MineWorldIconPrefabPath =
            VisualFolder + "/MineWorldIcon.prefab";
        private const string IconFolder = "Assets/Ignore/AIImage/Icons";
        private const string HelpFilledIconPath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Help Filled.png";
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
                var diePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    BoardFlowProjectSetup.NetworkWorldDiePrefabPath);
                foreach (var first in dice.Where(d => d.DieIndex == 0).ToArray())
                {
                    if (dice.Any(d => d.ConfiguredSlot == first.ConfiguredSlot && d.DieIndex == 1)) continue;
                    if (diePrefab == null)
                    {
                        throw new InvalidOperationException(
                            "NetworkWorldDie.prefab must be created by the board " +
                            "flow setup before upgrading legacy dice.");
                    }
                    var secondObject = PrefabUtility.InstantiatePrefab(
                        diePrefab,
                        first.transform.parent) as GameObject;
                    if (secondObject == null)
                    {
                        throw new InvalidOperationException(
                            "Could not instantiate NetworkWorldDie.prefab.");
                    }
                    var second = secondObject.GetComponent<NetworkWorldDie>();
                    SceneManager.MoveGameObjectToScene(second.gameObject, scene);
                    second.name = first.name + " Second";
                    second.transform.SetPositionAndRotation(
                        first.transform.position,
                        first.transform.rotation);
                    second.ConfigureSceneIdentity(first.ConfiguredSlot, 1);
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
            EnsureReticleBinding(canvas);
            EnsureUtilityUi(canvas);
            var helpIcon = AssetDatabase.LoadAssetAtPath<Sprite>(
                HelpFilledIconPath);
            if (helpIcon == null)
            {
                throw new InvalidOperationException(
                    "ModernUIPack Help Filled icon must be imported at '" +
                    HelpFilledIconPath + "'.");
            }
            foreach (var map in canvas.GetComponentsInChildren<BoardMinimapView>(true))
            {
                var data = new SerializedObject(map);
                var graphic = data.FindProperty("mineGraphic")
                    .objectReferenceValue as BoardMapMineGraphic;
                if (graphic == null)
                {
                    var route = data.FindProperty("shopRouteGraphic")
                        .objectReferenceValue as BoardMapRouteGraphic;
                    if (route == null) continue;
                    var go = new GameObject("Own Mines", typeof(RectTransform), typeof(CanvasRenderer), typeof(BoardMapMineGraphic));
                    go.layer = route.gameObject.layer;
                    go.transform.SetParent(route.transform.parent, false);
                    var rect = (RectTransform)go.transform;
                    rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
                    rect.offsetMin = rect.offsetMax = Vector2.zero;
                    graphic = go.GetComponent<BoardMapMineGraphic>();
                }
                graphic.SetIcon(helpIcon);
                graphic.color = new Color(1f, .2f, .18f);
                graphic.raycastTarget = false;
                data.FindProperty("mineGraphic").objectReferenceValue = graphic;
                data.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void EnsureReticleBinding(GameObject canvas)
        {
            var bindings = canvas.GetComponent<BoardCanvasBindings>();
            if (bindings == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab requires BoardCanvasBindings before " +
                    "item presentation migration.");
            }

            var data = new SerializedObject(bindings);
            var references = data.FindProperty("references");
            var reticleText = references?.FindPropertyRelative("ReticleText");
            if (reticleText == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvasBindings is missing its ReticleText contract.");
            }
            if (reticleText.objectReferenceValue != null)
            {
                return;
            }

            var authoredReticle = canvas
                .GetComponentsInChildren<UnityEngine.UI.Text>(true)
                .SingleOrDefault(text =>
                    text.gameObject.name == "BoardReticle");
            if (authoredReticle == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab must author a Text named " +
                    "'BoardReticle'.");
            }

            reticleText.objectReferenceValue = authoredReticle;
            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bindings);
        }

        public static void EnsureAssets()
        {
            Directory.CreateDirectory(DataFolder);
            Directory.CreateDirectory(VisualFolder);
            Directory.CreateDirectory("Assets/MazeParty/Resources/MazeParty/ItemViews");
            AssetDatabase.Refresh();
            var explosion = SharedVfxProjectSetup.EnsureCartoonExplosionPrefab();
            var impact = SharedVfxProjectSetup.EnsureHitSparkPrefab();
            EnsureSimple("Shot", PrimitiveType.Cube, new Color(1f, .85f, .2f), "Assets/MazeParty/Resources/MazeParty/ItemViews");
            Create(PrototypeItemId.DoubleDice, "Double Dice", "Roll two D12s with RMB. Move after both settle.", 8, 1, 0, 0, .25f, 0, 0, 0, null);
            Create(PrototypeItemId.Pistol, "Pistol", "7 rounds / 20 damage / 2 tiles. LMB fires through board barriers.", 10, 7, 20, 16, .25f, 0, 0, 0, null);
            Create(PrototypeItemId.Sniper, "Sniper", "5 rounds / 50 damage / 7 tiles. LMB fires.", 15, 5, 50, 56, 1, 0, 0, 0, null);
            Create(PrototypeItemId.Grenade, "Grenade", "Throw up to 2 tiles. 80 damage within 4m, including yourself.", 8, 1, 80, 16, .25f, 4, 0, 0, explosion);
            Create(PrototypeItemId.Mine, "Mine", "Place on ground within 4m. Arms after 1s; 2m trigger / 4m blast / 50 damage.", 6, 1, 50, 4, .25f, 4, 2, 1, explosion);
            Create(PrototypeItemId.LowDice, "Low Dice (1-6)", "One D12 rolls only 1-6, each equally likely. Applied automatically.", 5, 1, 0, 0, .25f, 0, 0, 0, null, 1, 6);
            Create(PrototypeItemId.HighDice, "High Dice (7-12)", "One D12 rolls only 7-12, each equally likely. Applied automatically.", 8, 1, 0, 0, .25f, 0, 0, 0, null, 7, 12);
            Create(PrototypeItemId.PositionSwapper, "Position Swapper", "LMB: select a player. Channel for 2s; damage interrupts. Swap positions without spending moves.", 12, 1, 0, 0, .25f, 0, 0, 0, null);
            Create(PrototypeItemId.Cloak, "Invisibility Cloak", "LMB: hidden from opponents and minimaps until action ends. Ends before combat or on death.", 10, 1, 0, 0, .25f, 0, 0, 0, null);
            SharedVfxProjectSetup.EnsureBoardItemBindings(
                explosion,
                impact);
            AssetDatabase.SaveAssets();
        }

        private static void Create(PrototypeItemId id, string title, string description, int price, int charges,
            int damage, float range, float interval, float blast, float trigger, float delay, GameObject explosion, int diceMinimum = 1, int diceMaximum = 12)
        {
            string path = DataFolder + "/" + id + ".asset";
            // Existing designer-authored balance and prefab edits always win.
            var existing =
                AssetDatabase.LoadAssetAtPath<BoardItemDefinition>(path);
            if (existing != null)
            {
                EnsureIcon(existing, id);
                EnsurePresentation(existing, id);
                return;
            }
            var item = ScriptableObject.CreateInstance<BoardItemDefinition>();
            item.Id = id; item.DisplayName = title; item.Description = description; item.Price = price;
            item.Charges = charges; item.Damage = damage; item.Range = range; item.FireInterval = interval;
            item.BlastRadius = blast; item.TriggerRadius = trigger; item.ArmingDelay = delay;
            item.DiceMinimum = diceMinimum; item.DiceMaximum = diceMaximum;
            item.HeldPrefab = RequiresHeldModel(id) ? EnsureModel(id) : null;
            item.WorldPrefab = id == PrototypeItemId.Mine
                ? EnsureMineWorldIconPrefab()
                : item.HeldPrefab;
            item.ExplosionPrefab = explosion;
            item.Icon = RequireIcon(id);
            AssetDatabase.CreateAsset(item, path);
        }

        private static bool RequiresHeldModel(PrototypeItemId id)
        {
            return id == PrototypeItemId.Pistol ||
                   id == PrototypeItemId.Sniper ||
                   id == PrototypeItemId.Grenade ||
                   id == PrototypeItemId.Mine;
        }

        private static void EnsureIcon(
            BoardItemDefinition item,
            PrototypeItemId id)
        {
            if (item.Icon != null)
            {
                return;
            }

            item.Icon = RequireIcon(id);
            EditorUtility.SetDirty(item);
        }

        private static void EnsurePresentation(
            BoardItemDefinition item,
            PrototypeItemId id)
        {
            if (id != PrototypeItemId.Mine)
                return;

            if (item.HeldPrefab == null)
                item.HeldPrefab = EnsureModel(id);
            var worldIcon = EnsureMineWorldIconPrefab();
            if (item.WorldPrefab == worldIcon)
                return;

            item.WorldPrefab = worldIcon;
            EditorUtility.SetDirty(item);
        }

        private static GameObject EnsureMineWorldIconPrefab()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(
                HelpFilledIconPath);
            if (icon == null)
            {
                throw new InvalidOperationException(
                    "ModernUIPack Help Filled icon must be imported at '" +
                    HelpFilledIconPath + "'.");
            }

            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(
                MineWorldIconPrefabPath);
            var root = existing != null
                ? PrefabUtility.LoadPrefabContents(MineWorldIconPrefabPath)
                : new GameObject("MineWorldIcon");
            try
            {
                root.name = "MineWorldIcon";
                var markerTransform = root.transform.Find("Help Filled");
                GameObject markerObject;
                if (markerTransform == null)
                {
                    markerObject = new GameObject(
                        "Help Filled",
                        typeof(SpriteRenderer),
                        typeof(BoardWorldMineIcon));
                    markerObject.transform.SetParent(root.transform, false);
                }
                else
                {
                    markerObject = markerTransform.gameObject;
                    if (markerObject.GetComponent<SpriteRenderer>() == null)
                        markerObject.AddComponent<SpriteRenderer>();
                    if (markerObject.GetComponent<BoardWorldMineIcon>() == null)
                        markerObject.AddComponent<BoardWorldMineIcon>();
                }

                markerObject.transform.localPosition =
                    new Vector3(0f, 0.42f, 0f);
                markerObject.transform.localRotation = Quaternion.identity;
                markerObject.transform.localScale = Vector3.one * 0.28f;
                var renderer = markerObject.GetComponent<SpriteRenderer>();
                renderer.sprite = icon;
                renderer.color = new Color(1f, 0.2f, 0.18f, 1f);
                renderer.sortingOrder = 20;
                renderer.shadowCastingMode =
                    UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                markerObject.GetComponent<BoardWorldMineIcon>()
                    .Configure(renderer);

                PrefabUtility.SaveAsPrefabAsset(
                    root,
                    MineWorldIconPrefabPath);
            }
            finally
            {
                if (existing != null)
                    PrefabUtility.UnloadPrefabContents(root);
                else
                    UnityEngine.Object.DestroyImmediate(root);
            }

            return AssetDatabase.LoadAssetAtPath<GameObject>(
                MineWorldIconPrefabPath);
        }

        private static Sprite RequireIcon(PrototypeItemId id)
        {
            var path = IconFolder + "/" + id + ".png";
            var icon = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (icon == null)
            {
                throw new InvalidOperationException(
                    "Board item icon '" + path +
                    "' must be imported as a Sprite before upgrading items.");
            }

            return icon;
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
            else if (id == PrototypeItemId.Grenade)
            {
                Part(root, "Body", PrimitiveType.Sphere, Vector3.zero, new Vector3(.25f, .32f, .25f), metal);
                Part(root, "Fuse", PrimitiveType.Cube, new Vector3(.04f, .17f, 0), new Vector3(.06f, .1f, .12f), accent);
            }
            else if (id == PrototypeItemId.Mine)
            {
                Part(root, "Disc", PrimitiveType.Cylinder, Vector3.zero, new Vector3(.42f, .035f, .42f), metal);
                Part(root, "Pressure Plate", PrimitiveType.Cylinder, new Vector3(0, .045f, 0), new Vector3(.26f, .025f, .26f), accent);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(root);
                throw new ArgumentOutOfRangeException(
                    nameof(id), id, "This item does not use a held model.");
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
