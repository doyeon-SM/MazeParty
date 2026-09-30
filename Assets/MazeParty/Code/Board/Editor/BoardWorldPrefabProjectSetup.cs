using System;
using System.Linq;
using MazeParty.Gameplay;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace MazeParty.Editor
{
    public static class BoardWorldPrefabProjectSetup
    {
        public const string Folder = "Assets/MazeParty/Prefabs/Board/World";
        private const string CatalogPath = "Assets/MazeParty/Resources/MazeParty/Board/BoardWorldPrefabs.asset";
        private const string Materials = "Assets/MazeParty/Board/Materials/";
        private const string BoundaryWallPath = Folder + "/PlayerBoundaryWall.prefab";
        private const string ToolkitPrefabFolder =
            "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/";
        private const string BlueFirePath = ToolkitPrefabFolder + "Blue Fire.prefab";
        private const string RealFirePath = ToolkitPrefabFolder + "Real Fire.prefab";
        private const float BoundaryFlamePrefabScale = .65f;
        private const float PassableFlameDensity = .45f;
        private const float BlockedFlameDensity = 2f;
        private const float MaximumBoundaryParticleDiameter = .72f;
        private const float MaximumBoundaryParticleTravel = .65f;

        [MenuItem("MazeParty/Board/Install World Prefabs")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before installing board prefabs.");
            EnsureBoundaryFlameVfx();
            var assets = EnsureAssets();
            foreach (var path in new[] { "Assets/MazeParty/Scenes/Board/Board.unity", "Assets/MazeParty/Scenes/Board/Dev/BoardFlowTestbed.unity" })
            {
                var scene = SceneManager.GetSceneByPath(path);
                var opened = !scene.isLoaded;
                if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                try
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        foreach (var topology in root.GetComponentsInChildren<BoardTopology>(true))
                        {
                            var tiles = topology.GetComponentsInChildren<BoardTile>(true);
                            for (var index = 0; index < tiles.Length; index++)
                            {
                                var tile = tiles[index];
                                var coordinate = tile.Coordinate;
                                var type = tile.TileType;
                                var existing = tile.GetComponent<Renderer>();
                                var normalB = existing != null && existing.sharedMaterial != null && existing.sharedMaterial.name == "RoomNormalB";
                                var prefab = EnsureTile(type, normalB ? 1 : 0);
                                Convert(tile.gameObject, prefab);
                                tile.Configure(coordinate, type);
                                PrefabUtility.RecordPrefabInstancePropertyModifications(tile);
                                UpdateTileLabel(tile);
                            }
                            Bind(topology.GetComponent<KeyShopWorldMarker>(), assets);
                            Bind(topology.GetComponent<ItemShopWorldMarker>(), assets);
                            topology.RebuildIndex();
                        }
                        foreach (var walls in root.GetComponentsInChildren<PlayerBoardBoundaryWalls>(true)) Bind(walls, assets);
                        if (root.name == "Board Backdrop (No Gameplay Collision)") Convert(root, EnsureBackdrop());
                    }
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
            }
            const string playerPath = "Assets/MazeParty/Prefabs/Multiplayer/NetworkPlayer.prefab";
            var player = PrefabUtility.LoadPrefabContents(playerPath);
            try
            {
                var changed = false;
                foreach (var walls in player.GetComponentsInChildren<PlayerBoardBoundaryWalls>(true))
                    changed |= Bind(walls, assets);
                if (changed) PrefabUtility.SaveAsPrefabAsset(player, playerPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(player); }
            AssetDatabase.SaveAssets();
            Debug.Log("Board world prefabs installed. Existing prefab and material designs preserved.");
        }

        [MenuItem("MazeParty/Board/Install Boundary Flame VFX")]
        public static void InstallBoundaryFlameVfx()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException(
                    "Stop Play Mode before installing the boundary flame VFX.");

            EnsureBoundaryFlameVfx();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Boundary flame VFX installed. Existing authored flame design was preserved when already configured.");
        }

        private static void Convert(GameObject instance, GameObject prefab)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(instance)) return;
            var position = instance.transform.position;
            var rotation = instance.transform.rotation;
            var name = instance.name;
            PrefabUtility.ConvertToPrefabInstance(instance, prefab, new ConvertToPrefabInstanceSettings
            {
                objectMatchMode = ObjectMatchMode.ByHierarchy,
                componentsNotMatchedBecomesOverride = true,
                gameObjectsNotMatchedBecomesOverride = true,
                recordPropertyOverridesOfMatches = false,
                changeRootNameToAssetName = false
            }, InteractionMode.AutomatedAction);
            instance.name = name;
            instance.transform.SetPositionAndRotation(position, rotation);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        }

        internal static bool Bind(Component target, BoardWorldPrefabs assets)
        {
            if (target == null) return false;
            var so = new SerializedObject(target);
            var property = so.FindProperty("worldPrefabs");
            if (property.objectReferenceValue == null)
            {
                property.objectReferenceValue = assets;
                return so.ApplyModifiedPropertiesWithoutUndo();
            }
            return false;
        }

        internal static BoardWorldPrefabs EnsureAssets()
        {
            EnsureFolder(Folder);
            EnsureFolder("Assets/MazeParty/Resources/MazeParty/Board");
            var assets = AssetDatabase.LoadAssetAtPath<BoardWorldPrefabs>(CatalogPath);
            if (assets == null)
            {
                assets = ScriptableObject.CreateInstance<BoardWorldPrefabs>();
                AssetDatabase.CreateAsset(assets, CatalogPath);
            }
            var so = new SerializedObject(assets);
            SetMissing(so, "keyShop", EnsureShop(-1).GetComponent<BoardShopVisual>());
            var shops = so.FindProperty("itemShops");
            if (shops.arraySize != 2) shops.arraySize = 2;
            for (var index = 0; index < 2; index++)
                if (shops.GetArrayElementAtIndex(index).objectReferenceValue == null)
                    shops.GetArrayElementAtIndex(index).objectReferenceValue = EnsureShop(index).GetComponent<BoardShopVisual>();
            SetMissing(so, "boundaryWall", EnsureWall().GetComponent<BoardBoundaryWallVisual>());
            so.ApplyModifiedPropertiesWithoutUndo();
            if (!assets.HasRequiredReferences) throw new InvalidOperationException("Repair the BoardWorldPrefabs bindings before continuing.");
            return assets;
        }

        internal static BoardTile CreateTile(Transform parent, Vector2Int coordinate, BoardTileType type, int index, Vector3 position)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(EnsureTile(type, index), parent);
            instance.name = "Room " + coordinate.x + "," + coordinate.y + " - " + type;
            instance.transform.position = position;
            var tile = instance.GetComponent<BoardTile>();
            tile.Configure(coordinate, type);
            PrefabUtility.RecordPrefabInstancePropertyModifications(tile);
            UpdateTileLabel(tile);
            return tile;
        }

        private static void UpdateTileLabel(BoardTile tile)
        {
            var label = tile.transform.Find("Room Label")?.GetComponent<TextMesh>();
            if (label == null) return;
            label.text = tile.Coordinate.x + "," + tile.Coordinate.y + "\n" + tile.TileType.ToString().ToUpperInvariant();
            PrefabUtility.RecordPrefabInstancePropertyModifications(label);
        }

        private static GameObject EnsureTile(BoardTileType type, int index)
        {
            var suffix = type == BoardTileType.Normal ? (index % 2 == 0 ? "NormalA" : "NormalB") : type.ToString();
            return EnsurePrefab("Tile" + suffix, () =>
            {
                var root = Primitive("Tile" + suffix, PrimitiveType.Cube, null, Vector3.zero, new Vector3(7.72f,.2f,7.72f),
                    AssetDatabase.LoadAssetAtPath<Material>(Materials + "Room" + suffix + ".mat"));
                var tile = root.AddComponent<BoardTile>();
                tile.Configure(Vector2Int.zero, type);
                var so = new SerializedObject(tile);
                so.FindProperty("landingEffectRenderer").objectReferenceValue = root.GetComponent<Renderer>();
                so.ApplyModifiedPropertiesWithoutUndo();
                var label = Label(root.transform, "Room Label", new Vector3(0,.56f,0), type.ToString().ToUpperInvariant());
                label.transform.localRotation = Quaternion.Euler(90,0,0);
                label.transform.localScale = Vector3.one * .05f;
                label.fontSize = 30;
                label.characterSize = .35f;
                return root;
            });
        }

        internal static GameObject EnsureBackdrop()
        {
            return EnsurePrefab("BoardBackdrop", () =>
            {
                var root = Primitive("Board Backdrop (No Gameplay Collision)", PrimitiveType.Cube, null,
                    Vector3.zero, new Vector3(60,.4f,60), AssetDatabase.LoadAssetAtPath<Material>(Materials + "BoardBackdrop.mat"));
                Object.DestroyImmediate(root.GetComponent<Collider>());
                return root;
            });
        }

        private static GameObject EnsureShop(int index)
        {
            var key = index < 0;
            var name = key ? "KeyShop" : "ItemShop" + (index + 1);
            return EnsurePrefab(name, () =>
            {
                var root = new GameObject(name);
                var visualRoot = new GameObject("Visuals");
                visualRoot.transform.SetParent(root.transform, false);
                var material = EnsureMaterial(name, key ? new Color(1,.66f,.08f) : index == 0 ? new Color(.58f,.2f,.86f) : new Color(.18f,.72f,.82f));
                var body = Primitive("Body", PrimitiveType.Cube, visualRoot.transform,
                    new Vector3(0,key ? .9f : .85f,0), key ? new Vector3(1.35f,1.7f,1.35f) : new Vector3(1.4f,1.7f,1.4f), material);
                Object.DestroyImmediate(body.GetComponent<Collider>());
                var hit = new GameObject("Interaction Target");
                hit.transform.SetParent(root.transform, false);
                var collider = hit.AddComponent<BoxCollider>();
                collider.center = body.transform.localPosition;
                collider.size = body.transform.localScale;
                if (key) hit.AddComponent<KeyShopWorldTarget>();
                else hit.AddComponent<ItemShopWorldTarget>().Configure(index);
                var colliders = new System.Collections.Generic.List<Collider> { collider };
                if (key)
                {
                    var stand = Primitive("Base", PrimitiveType.Cylinder, visualRoot.transform, new Vector3(0,.12f,0), new Vector3(1.65f,.12f,1.65f), material);
                    var baseHit = new GameObject("Base Interaction Target");
                    baseHit.transform.SetParent(root.transform, false);
                    baseHit.transform.localPosition = stand.transform.localPosition;
                    baseHit.transform.localScale = stand.transform.localScale;
                    var baseCollider = baseHit.AddComponent<CapsuleCollider>();
                    EditorUtility.CopySerialized(stand.GetComponent<CapsuleCollider>(), baseCollider);
                    baseHit.AddComponent<KeyShopWorldTarget>();
                    colliders.Add(baseCollider);
                    Object.DestroyImmediate(stand.GetComponent<Collider>());
                }
                var label = Label(root.transform, "World Label", new Vector3(0,key ? 1.95f : 1.9f,0),
                    key ? "KEY SHOP\nRMB BUY  20 GOLD" : "ITEM SHOP " + (index + 1) + "\nRMB OPEN");
                var outline = TopViewHighlightUtility.CreateSquareOutline(root.transform, "Top View Highlight", key ? 1.12f : 1.05f, .1f, .04f);
                foreach (var renderer in outline.GetComponentsInChildren<Renderer>())
                {
                    renderer.SetPropertyBlock(null);
                    renderer.sharedMaterial = EnsureMaterial("ShopHighlight", Color.white);
                }
                outline.SetActive(false);
                var binding = root.AddComponent<BoardShopVisual>();
                var so = new SerializedObject(binding);
                so.FindProperty("label").objectReferenceValue = label;
                so.FindProperty("topViewHighlight").objectReferenceValue = outline;
                var targets = so.FindProperty("interactionColliders");
                targets.arraySize = colliders.Count;
                for (var i = 0; i < colliders.Count; i++) targets.GetArrayElementAtIndex(i).objectReferenceValue = colliders[i];
                so.ApplyModifiedPropertiesWithoutUndo();
                return root;
            });
        }

        private static GameObject EnsureWall()
        {
            return EnsurePrefab("PlayerBoundaryWall", () =>
            {
                var root = Primitive("Player Boundary Wall", PrimitiveType.Cube, null, Vector3.zero, Vector3.one, EnsureMaterial("BoundaryWall", Color.white));
                var renderer = root.GetComponent<Renderer>();
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                var binding = root.AddComponent<BoardBoundaryWallVisual>();
                var so = new SerializedObject(binding);
                so.FindProperty("blockingCollider").objectReferenceValue = root.GetComponent<BoxCollider>();
                var renderers = so.FindProperty("stateRenderers");
                renderers.arraySize = 1;
                renderers.GetArrayElementAtIndex(0).objectReferenceValue = renderer;
                so.ApplyModifiedPropertiesWithoutUndo();
                return root;
            });
        }

        private static void EnsureBoundaryFlameVfx()
        {
            EnsureWall();
            var contents = PrefabUtility.LoadPrefabContents(BoundaryWallPath);
            try
            {
                var binding = contents.GetComponent<BoardBoundaryWallVisual>();
                if (binding == null)
                    throw new InvalidOperationException(
                        BoundaryWallPath + " must contain BoardBoundaryWallVisual on its root.");

                var serializedBinding = new SerializedObject(binding);
                if (serializedBinding.FindProperty("flameWidthRoot").objectReferenceValue != null &&
                    serializedBinding.FindProperty("passableFlameRoot").objectReferenceValue != null &&
                    serializedBinding.FindProperty("blockedFlameRoot").objectReferenceValue != null)
                {
                    var changed = ApplyBoundaryFlameSettings(serializedBinding);
                    if (!binding.HasRequiredReferences)
                    {
                        throw new InvalidOperationException(
                            BoundaryWallPath +
                            " has authored flame roots but incomplete renderer or particle bindings. Repair it manually so setup does not overwrite the design.");
                    }
                    changed |= EnsureBoundaryFlameUrpCompatibility(contents);
                    changed |= EnsureBoundaryFlameEnvelope(binding);
                    if (changed)
                        PrefabUtility.SaveAsPrefabAsset(contents, BoundaryWallPath);
                    return;
                }

                if (contents.transform.childCount > 0)
                {
                    throw new InvalidOperationException(
                        BoundaryWallPath +
                        " has authored children but no complete flame bindings. Repair it manually so setup does not overwrite the design.");
                }

                var blueFire = AssetDatabase.LoadAssetAtPath<GameObject>(BlueFirePath);
                var redFire = AssetDatabase.LoadAssetAtPath<GameObject>(RealFirePath);
                if (blueFire == null || redFire == null)
                {
                    throw new InvalidOperationException(
                        "AllIn1VfxToolkit v2.32 is required at Assets/Ignore/AllIn1VfxToolkit before installing boundary VFX.");
                }

                var legacyRenderer = contents.GetComponent<MeshRenderer>();
                if (legacyRenderer != null)
                    Object.DestroyImmediate(legacyRenderer);
                var legacyFilter = contents.GetComponent<MeshFilter>();
                if (legacyFilter != null)
                    Object.DestroyImmediate(legacyFilter);

                var flameWidth = new GameObject("Flame Width").transform;
                flameWidth.SetParent(contents.transform, false);

                var passable = InstantiateNestedFlame(
                    blueFire,
                    flameWidth,
                    "Passable Blue Flame",
                    true);
                var blocked = InstantiateNestedFlame(
                    redFire,
                    flameWidth,
                    "Blocked Red Flame",
                    false);

                serializedBinding.Update();
                serializedBinding.FindProperty("blockingCollider").objectReferenceValue =
                    contents.GetComponent<BoxCollider>();
                serializedBinding.FindProperty("flameWidthRoot").objectReferenceValue =
                    flameWidth;
                serializedBinding.FindProperty("passableFlameRoot").objectReferenceValue =
                    passable;
                serializedBinding.FindProperty("blockedFlameRoot").objectReferenceValue =
                    blocked;
                serializedBinding.FindProperty("authoredFlameWidth").floatValue =
                    BoundaryFlamePrefabScale;
                serializedBinding.FindProperty("flameTopHeight").floatValue =
                    BoardBoundaryWallVisual.RequiredFlameTopHeight;
                serializedBinding.FindProperty("passableEmissionDensity").floatValue =
                    PassableFlameDensity;
                serializedBinding.FindProperty("blockedEmissionDensity").floatValue =
                    BlockedFlameDensity;
                SetObjectArray(
                    serializedBinding.FindProperty("stateRenderers"),
                    contents.GetComponentsInChildren<Renderer>(true));
                SetObjectArray(
                    serializedBinding.FindProperty("stateParticles"),
                    contents.GetComponentsInChildren<ParticleSystem>(true));
                serializedBinding.ApplyModifiedPropertiesWithoutUndo();
                EnsureBoundaryFlameEnvelope(binding);

                PrefabUtility.SaveAsPrefabAsset(contents, BoundaryWallPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static GameObject InstantiateNestedFlame(
            GameObject prefab,
            Transform parent,
            string instanceName,
            bool active)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (instance == null)
                throw new InvalidOperationException("Failed to instantiate " + prefab.name + ".");

            instance.name = instanceName;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            instance.SetActive(active);

            BakeNestedParticleScale(instance, BoundaryFlamePrefabScale);
            EnsureBoundaryFlameUrpCompatibility(instance);

            return instance;
        }

        private static bool ApplyBoundaryFlameSettings(
            SerializedObject serializedBinding)
        {
            serializedBinding.Update();
            var changed = false;
            changed |= SetFloat(
                serializedBinding.FindProperty("flameTopHeight"),
                BoardBoundaryWallVisual.RequiredFlameTopHeight);
            changed |= SetFloat(
                serializedBinding.FindProperty("passableEmissionDensity"),
                PassableFlameDensity);
            changed |= SetFloat(
                serializedBinding.FindProperty("blockedEmissionDensity"),
                BlockedFlameDensity);
            if (changed)
                serializedBinding.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        private static bool EnsureBoundaryFlameEnvelope(
            BoardBoundaryWallVisual binding)
        {
            var changed = false;
            var widthRoot = binding.FlameWidthRoot;
            if (widthRoot.localPosition != Vector3.zero)
            {
                widthRoot.localPosition = Vector3.zero;
                changed = true;
            }
            if (widthRoot.localRotation != Quaternion.identity)
            {
                widthRoot.localRotation = Quaternion.identity;
                changed = true;
            }
            if (widthRoot.localScale != Vector3.one)
            {
                widthRoot.localScale = Vector3.one;
                changed = true;
            }

            foreach (var stateRoot in new[]
                     {
                         binding.PassableFlameRoot,
                         binding.BlockedFlameRoot
                     })
            {
                if (stateRoot.transform.localPosition != Vector3.zero)
                {
                    stateRoot.transform.localPosition = Vector3.zero;
                    changed = true;
                }
                if (stateRoot.transform.localRotation != Quaternion.identity)
                {
                    stateRoot.transform.localRotation = Quaternion.identity;
                    changed = true;
                }

                foreach (var particle in
                         stateRoot.GetComponentsInChildren<ParticleSystem>(true))
                {
                    changed |= ClampParticleAuthoring(particle);
                }
            }

            return changed;
        }

        private static bool ClampParticleAuthoring(ParticleSystem particle)
        {
            var changed = false;
            var main = particle.main;
            if (main.startSize3D)
            {
                changed |= ClampCurveMultiplier(
                    main.startSizeX,
                    main.startSizeXMultiplier,
                    MaximumBoundaryParticleDiameter,
                    value => main.startSizeXMultiplier = value);
                changed |= ClampCurveMultiplier(
                    main.startSizeY,
                    main.startSizeYMultiplier,
                    MaximumBoundaryParticleDiameter,
                    value => main.startSizeYMultiplier = value);
                changed |= ClampCurveMultiplier(
                    main.startSizeZ,
                    main.startSizeZMultiplier,
                    MaximumBoundaryParticleDiameter,
                    value => main.startSizeZMultiplier = value);
            }
            else
            {
                changed |= ClampCurveMultiplier(
                    main.startSize,
                    main.startSizeMultiplier,
                    MaximumBoundaryParticleDiameter,
                    value => main.startSizeMultiplier = value);
            }

            var maximumLifetime = EstimateMaximum(main.startLifetime);
            if (maximumLifetime > .001f)
            {
                changed |= ClampCurveMultiplier(
                    main.startSpeed,
                    main.startSpeedMultiplier,
                    MaximumBoundaryParticleTravel / maximumLifetime,
                    value => main.startSpeedMultiplier = value);
            }

            return changed;
        }

        private static bool ClampCurveMultiplier(
            ParticleSystem.MinMaxCurve curve,
            float multiplier,
            float maximum,
            Action<float> assign)
        {
            var currentMaximum = EstimateMaximum(curve);
            if (currentMaximum <= maximum + .0001f)
                return false;

            assign(multiplier * maximum / currentMaximum);
            return true;
        }

        private static float EstimateMaximum(
            ParticleSystem.MinMaxCurve curve)
        {
            var maximum = 0f;
            const int samples = 24;
            for (var sample = 0; sample <= samples; sample++)
            {
                var time = sample / (float)samples;
                maximum = Mathf.Max(
                    maximum,
                    Mathf.Abs(curve.Evaluate(time, 0f)),
                    Mathf.Abs(curve.Evaluate(time, 1f)));
            }
            return maximum;
        }

        private static bool SetFloat(
            SerializedProperty property,
            float value)
        {
            if (Mathf.Approximately(property.floatValue, value))
                return false;

            property.floatValue = value;
            return true;
        }

        private static void BakeNestedParticleScale(GameObject root, float scale)
        {
            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child != root.transform)
                    child.localPosition *= scale;
            }

            foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                main.startSpeedMultiplier *= scale;
                if (main.startSize3D)
                {
                    main.startSizeXMultiplier *= scale;
                    main.startSizeYMultiplier *= scale;
                    main.startSizeZMultiplier *= scale;
                }
                else
                {
                    main.startSizeMultiplier *= scale;
                }

                var shape = particle.shape;
                if (shape.enabled)
                {
                    shape.position *= scale;
                    shape.scale *= scale;
                }
            }
        }

        private static bool EnsureBoundaryFlameUrpCompatibility(GameObject root)
        {
            var changed = false;
            foreach (var particle in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = particle.main;
                if (main.scalingMode == ParticleSystemScalingMode.Shape)
                    continue;

                main.scalingMode = ParticleSystemScalingMode.Shape;
                changed = true;
            }

            // The demo prefabs include legacy-pipeline glow and GrabPass distortion
            // layers. URP renders those as error planes and rejects the grab-texture
            // globals, so keep the compatible authored fire layers and disable only
            // incompatible renderers on our nested instances. Vendor assets remain
            // untouched under Assets/Ignore.
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!UsesUnsupportedRenderPipelineShader(renderer))
                    continue;

                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    changed = true;
                }
                if (renderer.gameObject.activeSelf)
                {
                    renderer.gameObject.SetActive(false);
                    changed = true;
                }
            }

            return changed;
        }

        private static bool UsesUnsupportedRenderPipelineShader(Renderer renderer)
        {
            if (renderer.name.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                renderer.name.IndexOf("GrabPass", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            foreach (var material in renderer.sharedMaterials)
            {
                if (material == null)
                    continue;

                if (material.name.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.name.IndexOf("GrabPass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    material.shader != null &&
                    (material.shader.name.IndexOf("BuiltIn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     material.shader.name.IndexOf("Distortion", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     material.shader.name.IndexOf("GrabPass", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    return true;
                }
            }

            return false;
        }

        private static void SetObjectArray<T>(
            SerializedProperty property,
            T[] values)
            where T : Object
        {
            property.arraySize = values.Length;
            for (var index = 0; index < values.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue =
                    values[index];
            }
        }

        private static TextMesh Label(Transform parent, string name, Vector3 position, string text)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localRotation = Quaternion.Euler(0,180,0);
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 48;
            label.characterSize = .09f;
            label.color = Color.white;
            // The existing scene hook installs depth-tested text on runtime instances.
            return label;
        }

        private static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        private static GameObject EnsurePrefab(string name, Func<GameObject> create)
        {
            EnsureFolder(Folder);
            var path = Folder + "/" + name + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = create();
            try { return PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { Object.DestroyImmediate(root); }
        }

        private static Material EnsureMaterial(string name, Color color)
        {
            var path = Materials + name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void SetMissing(SerializedObject so, string name, Object value)
        {
            var property = so.FindProperty(name);
            if (property.objectReferenceValue == null) property.objectReferenceValue = value;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var slash = path.LastIndexOf('/');
            EnsureFolder(path.Substring(0,slash));
            AssetDatabase.CreateFolder(path.Substring(0,slash), path.Substring(slash + 1));
        }
    }
}
