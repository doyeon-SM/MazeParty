using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    // Explicit authoring operation: updates game-owned assets, never vendor packages or UI.
    public static class ToonShaderProjectSetup
    {
        public const string ShaderName = "MazeParty/Toon Shader FX";
        public const string TerrainMaterialPath = "Assets/MazeParty/Materials/Toon/ToonTerrain.mat";
        private const string Converted = "Assets/MazeParty/Materials/Toon/Converted";
        private static readonly Dictionary<Material, Material> Replacements = new();
        private static readonly List<string> Changes = new();
        private static string BackupRoot => "Logs/ToonRollout-20261019/Before";

        private static bool Eligible(Material material) => material != null &&
            (material.shader.name == "Universal Render Pipeline/Lit" ||
             material.shader.name == "Universal Render Pipeline/Simple Lit" ||
             material.shader.name == "Shader Graphs/ImphenziaPixPal_URP" ||
             material.shader.name == "Shader Graphs/ORM_Shader");

        private static bool GamePath(string path) =>
            path.StartsWith("Assets/MazeParty/", StringComparison.Ordinal) && !path.Contains("/Dev/");

        private static void Backup(string path)
        {
            var target = Path.Combine(BackupRoot, path);
            if (File.Exists(path) && !File.Exists(target))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(path, target);
            }
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static string TextureProperty(Material material, params string[] names)
        {
            foreach (var name in names)
            {
                var index = material.shader.FindPropertyIndex(name);
                if (index >= 0 && material.shader.GetPropertyType(index) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    return name;
            }
            return null;
        }

        private static Color SourceColor(Material material)
        {
            foreach (var name in new[] { "_BaseColor", "_Color" })
            {
                var index = material.shader.FindPropertyIndex(name);
                if (index >= 0 && material.shader.GetPropertyType(index) == UnityEngine.Rendering.ShaderPropertyType.Color)
                    return material.GetColor(name);
            }
            return Color.white;
        }

        internal static Material Resolve(Material source)
        {
            if (!Eligible(source)) return source;
            if (Replacements.TryGetValue(source, out var resolved)) return resolved;
            var path = AssetDatabase.GetAssetPath(source);
            var owned = GamePath(path) && path.EndsWith(".mat", StringComparison.Ordinal);
            var targetPath = path;
            if (!owned)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId);
                var safeName = string.Concat(source.name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                targetPath = Converted + "/" + safeName + "_" + guid.Substring(0, 8) + "_" + localId + ".mat";
                resolved = AssetDatabase.LoadAssetAtPath<Material>(targetPath);
                if (resolved != null) { Replacements[source] = resolved; return resolved; }
            }

            // Capture source values before changing its shader. Use BaseMap/BaseColor
            // as canonical properties so paint textures and gameplay color blocks keep working.
            var textureName = TextureProperty(source, "_BaseMap", "_ColorTexture", "_MainTex", "_BaseColor");
            var texture = textureName != null ? source.GetTexture(textureName) : null;
            var scale = textureName != null ? source.GetTextureScale(textureName) : Vector2.one;
            var offset = textureName != null ? source.GetTextureOffset(textureName) : Vector2.zero;
            var color = SourceColor(source);
            // ORM graph drives alpha from a separate texture; its tint alpha is unused.
            if (source.shader.name == "Shader Graphs/ORM_Shader") color.a = 1f;
            var normalName = TextureProperty(source, "_BumpMap", "_NormalMapTexture");
            var normal = normalName != null ? source.GetTexture(normalName) : null;
            var normalScale = source.HasProperty("_BumpScale") ? source.GetFloat("_BumpScale") :
                source.HasProperty("_NormalStr") ? source.GetFloat("_NormalStr") : 1f;
            var alphaName = TextureProperty(source, "_AlphaTexture");
            var alpha = alphaName != null ? source.GetTexture(alphaName) : null;
            var alphaClip = source.HasProperty("_AlphaClip") ? source.GetFloat("_AlphaClip") : 0f;
            var cutoff = source.HasProperty("_Cutoff") ? source.GetFloat("_Cutoff") : 0.5f;
            var surface = source.HasProperty("_Surface") ? source.GetFloat("_Surface") : 0f;
            var cull = source.HasProperty("_Cull") ? source.GetFloat("_Cull") : 2f;
            var emission = source.HasProperty("_EmissionColor") ? source.GetColor("_EmissionColor") : Color.black;
            var emissionName = TextureProperty(source, "_EmissionMap", "_Emission");
            var emissionMap = emissionName != null ? source.GetTexture(emissionName) : null;
            if (source.HasProperty("_EmissionStrength"))
                emission = Color.white * source.GetFloat("_EmissionStrength");
            var queue = source.renderQueue;

            if (owned) { Backup(path); resolved = source; }
            else resolved = new Material(source) { name = source.name + " Toon" };
            resolved.shader = Shader.Find(ShaderName);
            resolved.shaderKeywords = Array.Empty<string>();
            resolved.SetTexture("_BaseMap", texture);
            resolved.SetTextureScale("_BaseMap", scale);
            resolved.SetTextureOffset("_BaseMap", offset);
            resolved.SetColor("_BaseColor", color);
            resolved.SetColor("_Color", color);
            resolved.SetTexture("_BumpMap", normal);
            resolved.SetFloat("_BumpScale", normalScale);
            if (normal != null) resolved.EnableKeyword("_NORMALMAP");
            resolved.SetTexture("_AlphaMap", alpha);
            resolved.SetFloat("_UseAlphaMap", alpha != null ? 1f : 0f);
            resolved.SetFloat("_AlphaClip", alphaClip);
            resolved.SetFloat("_Cutoff", cutoff);
            resolved.SetFloat("_Surface", surface);
            resolved.SetFloat("_Cull", cull);
            resolved.SetFloat("_SrcBlend", surface > 0.5f ? 5f : 1f);
            resolved.SetFloat("_DstBlend", surface > 0.5f ? 10f : 0f);
            resolved.SetFloat("_ZWrite", surface > 0.5f ? 0f : 1f);
            resolved.SetShaderPassEnabled("ShadowCaster", surface < 0.5f);
            resolved.SetShaderPassEnabled("DepthOnly", surface < 0.5f);
            resolved.SetShaderPassEnabled("DepthNormals", surface < 0.5f);
            resolved.SetOverrideTag("RenderType", surface > 0.5f ? "Transparent" : alphaClip > 0.5f ? "TransparentCutout" : "Opaque");
            resolved.SetColor("_EmissionColor", emission);
            resolved.SetTexture("_EmissionMap", emissionMap);
            // Do not inherit URP/Lit's hidden _Glossiness default (0) into the toon highlight.
            resolved.SetFloat("_Glossiness", 32f);
            resolved.SetFloat("_ToonEdge", 0.025f);
            resolved.SetColor("_AmbientColor", new Color(0.4f, 0.4f, 0.4f, 1f));
            resolved.SetColor("_SpecularColor", new Color(0.9f, 0.9f, 0.9f, 1f));
            resolved.SetColor("_RimColor", Color.white);
            resolved.SetFloat("_RimBlend", 0.5f);
            resolved.SetFloat("_RimThreshold", 0.1f);
            resolved.renderQueue = queue;
            if (!owned) AssetDatabase.CreateAsset(resolved, targetPath);
            EditorUtility.SetDirty(resolved);
            Replacements[source] = resolved;
            Changes.Add("Material: " + path + " -> " + targetPath);
            return resolved;
        }

        public static Material GetTerrainMaterial()
        {
            EnsureFolder("Assets/MazeParty/Materials/Toon");
            var material = AssetDatabase.LoadAssetAtPath<Material>(TerrainMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("MazeParty/Toon Terrain")) { name = "ToonTerrain" };
                material.EnableKeyword("_TERRAIN_INSTANCED_PERPIXEL_NORMAL");
                AssetDatabase.CreateAsset(material, TerrainMaterialPath);
            }
            return material;
        }

        public static string PrepareMaterials()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Stop Play Mode first.");
            if (Shader.Find(ShaderName) == null) throw new InvalidOperationException("Toon Shader FX is missing.");
            EnsureFolder(Converted);
            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/MazeParty" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (GamePath(path)) Resolve(AssetDatabase.LoadAssetAtPath<Material>(path));
            }
            GetTerrainMaterial();
            AssetDatabase.SaveAssets();
            return "Prepared " + Changes.Count + " material changes.";
        }

        private static bool ApplyRoot(GameObject root)
        {
            bool changed = false;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (renderer.GetComponent<TextMesh>() != null || renderer.GetComponentInParent<Canvas>() != null) continue;
                var materials = renderer.sharedMaterials;
                bool replace = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    var result = Resolve(materials[i]);
                    if (result == materials[i]) continue;
                    materials[i] = result;
                    replace = true;
                }
                if (replace)
                {
                    renderer.sharedMaterials = materials;
                    if (PrefabUtility.IsPartOfPrefabInstance(renderer))
                        PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                    changed = true;
                }
            }
            foreach (var terrain in root.GetComponentsInChildren<Terrain>(true))
            {
                var material = GetTerrainMaterial();
                if (terrain.materialTemplate == material) continue;
                terrain.materialTemplate = material;
                if (PrefabUtility.IsPartOfPrefabInstance(terrain))
                    PrefabUtility.RecordPrefabInstancePropertyModifications(terrain);
                changed = true;
            }
            return changed;
        }

        public static string ApplyPrefabs()
        {
            var paths = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/MazeParty" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(GamePath)
                .OrderBy(p => AssetDatabase.GetDependencies(p).Length).ToArray();
            int count = 0;
            foreach (var path in paths)
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    if (!ApplyRoot(root)) continue;
                    Backup(path);
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                    Changes.Add("Prefab: " + path);
                    count++;
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            SaveReport();
            return "Updated " + count + " prefabs.";
        }

        public static string ApplyScenes()
        {
            var active = SceneManager.GetActiveScene();
            var paths = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/MazeParty" })
                .Select(AssetDatabase.GUIDToAssetPath).Where(GamePath).ToArray();
            int count = 0;
            try
            {
                foreach (var path in paths)
                {
                    var scene = SceneManager.GetSceneByPath(path);
                    bool alreadyLoaded = scene.IsValid() && scene.isLoaded;
                    if (alreadyLoaded && scene.isDirty) throw new InvalidOperationException("Unsaved scene: " + path);
                    if (!alreadyLoaded) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    try
                    {
                        bool changed = false;
                        foreach (var root in scene.GetRootGameObjects()) changed |= ApplyRoot(root);
                        if (!changed) continue;
                        Backup(path);
                        EditorSceneManager.SaveScene(scene);
                        Changes.Add("Scene: " + path);
                        count++;
                    }
                    finally { if (!alreadyLoaded) EditorSceneManager.CloseScene(scene, true); }
                }
            }
            finally { if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active); }
            AssetDatabase.SaveAssets();
            SaveReport();
            return "Updated " + count + " scenes.";
        }

        private static void SaveReport()
        {
            Directory.CreateDirectory("Logs/ToonRollout-20261019");
            File.WriteAllLines("Logs/ToonRollout-20261019/rollout-changes.txt", Changes);
        }

        [MenuItem("MazeParty/Art/Apply Toon Shader FX To Game")]
        public static void ApplyToGame()
        {
            PrepareMaterials();
            ApplyPrefabs();
            ApplyScenes();
        }
    }
}
