using System;
using System.IO;
using System.Linq;
using MazeParty.Gameplay;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MazeParty.Editor
{
    /// <summary>Explicit art migration; reruns preserve already-installed authored models.</summary>
    public static class ItemVisualFeedbackProjectSetup
    {
        public const string InputSpritePath =
            "Assets/Ignore/Input Sprites for TextMesh Pro/all input icons same size.asset";
        public const string SwitchSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Objects/switch_007.prefab";
        private const string DiceFolder = "Assets/MazeParty/Prefabs/Board/Dice";
        private const string GiftMaterials = "Assets/MazeParty/Materials/Board/ChristmasGifts";

        [MenuItem("MazeParty/Gameplay/Apply Item Visual Feedback")]
        public static void Apply()
        {
            Directory.CreateDirectory(GiftMaterials);
            AssetDatabase.Refresh();
            UpgradeGift("Assets/MazeParty/Prefabs/Minigames/GiftGrab/Gift.prefab");
            UpgradeGift("Assets/MazeParty/Prefabs/Board/World/BoardLandingGift.prefab");
            var model = EnsurePositionSwapper();
            var item = AssetDatabase.LoadAssetAtPath<BoardItemDefinition>(
                "Assets/MazeParty/Resources/MazeParty/Items/PositionSwapper.asset");
            if (item == null) throw new InvalidOperationException("Missing PositionSwapper item definition.");
            item.HeldPrefab = model;
            item.WorldPrefab = model;
            EditorUtility.SetDirty(item);
            UpgradeDice();
            AssetDatabase.SaveAssets();
        }

        private static void UpgradeGift(string path)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                if (root.GetComponent<GiftVisualVariants>() != null) return;
                RemoveChildren(root);
                var variants = new GameObject[3];
                for (var index = 0; index < variants.Length; index++)
                {
                    var sourcePath = "Assets/Ignore/Christmas Gifts/Prefabs/box" + (index + 1) + ".prefab";
                    var source = RequiredPrefab(sourcePath);
                    var model = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                    model.name = "Box " + (index + 1);
                    model.transform.localPosition = Vector3.zero;
                    model.transform.localRotation = Quaternion.identity;
                    var bounds = MeshBounds(model);
                    model.transform.localScale *= 0.7f / Mathf.Max(bounds.size.x, bounds.size.z);
                    bounds = MeshBounds(model);
                    model.transform.localPosition -= root.transform.InverseTransformVector(
                        bounds.center - root.transform.position);
                    foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
                        renderer.sharedMaterials = renderer.sharedMaterials.Select(ConvertGiftMaterial).ToArray();
                    StripPhysics(model);
                    variants[index] = model;
                    model.SetActive(index == 0);
                }
                var component = root.AddComponent<GiftVisualVariants>();
                var data = new SerializedObject(component);
                var references = data.FindProperty("variants");
                references.arraySize = 3;
                for (var index = 0; index < variants.Length; index++)
                    references.GetArrayElementAtIndex(index).objectReferenceValue = variants[index];
                data.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static Material ConvertGiftMaterial(Material source)
        {
            if (source == null) throw new InvalidOperationException("Gift has an unassigned material.");
            var path = GiftMaterials + "/" + source.name + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) throw new InvalidOperationException("URP Lit shader is missing.");
            material = new Material(shader) { name = source.name };
            material.SetColor("_BaseColor", source.HasProperty("_Color") ? source.GetColor("_Color") : Color.white);
            if (source.HasProperty("_MainTex"))
            {
                material.SetTexture("_BaseMap", source.GetTexture("_MainTex"));
                material.SetTextureScale("_BaseMap", source.GetTextureScale("_MainTex"));
                material.SetTextureOffset("_BaseMap", source.GetTextureOffset("_MainTex"));
            }
            if (source.HasProperty("_BumpMap") && source.GetTexture("_BumpMap") != null)
            {
                material.SetTexture("_BumpMap", source.GetTexture("_BumpMap"));
                material.EnableKeyword("_NORMALMAP");
            }
            material.SetFloat("_Smoothness", 0.25f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        public static GameObject EnsurePositionSwapper()
        {
            const string path = "Assets/MazeParty/Prefabs/Board/Items/PositionSwapper.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null && existing.transform.Find("Switch 007") != null) return existing;
            var root = existing != null ? PrefabUtility.LoadPrefabContents(path) : new GameObject("PositionSwapper");
            try
            {
                RemoveChildren(root);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(RequiredPrefab(SwitchSourcePath), root.transform);
                model.name = "Switch 007";
                model.transform.localPosition = Vector3.zero;
                // Buttons face the sky (+Z -> +Y); red 003 is forward (+Y -> +Z).
                model.transform.localRotation = Quaternion.LookRotation(Vector3.up, Vector3.forward);
                model.transform.localScale = Vector3.one * 0.6f;
                var bounds = MeshBounds(model);
                model.transform.localPosition -= root.transform.InverseTransformVector(
                    bounds.center - root.transform.position);
                StripPhysics(model);
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                if (existing != null) PrefabUtility.UnloadPrefabContents(root);
                else UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void UpgradeDice()
        {
            var path = DiceFolder + "/NetworkWorldDie.prefab";
            var root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                var hint = root.GetComponentInChildren<WorldDieReadyHint>(true);
                if (hint == null) throw new InvalidOperationException("Missing authored dice instructions.");
                ApplyInputIcon(hint.transform.Find("Left Mouse").gameObject, "ml");
                ApplyInputIcon(hint.transform.Find("Right Mouse").gameObject, "mr");
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        public static Renderer ApplyInputIcon(GameObject target, string spriteName)
        {
            var asset = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(InputSpritePath);
            if (asset == null) throw new InvalidOperationException("Missing input sprite asset.");
            var character = asset.spriteCharacterTable.Single(value => value.name == spriteName);
            var rect = character.glyph.glyphRect;
            var texture = asset.spriteSheet;
            var meshPath = DiceFolder + (spriteName == "mr" ? "/MouseRight.asset" : "/MouseLeft.asset");
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            var isNew = mesh == null;
            if (isNew) mesh = new Mesh { name = spriteName };
            mesh.Clear();
            const float height = 0.41f;
            var width = height * rect.width / rect.height;
            mesh.vertices = new[] {
                new Vector3(-width * 0.5f, -height * 0.5f, 0),
                new Vector3(-width * 0.5f, height * 0.5f, 0),
                new Vector3(width * 0.5f, height * 0.5f, 0),
                new Vector3(width * 0.5f, -height * 0.5f, 0) };
            var u0 = (rect.x + 0.5f) / texture.width;
            var u1 = (rect.x + rect.width - 0.5f) / texture.width;
            var v0 = (rect.y + 0.5f) / texture.height;
            var v1 = (rect.y + rect.height - 0.5f) / texture.height;
            mesh.uv = new[] { new Vector2(u0, v0), new Vector2(u0, v1), new Vector2(u1, v1), new Vector2(u1, v0) };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            if (isNew) AssetDatabase.CreateAsset(mesh, meshPath);
            else EditorUtility.SetDirty(mesh);

            var materialPath = DiceFolder + "/MouseIcon.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            var newMaterial = material == null;
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("URP Unlit shader is missing.");
            if (newMaterial) material = new Material(shader) { name = "MouseIcon" };
            material.shader = shader;
            material.SetTexture("_BaseMap", texture);
            material.SetColor("_BaseColor", Color.white);
            material.SetFloat("_Surface", 1f);
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.SetFloat("_Cull", (float)CullMode.Off);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)RenderQueue.Transparent;
            if (newMaterial) AssetDatabase.CreateAsset(material, materialPath);
            else EditorUtility.SetDirty(material);
            var filter = target.GetComponent<MeshFilter>() ?? target.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = target.GetComponent<MeshRenderer>() ?? target.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }

        private static GameObject RequiredPrefab(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException("Missing model: " + path);
            return prefab;
        }

        private static Bounds MeshBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) throw new InvalidOperationException("No mesh renderers: " + root.name);
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        private static void StripPhysics(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
                UnityEngine.Object.DestroyImmediate(body);
        }

        private static void RemoveChildren(GameObject root)
        {
            while (root.transform.childCount > 0)
                UnityEngine.Object.DestroyImmediate(root.transform.GetChild(0).gameObject);
        }
    }
}
