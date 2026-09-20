using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class BoardTombstoneProjectSetup
    {
        private const string BoardPath = "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string PrefabFolder = "Assets/MazeParty/Prefabs/Board/World";
        private const string PrefabPath = PrefabFolder + "/BoardTombstone.prefab";
        private const string MaterialFolder = "Assets/MazeParty/Board/Materials";
        private const string MaterialPath = MaterialFolder + "/BoardTombstoneStone.mat";

        [MenuItem("MazeParty/Gameplay/Install Board Tombstone Presentation")]
        public static void Install()
        {
            var scene = SceneManager.GetSceneByPath(BoardPath);
            var openedForInstall = !scene.IsValid() || !scene.isLoaded;
            if (openedForInstall)
            {
                scene = EditorSceneManager.OpenScene(BoardPath, OpenSceneMode.Additive);
            }

            BoardTopology topology = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                topology = root.GetComponentInChildren<BoardTopology>(true);
                if (topology != null)
                {
                    break;
                }
            }
            if (topology == null)
            {
                throw new System.InvalidOperationException("Board scene has no BoardTopology.");
            }

            EnsureView(topology.gameObject);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            if (openedForInstall)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        internal static void EnsureView(GameObject topologyRoot)
        {
            var view = topologyRoot.GetComponent<BoardTombstoneWorldView>();
            if (view == null)
            {
                view = topologyRoot.AddComponent<BoardTombstoneWorldView>();
            }
            view.Configure(EnsurePrefab());
            EditorUtility.SetDirty(view);
        }

        private static BoardTombstoneMarker EnsurePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null)
            {
                return existing.GetComponent<BoardTombstoneMarker>();
            }

            EnsureFolder("Assets/MazeParty/Prefabs/Board", "World");
            EnsureFolder("Assets/MazeParty/Board", "Materials");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.color = new Color(0.37f, 0.44f, 0.5f);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }

            var root = new GameObject("Board Tombstone");
            var marker = root.AddComponent<BoardTombstoneMarker>();
            var collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0.55f, 0f);
            collider.size = new Vector3(0.62f, 1.1f, 0.34f);

            var stone = GameObject.CreatePrimitive(PrimitiveType.Cube);
            stone.name = "Stone";
            stone.transform.SetParent(root.transform, false);
            stone.transform.localPosition = new Vector3(0f, 0.53f, 0f);
            stone.transform.localScale = new Vector3(0.58f, 0.92f, 0.27f);
            Object.DestroyImmediate(stone.GetComponent<Collider>());
            stone.GetComponent<MeshRenderer>().sharedMaterial = material;

            var crown = GameObject.CreatePrimitive(PrimitiveType.Cube);
            crown.name = "Crown";
            crown.transform.SetParent(root.transform, false);
            crown.transform.localPosition = new Vector3(0f, 1.01f, 0f);
            crown.transform.localScale = new Vector3(0.58f, 0.17f, 0.27f);
            Object.DestroyImmediate(crown.GetComponent<Collider>());
            crown.GetComponent<MeshRenderer>().sharedMaterial = material;

            var labelObject = new GameObject("Gold Label");
            labelObject.transform.SetParent(root.transform, false);
            labelObject.transform.localPosition = new Vector3(0f, 1.22f, 0f);
            labelObject.transform.localRotation = Quaternion.identity;
            var label = labelObject.AddComponent<TextMesh>();
            label.anchor = TextAnchor.LowerCenter;
            label.alignment = TextAlignment.Center;
            label.characterSize = 0.11f;
            label.fontSize = 48;
            label.color = new Color(1f, 0.84f, 0.33f);
            label.text = "+GOLD\nRMB";
            marker.SetGoldLabel(label);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab.GetComponent<BoardTombstoneMarker>();
        }

        private static void EnsureFolder(string parent, string folder)
        {
            if (!AssetDatabase.IsValidFolder(parent + "/" + folder))
            {
                AssetDatabase.CreateFolder(parent, folder);
            }
        }
    }
}
