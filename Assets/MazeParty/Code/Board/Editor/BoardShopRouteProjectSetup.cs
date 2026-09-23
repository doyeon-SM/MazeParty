using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace MazeParty.Editor
{
    public static class BoardShopRouteProjectSetup
    {
        public const string DotPath = "Assets/MazeParty/Prefabs/Board/World/KeyShopRouteHemisphere.prefab";
        private const string MeshPath = "Assets/MazeParty/Board/Materials/KeyShopRouteHemisphere.asset";
        private const string MaterialPath = "Assets/MazeParty/Board/Materials/KeyShopRouteYellow.mat";
        private const string ScenePath = "Assets/MazeParty/Scenes/Board/Board.unity";

        [MenuItem("MazeParty/Board/Install Local Key Shop Route")]
        public static void Install()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                foreach (var root in scene.GetRootGameObjects())
                {
                    var topology = root.GetComponentInChildren<BoardTopology>(true);
                    if (topology == null) continue;
                    EnsureView(topology.gameObject);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                    AssetDatabase.SaveAssets();
                    return;
                }
                throw new System.InvalidOperationException("Board topology is missing.");
            }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        internal static void EnsureView(GameObject root)
        {
            var view = root.GetComponent<BoardShopRouteView>();
            if (view == null) view = root.AddComponent<BoardShopRouteView>();
            if (!view.HasRequiredReferences) view.Configure(EnsureDotPrefab());
            EditorUtility.SetDirty(view);
        }

        private static GameObject EnsureDotPrefab()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DotPath);
            if (prefab != null) return prefab;
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            if (mesh == null)
            {
                mesh = CreateHemisphere();
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new System.InvalidOperationException("Board URP/Lit shader is missing.");
                material = new Material(shader) { name = "Key Shop Route Yellow", enableInstancing = true };
                material.SetColor("_BaseColor", new Color(1f, .82f, .02f));
                material.SetFloat("_Smoothness", .35f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(.3f, .21f, 0f));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            var dot = new GameObject("Key Shop Route Hemisphere", typeof(MeshFilter), typeof(MeshRenderer));
            try
            {
                dot.transform.localScale = Vector3.one * .32f;
                dot.GetComponent<MeshFilter>().sharedMesh = mesh;
                var renderer = dot.GetComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return PrefabUtility.SaveAsPrefabAsset(dot, DotPath);
            }
            finally { Object.DestroyImmediate(dot); }
        }

        private static Mesh CreateHemisphere()
        {
            const int sectors = 16, rings = 6;
            var vertices = new List<Vector3>();
            var normals = new List<Vector3>();
            var triangles = new List<int>();
            for (var ring = 0; ring <= rings; ring++)
            {
                var latitude = ring * Mathf.PI * .5f / rings;
                for (var sector = 0; sector <= sectors; sector++)
                {
                    var angle = sector * Mathf.PI * 2f / sectors;
                    var normal = new Vector3(Mathf.Sin(latitude) * Mathf.Cos(angle), Mathf.Cos(latitude), Mathf.Sin(latitude) * Mathf.Sin(angle));
                    vertices.Add(normal * .5f); normals.Add(normal);
                }
            }
            for (var ring = 0; ring < rings; ring++)
            for (var sector = 0; sector < sectors; sector++)
            {
                var a = ring * (sectors + 1) + sector;
                var b = a + sectors + 1;
                triangles.Add(a); triangles.Add(a + 1); triangles.Add(b);
                triangles.Add(a + 1); triangles.Add(b + 1); triangles.Add(b);
            }
            var mesh = new Mesh { name = "Upper Hemisphere" };
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
