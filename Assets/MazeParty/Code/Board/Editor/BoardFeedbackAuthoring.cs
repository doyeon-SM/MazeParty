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

namespace MazeParty.Editor
{
    /// <summary>One-time migration; subsequent runs preserve authored designs.</summary>
    public static class BoardFeedbackAuthoring
    {
        private const string World = "Assets/MazeParty/Prefabs/Board/World";
        private const string Dice = "Assets/MazeParty/Prefabs/Board/Dice";
        private const string Maps = "Assets/MazeParty/Resources/MazeParty/Board/Maps/";
        private const string LampSource = "Assets/Ignore/SpaceZeta_StreetLamps2/Prefabs/StreetLampRound1A.prefab";

        [MenuItem("MazeParty/Gameplay/Apply Board Night and Dice Feedback")]
        public static void Apply()
        {
            UpgradeDie();
            AddMapLighting("Forest", EnsureForestLamp());
            AddMapLighting("Maze", EnsureMazeLight());
            var previous = SceneManager.GetActiveScene();
            const string path = "Assets/MazeParty/Scenes/Board/Board.unity";
            var scene = SceneManager.GetSceneByPath(path);
            var loaded = scene.IsValid() && scene.isLoaded;
            if (!loaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var light = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Light>(true))
                    .Single(value => value.type == LightType.Directional);
                if (light.name != "Board Moonlight")
                {
                    light.name = "Board Moonlight";
                    light.color = new Color(0.57f, 0.69f, 1f);
                    light.intensity = 0.28f;
                    light.shadows = LightShadows.Soft;
                    RenderSettings.sun = light;
                    ConfigureNightAmbient();
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (!loaded) EditorSceneManager.CloseScene(scene, true);
                if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            }
            AssetDatabase.SaveAssets();
        }

        public static void ConfigureNightAmbient()
        {
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.22f;
            RenderSettings.reflectionIntensity = 0.3f;
        }

