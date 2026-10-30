using System;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Editor
{
    /// <summary>Explicit one-time migration; normal setup preserves authored designs.</summary>
    public static class AwardCeremonyFeedbackAuthoring
    {
        private const string LobbyPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/World/LobbyArena.prefab";

        [MenuItem("MazeParty/Gameplay/Apply Ceremony Lobby Design")]
        public static void ApplyLobbyDesign()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Ceremony authoring requires Edit Mode.");
            Upgrade(AwardCeremonyProjectSetup.StagePrefabPath, AttachLobbyEnvironment);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("MazeParty/Gameplay/Apply Ceremony Feedback")]
        public static void Apply()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Ceremony authoring requires Edit Mode.");
            Upgrade(AwardCeremonyProjectSetup.CanvasPrefabPath, UpgradeCanvas);
            Upgrade(AwardCeremonyProjectSetup.StagePrefabPath, UpgradeStage);
            AssetDatabase.SaveAssets();
        }

        private static void Upgrade(string path, Func<GameObject, bool> edit)
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { if (edit(root)) PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        private static bool UpgradeCanvas(GameObject root)
        {
            var b = root.GetComponent<AwardCeremonyCanvasBindings>();
            if (b.HasRequiredReferences) return false;
            var overlay = root.transform.Find("Bonus Award Overlay");
            var final = root.transform.Find("Final Ranking Panel");
            if (overlay == null || final == null) throw new InvalidOperationException("Unknown ceremony Canvas layout.");
            Remove(final.Find("Final Title Backing"));
            Remove(final.Find("Ranking Backing"));
            Remove(overlay.Find("Award Value"));
            final.name = "Return Controls";
            var winners = overlay.Find("Award Winner").GetComponent<Text>();
            winners.fontSize = 42;
            winners.supportRichText = true;
            winners.resizeTextForBestFit = true;
            winners.resizeTextMinSize = 28;
            winners.resizeTextMaxSize = 42;
            Layout(winners.rectTransform, new Vector2(.08f,.24f), new Vector2(.92f,.51f));
            var inputLock = final.Find("Input Lock").GetComponent<Text>();
            Layout(inputLock.rectTransform, new Vector2(.04f,.11f), new Vector2(.65f,.17f));
            inputLock.text = string.Empty;
            var status = final.Find("Return Status").GetComponent<Text>();
            Layout(status.rectTransform, new Vector2(.04f,.03f), new Vector2(.65f,.11f));
            status.text = string.Empty;
            var button = final.Find("Leave Room Button").GetComponent<Button>();
            b.Configure(root.GetComponent<Canvas>(), root.GetComponent<GraphicRaycaster>(),
                overlay.gameObject, overlay.GetComponent<Animator>(),
                overlay.Find("Award Step").GetComponent<Text>(),
                overlay.Find("Award Category").GetComponent<Text>(), winners,
                overlay.Find("Award Reward").GetComponent<Text>(), final.gameObject,
                inputLock, button, button.GetComponentInChildren<Text>(true), status);
            root.GetComponent<AwardCeremonyView>().Configure(b);
            final.gameObject.SetActive(false);
            return true;
        }

        public static GameObject CreateStage(Material stageMaterial, Material podiumMaterial)
        {
            var root = new GameObject("AwardCeremonyStage", typeof(AwardCeremonyStageBindings),
                typeof(AwardCeremonyPresentation));
            var presentation = new GameObject("Stage Presentation").transform;
            presentation.SetParent(root.transform, false);
            Cube(presentation, "Stage Floor", new Vector3(0,-.35f,1), new Vector3(27,.7f,12), stageMaterial);
            Cube(presentation, "Stage Backdrop", new Vector3(0,4.2f,7.3f), new Vector3(27,9,.5f), stageMaterial);
            // The migration also defines the initial template, so both entry points agree.
            Cube(presentation, "Podium 1", Vector3.zero, Vector3.one, podiumMaterial);
            var camera = new GameObject("CM_AwardCeremonyShared", typeof(CinemachineCamera));
            camera.transform.SetParent(root.transform, false);
            UpgradeStage(root);
            AttachLobbyEnvironment(root);
            return root;
        }

        private static bool AttachLobbyEnvironment(GameObject root)
        {
            var b = root.GetComponent<AwardCeremonyStageBindings>();
            if (b == null || !b.HasRequiredReferences)
                throw new InvalidOperationException("Complete ceremony bindings are required before replacing the room.");
            var presentation = b.PresentationRoot.transform;
            if (presentation.GetComponentInChildren<LobbyArenaBindings>(true) != null)
                return false; // Re-running setup must preserve the authored room and camera.
            var lobbyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefabPath);
            if (lobbyPrefab == null || !lobbyPrefab.GetComponent<LobbyArenaBindings>().HasRequiredReferences)
                throw new InvalidOperationException("The authored LobbyArena prefab is required for the ceremony.");

            var room = (GameObject)PrefabUtility.InstantiatePrefab(lobbyPrefab, presentation);
            room.name = "Ceremony Prison Room";
            room.transform.localPosition = Vector3.zero;
            room.transform.localRotation = Quaternion.identity;
            room.transform.localScale = Vector3.one;
            // Retain the nested design link without registering a second live lobby.
            UnityEngine.Object.DestroyImmediate(room.GetComponent<LobbyArena>());
            var lobby = room.GetComponent<LobbyArenaBindings>();
            var floor = lobby.PresentationRoot.transform.Find("Authored Collision Floor")
                .GetComponent<BoxCollider>();
            var bounds = lobby.HorizontalBounds;
            floor.transform.SetPositionAndRotation(bounds.transform.position, bounds.transform.rotation);
            floor.transform.localScale = bounds.transform.localScale;
            // The visual tiles have a top surface at -0.02. Cover the entire room,
            // including the outer walking lanes, with one continuous solid floor.
            floor.size = new Vector3(bounds.size.x, .4f, bounds.size.z);
            floor.center = new Vector3(bounds.center.x, -.22f, bounds.center.z);
            floor.enabled = true;
            floor.isTrigger = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(room.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(floor.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(floor);
            Remove(presentation.Find("Stage Floor"));
            Remove(presentation.Find("Stage Backdrop"));
            Remove(presentation.Find("Stage Fill Light"));
            b.Configure(b.PresentationRoot, b.PodiumRoots, b.PlayerAnchors, b.RankLabels,
                b.KeyLabels, b.WinnerSpotlights, b.SharedCamera, floor);
            b.SharedCamera.transform.localPosition = new Vector3(0f, 15.5f, -17f);
            b.SharedCamera.transform.localRotation = Quaternion.Euler(40f, 0f, 0f);
            var lens = b.SharedCamera.Lens;
            lens.FieldOfView = 50f;
            b.SharedCamera.Lens = lens;
            return true;
        }

        private static bool UpgradeStage(GameObject root)
        {
            var b = root.GetComponent<AwardCeremonyStageBindings>();
            if (b.HasRequiredReferences) return false;
            var presentation = root.transform.Find("Stage Presentation");
            if (presentation == null) throw new InvalidOperationException("Unknown ceremony stage layout.");
            var firstPodium = presentation.Find("Podium 1");
            var podiumMaterial = firstPodium.GetComponent<Renderer>().sharedMaterial;
            for (var i = presentation.childCount - 1; i >= 0; i--)
            {
                var child = presentation.GetChild(i);
                if (child.name.StartsWith("Podium ") || child.name.StartsWith("Rank Label ") ||
                    child.name.StartsWith("Winner Spotlight ") || child.name == "Runtime Players")
                    Remove(child);
            }
            var floor = presentation.Find("Stage Floor");
            floor.localPosition = new Vector3(0,-.35f,1);
            floor.localScale = new Vector3(27,.7f,12);
            var floorCollider = floor.GetComponent<BoxCollider>();
            if (floorCollider == null) floorCollider = floor.gameObject.AddComponent<BoxCollider>();
            var backdrop = presentation.Find("Stage Backdrop");
            backdrop.localPosition = new Vector3(0,4.2f,7.3f);
            backdrop.localScale = new Vector3(27,9,.5f);
            if (backdrop.GetComponent<BoxCollider>() == null) backdrop.gameObject.AddComponent<BoxCollider>();
            var podiums = new Transform[3];
            var anchors = new Transform[4];
            var ranks = new TextMesh[3];
            var keys = new TextMesh[3];
            var lights = new Light[3];
            var x = new[] {0f,-6f,6f};
            var height = new[] {2.4f,1.8f,1.25f};
            var colors = new[] {new Color(1,.72f,.12f), new Color(.72f,.78f,.86f), new Color(.75f,.38f,.14f)};
            for (var i = 0; i < 3; i++)
            {
                podiums[i] = Cube(presentation, "Podium " + (i+1),
                    new Vector3(x[i], height[i]*.5f,1.2f), new Vector3(5f,height[i],2.4f), podiumMaterial);
                // Use authored per-rank materials, preserving the shared base material.
                var materialPath = "Assets/MazeParty/Prefabs/Board/AwardCeremony/PodiumRank" + (i+1) + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(podiumMaterial) { name = "PodiumRank" + (i+1) };
                    material.SetColor("_BaseColor", colors[i]);
                    AssetDatabase.CreateAsset(material, materialPath);
                }
                podiums[i].GetComponent<Renderer>().sharedMaterial = material;
                anchors[i] = new GameObject("Player Anchor " + (i+1)).transform;
                anchors[i].SetParent(presentation, false);
                anchors[i].localPosition = new Vector3(x[i], height[i],1.2f);
                anchors[i].localRotation = Quaternion.Euler(0,180,0);
                ranks[i] = Label(presentation, "Rank Label " + (i+1),
                    new Vector3(x[i],height[i]*.74f,-.015f), i==0?"1st":i==1?"2nd":"3rd", .105f);
                keys[i] = Label(presentation, "Key Count " + (i+1),
                    new Vector3(x[i],height[i]*.22f,-.02f), string.Empty, .06f);
                if (i == 2) { ranks[i].color = Color.white; keys[i].color = Color.white; }
                var spotlight = new GameObject("Winner Spotlight " + (i+1), typeof(Light));
                spotlight.transform.SetParent(presentation, false);
                spotlight.transform.localPosition = new Vector3(x[i],10,1.2f);
                spotlight.transform.localRotation = Quaternion.Euler(90,0,0);
                lights[i] = spotlight.GetComponent<Light>();
                lights[i].type = LightType.Spot;
                lights[i].color = new Color(1,.84f,.46f);
                lights[i].intensity = 120;
                lights[i].range = 18;
                lights[i].spotAngle = 48;
                lights[i].shadows = LightShadows.Soft;
                spotlight.SetActive(false);
            }
            anchors[3] = new GameObject("Fourth Place Ground Anchor").transform;
            anchors[3].SetParent(presentation, false);
            anchors[3].localPosition = new Vector3(10.2f,0,1.2f);
            anchors[3].localRotation = Quaternion.Euler(0,180,0);
            var fill = new GameObject("Stage Fill Light", typeof(Light));
            fill.transform.SetParent(presentation, false);
            fill.transform.localPosition = new Vector3(0,7,-5);
            var fillLight = fill.GetComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.color = new Color(.8f,.88f,1f);
            fillLight.intensity = 80;
            fillLight.range = 35;
            var camera = root.transform.Find("CM_AwardCeremonyShared").GetComponent<CinemachineCamera>();
            camera.transform.localPosition = new Vector3(0,10.5f,-23.5f);
            camera.transform.localRotation = Quaternion.Euler(18,0,0);
            var lens = camera.Lens;
            lens.FieldOfView = 46;
            camera.Lens = lens;
            camera.Priority = 0;
            b.Configure(presentation.gameObject, podiums, anchors, ranks, keys, lights, camera, floorCollider);
            root.GetComponent<AwardCeremonyPresentation>().Configure(b);
            presentation.gameObject.SetActive(false);
            camera.gameObject.SetActive(false);
            return true;
        }

        private static TextMesh Label(Transform parent, string name, Vector3 position, string text, float size)
        {
            var obj = new GameObject(name, typeof(TextMesh));
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = position;
            var label = obj.GetComponent<TextMesh>();
            label.text = text;
            label.anchor = TextAnchor.MiddleCenter;
            label.alignment = TextAlignment.Center;
            label.fontSize = 72;
            label.characterSize = size;
            label.color = new Color(.06f,.075f,.11f);
            GameFonts.Apply(label);
            return label;
        }

        private static Transform Cube(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.localPosition = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            return cube.transform;
        }
        private static void Remove(Transform target) { if (target != null) UnityEngine.Object.DestroyImmediate(target.gameObject); }
        private static void Layout(RectTransform rect, Vector2 min, Vector2 max)
        { rect.anchorMin = min; rect.anchorMax = max; rect.offsetMin = rect.offsetMax = Vector2.zero; }
    }
}
