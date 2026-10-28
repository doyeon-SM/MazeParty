using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Editor
{
    public static class BoardForestGhostAuthoring
    {
        public const string PrefabPath = "Assets/MazeParty/Prefabs/Board/World/ForestGhost.prefab";
        public static BoardForestGhostView EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    if (EnsureDamageShieldBinding(contents))
                        PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
                return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<BoardForestGhostView>();
            }
            var root = new GameObject("Forest Ghost");
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Ignore/GhostCharacter_Free/Prefabs/Ghost.prefab");
                if (source == null) throw new System.InvalidOperationException("GhostCharacter_Free is required.");
                var ghost = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
                PrefabUtility.UnpackPrefabInstance(ghost, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                foreach (var script in ghost.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
                foreach (var collider in ghost.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
                foreach (var animator in ghost.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                ghost.name = "Ghost Model";
                var view = root.AddComponent<BoardForestGhostView>();
                var serialized = new SerializedObject(view);
                serialized.FindProperty("model").objectReferenceValue = ghost.transform;
                var renderers = ghost.GetComponentsInChildren<Renderer>(true);
                var array = serialized.FindProperty("ghostRenderers"); array.arraySize = renderers.Length;
                for (int i = 0; i < renderers.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = renderers[i];
                var original = renderers[0].sharedMaterial;
                serialized.FindProperty("normalMaterial").objectReferenceValue = EnsureMaterial(original, "ForestGhostWhite", Color.white);
                serialized.FindProperty("strongMaterial").objectReferenceValue = EnsureMaterial(original, "ForestGhostPurple", new Color(0.65f, 0.22f, 1f));
                foreach (var pair in new[] { ("idleClip", "ghost_idle"), ("attackClip", "ghost_attack"), ("dissolveClip", "ghost_dissolve") })
                    serialized.FindProperty(pair.Item1).objectReferenceValue = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                        "Assets/Ignore/GhostCharacter_Free/AnimationClips/" + pair.Item2 + ".anim");
                foreach (var pair in new[] { ("coin", "Assets/MazeParty/Prefabs/Board/World/BoardEventCoin.prefab"),
                    ("key", "Assets/MazeParty/Prefabs/Board/World/BoardEventKey.prefab"),
                    ("healing", "Assets/MazeParty/Prefabs/Common/VFX/HealingShield.prefab") })
                {
                    var item = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(pair.Item2), root.transform);
                    if (pair.Item1 != "healing") item.transform.localScale *= 0.35f;
                    foreach (var particle in item.GetComponentsInChildren<ParticleSystem>(true))
                    { particle.useAutoRandomSeed = false; particle.randomSeed = 1027; }
                    item.SetActive(false);
                    serialized.FindProperty(pair.Item1).objectReferenceValue = item;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EnsureDamageShieldBinding(root);
                foreach (var renderer in renderers) renderer.sharedMaterial = (Material)serialized.FindProperty("normalMaterial").objectReferenceValue;
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath).GetComponent<BoardForestGhostView>();
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static bool EnsureDamageShieldBinding(GameObject root)
        {
            var data = new SerializedObject(root.GetComponent<BoardForestGhostView>());
            var field = data.FindProperty("damageShield");
            if (field.objectReferenceValue != null) return false;
            var shield = (GameObject)PrefabUtility.InstantiatePrefab(EnsureDamageShieldPrefab(), root.transform);
            var healing = (GameObject)data.FindProperty("healing").objectReferenceValue;
            shield.transform.localPosition = healing.transform.localPosition;
            shield.transform.localRotation = healing.transform.localRotation;
            shield.transform.localScale = healing.transform.localScale;
            foreach (var particle in shield.GetComponentsInChildren<ParticleSystem>(true))
            { particle.useAutoRandomSeed = false; particle.randomSeed = 1027; }
            shield.SetActive(false);
            field.objectReferenceValue = shield;
            data.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static GameObject EnsureDamageShieldPrefab()
        {
            const string path = "Assets/MazeParty/Prefabs/Common/VFX/ForestGhostDamageShield.prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) return existing;
            var root = (GameObject)PrefabUtility.InstantiatePrefab(
                BoardLandingEffectFeedbackProjectSetup.EnsureHealingShieldPrefab());
            try
            {
                root.name = "ForestGhostDamageShield";
                var copies = new System.Collections.Generic.Dictionary<Material, Material>();
                foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (var i = 0; i < materials.Length; i++)
                    {
                        var source = materials[i];
                        if (source == null) continue;
                        if (!copies.TryGetValue(source, out var red))
                        {
                            var materialPath = "Assets/MazeParty/Materials/ForestGhostDamageShield_" + copies.Count + ".mat";
                            red = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                            if (red == null)
                            {
                                red = new Material(source) { name = "ForestGhostDamageShield_" + copies.Count };
                                foreach (var property in new[] { "_Color", "_RimColor",
                                    "_ColorGradingLight", "_ColorGradingMiddle", "_ColorGradingDark" })
                                {
                                    if (!red.HasProperty(property)) continue;
                                    var color = red.GetColor(property);
                                    var intensity = Mathf.Max(color.r, color.g, color.b);
                                    red.SetColor(property, new Color(intensity, intensity * .12f, intensity * .08f, color.a));
                                }
                                if (red.HasProperty("_HsvShift"))
                                    red.SetFloat("_HsvShift", 0f);
                                // The source blue ramp overrides the base tint; retain the animated shapes with a red tint.
                                if (red.HasProperty("_ColorRampBlend")) red.SetFloat("_ColorRampBlend", 0f);
                                AssetDatabase.CreateAsset(red, materialPath);
                            }
                            copies.Add(source, red);
                        }
                        materials[i] = red;
                    }
                    renderer.sharedMaterials = materials;
                }
                return PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally { Object.DestroyImmediate(root); }
        }

        private static Material EnsureMaterial(Material source, string name, Color color)
        {
            var path = "Assets/MazeParty/Materials/" + name + ".mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null) return existing;
            var material = new Material(source) { name = name };
            material.SetColor("_MainColor", color);
            material.SetFloat("_Dissolve", 1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
    }
}
