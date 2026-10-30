using System;
using System.Linq;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MazeParty.Editor
{
    /// <summary>Seeds the requested presentation on missing elements; existing UI design is preserved.</summary>
    public static class GameplayPresentationAssetSetup
    {
        private const string Prefabs = "Assets/MazeParty/Prefabs/";
        private const string Toolkit = "Assets/Ignore/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/";

        public static void ApplyRequestedPresentation()
        {
            Edit(Prefabs + "Minigames/Common/UI/BouncingBallsHud.prefab", EnsureScoreOutlines);
            Edit(Prefabs + "Board/UI/BoardItemCanvas.prefab", EnsureUseHint);
            Edit(Prefabs + "Multiplayer/UI/LobbyCanvas.prefab", root =>
                root.GetComponent<OnlineLobbyView>().ConfigureBoardMapTurns(
                    EnsureLobbyTurns(root.transform.Find("Selected Board Map").gameObject)));
            Edit(Prefabs + "Board/UI/BoardMapCanvas.prefab", root =>
            {
                var binding = root.GetComponent<BoardMapCanvasBindings>();
                binding.MovesText.resizeTextForBestFit = false;
                // Leave room for the largest number at the bounce peak.
                binding.MovesText.rectTransform.sizeDelta = new Vector2(240f, 120f);
            });
            for (var i = 1; i <= 4; i++)
                Edit(Prefabs + "Minigames/SnowySpin/PlayerBall" + i + ".prefab", SimplifySphere);
            Edit(Prefabs + "Minigames/BouncingBalls/Ball.prefab", SimplifySphere);
            Edit(Prefabs + "Minigames/CliffBarrage/Projectile.prefab", AuthorFireBullet);
            Edit(Prefabs + "Minigames/CliffBarrage/LaserRig.prefab", AuthorBlueBeam);
            AssetDatabase.SaveAssets();
        }

        public static void EnsureScoreOutlines(GameObject root)
        {
            var hud = root.GetComponent<BouncingBallsHudBindings>();
            var outlines = new Outline[4];
            for (var i = 0; i < 4; i++)
            {
                var panel = hud.ScoreTexts[i].transform.parent.gameObject;
                var existing = panel.GetComponents<Outline>();
                outlines[i] = existing.Length > 1 ? existing[existing.Length - 1] : panel.AddComponent<Outline>();
                if (existing.Length > 0) existing[0].enabled = true;
                outlines[i].effectColor = new Color(1f, 0.85f, 0.05f, 1f);
                outlines[i].effectDistance = new Vector2(3f, -3f);
                outlines[i].useGraphicAlpha = false;
                outlines[i].enabled = false;
            }
            hud.Configure(hud.RootCanvas, hud.Root, hud.PlayerNameTexts, hud.ScoreTexts, outlines);
        }

        public static void EnsureUseHint(GameObject root)
        {
            var action = root.transform.Find("Item Use Hint");
            if (action == null)
            {
                var odd = AssetDatabase.LoadAssetAtPath<GameObject>(Prefabs + "Minigames/OddOneOut/UI/OddOneOutHud.prefab");
                var source = odd.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Click Action");
                var clone = Object.Instantiate(source.gameObject, root.transform, false);
                clone.name = "Item Use Hint";
                action = clone.transform;
                var rect = clone.GetComponent<RectTransform>();
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 125f);
                rect.sizeDelta = new Vector2(280f, 84f);
                foreach (var graphic in clone.GetComponentsInChildren<Graphic>(true)) graphic.raycastTarget = false;
                clone.GetComponentInChildren<Text>(true).text = "Use";
                clone.SetActive(false);
            }
            var data = new SerializedObject(root.GetComponent<BoardItemCanvasBindings>());
            data.FindProperty("references.UseHintRoot").objectReferenceValue = action.gameObject;
            data.FindProperty("references.UseHintIcon").objectReferenceValue = action.GetComponentInChildren<RawImage>(true);
            data.FindProperty("references.UseHintText").objectReferenceValue = action.GetComponentInChildren<Text>(true);
            data.ApplyModifiedPropertiesWithoutUndo();
        }

        public static Text EnsureLobbyTurns(GameObject previewRoot)
        {
            var existing = previewRoot.transform.Find("Turn Count");
            if (existing != null) return existing.GetComponent<Text>();
            var title = previewRoot.transform.Find("Map Name").GetComponent<Text>();
            var label = Object.Instantiate(title, previewRoot.transform, false);
            label.name = "Turn Count";
            foreach (var localization in label.GetComponents<LocalizedText>()) Object.DestroyImmediate(localization);
            label.text = "Turn: 00";
            label.fontSize = 20;
            label.raycastTarget = false;
            label.rectTransform.anchoredPosition = new Vector2(0f, -62f);
            label.rectTransform.sizeDelta = new Vector2(-36f, 28f);
            var surface = previewRoot.transform.Find("Map Surface") as RectTransform;
            if (surface != null) surface.sizeDelta -= new Vector2(0f, 28f);
            return label;
        }

        private static void SimplifySphere(GameObject root)
        {
            for (var i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            root.GetComponent<MeshFilter>().sharedMesh = sphere.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(sphere);
            // Standard lit materials retain each game's existing ownership colour updates.
            var renderer = root.GetComponent<MeshRenderer>();
            var path = "Assets/MazeParty/Materials/" + (root.name.StartsWith("PlayerBall") ? "SnowySpinSimpleSphere" : "PinballSimpleSphere") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", renderer.sharedMaterial != null && renderer.sharedMaterial.HasProperty("_BaseColor")
                    ? renderer.sharedMaterial.GetColor("_BaseColor") : Color.white);
                material.SetFloat("_Smoothness", 0.35f);
                AssetDatabase.CreateAsset(material, path);
            }
            renderer.sharedMaterial = material;
        }

        public static void AuthorFireBullet(GameObject root)
        {
            if (root.transform.Find("Fire Bullet") != null) return;
            for (var i = root.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            RemoveGeometry(root);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Toolkit + "Fire Bullet.prefab");
            var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, root.transform);
            PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            visual.name = "Fire Bullet";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one * 0.6f;
            foreach (var animator in visual.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
            foreach (var script in visual.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
            foreach (var light in visual.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light.gameObject);
            foreach (var trail in visual.GetComponentsInChildren<TrailRenderer>(true))
            { trail.widthMultiplier *= 0.45f; trail.time = 0.15f; }
            root.SetActive(false);
        }

        public static void AuthorBlueBeam(GameObject root)
        {
            var legacyEmitter = root.transform.Find("Laser Emitter Source Visual");
            if (legacyEmitter != null) Object.DestroyImmediate(legacyEmitter.gameObject);
            if (root.GetComponent<CliffBarrageLaserVisual>() != null) return;
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Toolkit + "Beam Blue.prefab");
            var sphereSource = source.GetComponentsInChildren<Transform>(true).Single(t => t.name == "ChargeSphere");
            var beamSource = source.GetComponentsInChildren<Transform>(true).Single(t => t.name == "BlueBeam");
            var warning = root.transform.Find("One Second Telegraph").gameObject;
            var firing = root.transform.Find("Half Second Beam").gameObject;
            RemoveGeometry(firing);
            firing.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            firing.transform.localRotation = Quaternion.identity;
            firing.transform.localScale = Vector3.one;
            var charge = Object.Instantiate(sphereSource.gameObject, root.transform, false);
            charge.name = "Beam Blue Charge Sphere";
            for (var i = charge.transform.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(charge.transform.GetChild(i).gameObject);
            charge.transform.localPosition = new Vector3(0f, 0.75f, 0f);
            charge.transform.localRotation = Quaternion.identity;
            charge.transform.localScale = Vector3.one * 0.95f;
            var forward = Object.Instantiate(beamSource.gameObject, firing.transform, false).transform;
            forward.name = "Beam Blue Forward";
            forward.localPosition = Vector3.zero;
            forward.localRotation = Quaternion.identity;
            var backward = Object.Instantiate(forward, firing.transform, false);
            backward.name = "Beam Blue Backward";
            backward.localRotation = Quaternion.Euler(0f, 180f, 0f);
            var binding = root.AddComponent<CliffBarrageLaserVisual>();
            var data = new SerializedObject(binding);
            data.FindProperty("chargeSphere").objectReferenceValue = charge;
            data.FindProperty("warningRoot").objectReferenceValue = warning;
            data.FindProperty("firingRoot").objectReferenceValue = firing;
            data.FindProperty("forwardBeam").objectReferenceValue = forward;
            data.FindProperty("backwardBeam").objectReferenceValue = backward;
            data.FindProperty("meshLength").floatValue = beamSource.GetComponent<MeshFilter>().sharedMesh.bounds.size.z;
            data.ApplyModifiedPropertiesWithoutUndo();
            binding.Present(MazeParty.Gameplay.Minigames.CliffBarrage.CliffBarrageLaserPhase.Inactive, 20f);
            root.SetActive(false);
        }

        private static void RemoveGeometry(GameObject root)
        {
            var renderer = root.GetComponent<MeshRenderer>();
            if (renderer != null) Object.DestroyImmediate(renderer);
            var filter = root.GetComponent<MeshFilter>();
            if (filter != null) Object.DestroyImmediate(filter);
            foreach (var collider in root.GetComponents<Collider>()) Object.DestroyImmediate(collider);
        }

        private static void Edit(string path, Action<GameObject> action)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { action(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
    }
}