        private static void UpgradeDie()
        {
            var path = Dice + "/NetworkWorldDie.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                EnsureDiceHint(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static void EnsureDiceHint(GameObject root)
        {
            var die = root.GetComponent<NetworkWorldDie>();
            var existing = root.GetComponentInChildren<WorldDieReadyHint>(true);
            if (existing != null)
            {
                if (!existing.HasRequiredReferences)
                    throw new InvalidOperationException("Repair the authored die hint bindings.");
                return;
            }
            var result = new SerializedObject(die).FindProperty("publicResultText")
                .objectReferenceValue as TextMesh;
            if (result == null) throw new InvalidOperationException("Missing die result text.");
            var holder = Child(result.transform, "Ready Mouse Instructions");
            // Narrow enough for the two separately positioned Double Dice labels.
            holder.transform.localScale = Vector3.one * 0.58f;
            var move = Text(holder.transform, "Move", new Vector3(-0.61f, 0f, 0f), TextAnchor.MiddleLeft);
            var use = Text(holder.transform, "Use", new Vector3(0.57f, 0f, 0f), TextAnchor.MiddleLeft);
            Text(holder.transform, "/", new Vector3(0.09f, 0f, 0f), TextAnchor.MiddleCenter);
            var left = MouseIcon(holder.transform, "Left Mouse", -0.87f, false);
            var right = MouseIcon(holder.transform, "Right Mouse", 0.35f, true);
            var hint = holder.AddComponent<WorldDieReadyHint>();
            hint.Configure(move, use, left, right);
            die.ConfigureReadyHint(hint);
        }

        private static TextMesh Text(Transform parent, string text, Vector3 position, TextAnchor anchor)
        {
            var go = Child(parent, text);
            go.transform.localPosition = position;
            var label = go.AddComponent<TextMesh>();
            label.text = text;
            label.fontSize = 64;
            label.characterSize = 0.038f;
            label.anchor = anchor;
            label.alignment = TextAlignment.Center;
            label.color = Color.white;
            go.AddComponent<WorldTextOcclusion>();
            GameFonts.Apply(label);
            return label;
        }

        private static Renderer MouseIcon(Transform parent, string name, float x, bool right)
        {
            var path = Dice + (right ? "/MouseRight.asset" : "/MouseLeft.asset");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                var vertices = new List<Vector3>();
                var triangles = new List<int>();
                var colors = new List<Color>();
                Action<Vector3, Vector3, Vector3, Color> triangle = (a, b, c, color) =>
                {
                    var start = vertices.Count;
                    vertices.Add(a); vertices.Add(b); vertices.Add(c);
                    colors.Add(color); colors.Add(color); colors.Add(color);
                    triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                };
                for (var i = 0; i < 48; i++)
                {
                    var a = i * Mathf.PI * 2f / 48;
                    var b = (i + 1) * Mathf.PI * 2f / 48;
                    var outerA = new Vector3(Mathf.Cos(a) * 0.145f, Mathf.Sin(a) * 0.205f, 0f);
                    var outerB = new Vector3(Mathf.Cos(b) * 0.145f, Mathf.Sin(b) * 0.205f, 0f);
                    var innerA = outerA * 0.83f;
                    var innerB = outerB * 0.83f;
                    triangle(outerA, outerB, innerA, Color.white);
                    triangle(innerA, outerB, innerB, Color.white);
                    var selected = Mathf.Sin(a) > 0 && (right ? Mathf.Cos(a) > 0 : Mathf.Cos(a) < 0);
                    triangle(Vector3.zero, innerA, innerB,
                        selected ? new Color(1f, 0.85f, 0.25f) : new Color(0.04f, 0.05f, 0.08f));
                }
                mesh = new Mesh { name = name };
                mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, path);
            }
            var materialPath = Dice + "/MouseIcon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Resources.Load<Shader>(WorldTextOcclusion.TextShaderResource));
                material.color = Color.white;
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var go = Child(parent, name);
            go.transform.localPosition = new Vector3(x, 0f, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static GameObject EnsureForestLamp()
        {
            var path = World + "/ForestStreetLamp.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) return prefab;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(LampSource);
            if (source == null) throw new InvalidOperationException("Missing " + LampSource);
            var root = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                root.name = "Forest Street Lamp";
                foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
                foreach (var light in root.GetComponentsInChildren<Light>(true))
                {
                    light.enabled = light.type == LightType.Point;
                    light.color = new Color(1f, 0.77f, 0.44f);
                    light.intensity = 3.5f;
                    light.range = 11f;
                    light.shadows = LightShadows.None;
                    light.lightmapBakeType = LightmapBakeType.Realtime;
                }
                var renderer = root.GetComponent<MeshRenderer>();
                var original = renderer.sharedMaterial;
                var materialPath = World + "/ForestStreetLamp.mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                    material.SetTexture("_BaseMap", original.GetTexture("_MainTex"));
                    material.SetColor("_BaseColor", Color.white);
                    material.SetFloat("_Metallic", 0.65f);
                    material.SetFloat("_Smoothness", 0.45f);
                    var emission = AssetDatabase.LoadAssetAtPath<Texture>(
                        "Assets/Ignore/SpaceZeta_StreetLamps2/Materials/T_StreetLampRound1A_emission.png");
                    material.SetTexture("_EmissionMap", emission);
                    material.SetColor("_EmissionColor", new Color(1f, 0.77f, 0.44f) * 2f);
                    material.EnableKeyword("_EMISSION");
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                renderer.sharedMaterial = material;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject EnsureMazeLight()
        {
            var path = World + "/MazePassageLight.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null) return prefab;
            var root = new GameObject("Maze Passage Light");
            try
            {
                var light = root.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(0.72f, 0.85f, 1f);
                light.intensity = 4f;
                light.range = 10f;
                light.shadows = LightShadows.None;
                light.lightmapBakeType = LightmapBakeType.Realtime;
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static void AddMapLighting(string map, GameObject lightPrefab)
        {
            var path = Maps + map + "GrayboxMapRoot.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var environment = root.GetComponent<BoardMapRoot>().EnvironmentRoot;
                if (environment.Find("Night Lighting") != null) return;
                var container = Child(environment, "Night Lighting");
                var tiles = root.GetComponentsInChildren<BoardTile>(true);
                Physics.SyncTransforms();
                var placed = new List<Vector3>();
                foreach (var tile in tiles)
                {
                    var center = tile.WorldCenter;
                    if (map == "Forest" && placed.Any(p => Vector3.Distance(p, center) < 7f)) continue;
                    var position = center + Vector3.up * 3.2f;
                    if (map == "Forest")
                    {
                        // Small path-side fixtures, away from tile recovery/spawn centers.
                        var offset = new Vector3(2.2f, 0f, 1.6f);
                        position = center + offset;
                        var terrain = root.GetComponentInChildren<Terrain>(true);
                        if (terrain != null)
                            position.y = terrain.SampleHeight(position) + terrain.transform.position.y;
                    }
                    var instance = (GameObject)PrefabUtility.InstantiatePrefab(lightPrefab, container.transform);
                    instance.name = map + " Light " + placed.Count.ToString("00");
                    instance.transform.position = position;
                    placed.Add(center);
                }
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static GameObject Child(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }
    }
}
