using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Gives legacy world-space TextMesh labels a build-included URP shader with
    /// normal depth testing. Screen-space UI is intentionally unaffected.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TextMesh))]
    public sealed class WorldTextOcclusion : MonoBehaviour
    {
        public const string SurfaceMaterialResource =
            "MazeParty/Materials/LobbySurface";
        public const string TextShaderResource =
            "MazeParty/Shaders/WorldTextOccluded";

        private Material _runtimeMaterial;

        private void Awake()
        {
            RefreshMaterial();
        }

        private void OnDestroy()
        {
            if (_runtimeMaterial == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(_runtimeMaterial);
            }
            else
            {
                DestroyImmediate(_runtimeMaterial);
            }
        }

        public void RefreshMaterial()
        {
            if (_runtimeMaterial != null)
            {
                return;
            }

            var textMesh = GetComponent<TextMesh>();
            var renderer = GetComponent<MeshRenderer>();
            var shader = Resources.Load<Shader>(TextShaderResource);
            if (textMesh == null || renderer == null || shader == null)
            {
                return;
            }

            _runtimeMaterial = new Material(shader)
            {
                name = gameObject.name + " Occluded Text (Runtime)",
                hideFlags = HideFlags.HideAndDontSave
            };
            if (textMesh.font != null && textMesh.font.material != null)
            {
                _runtimeMaterial.mainTexture = textMesh.font.material.mainTexture;
            }
            _runtimeMaterial.color = Color.white;
            renderer.sharedMaterial = _runtimeMaterial;
        }

        public static void Apply(TextMesh textMesh)
        {
            if (textMesh == null)
            {
                return;
            }

            var occlusion = textMesh.GetComponent<WorldTextOcclusion>();
            if (occlusion == null)
            {
                occlusion = textMesh.gameObject.AddComponent<WorldTextOcclusion>();
            }
            occlusion.RefreshMaterial();
        }

        public static void ApplyBuildSafeSurface(Renderer renderer)
        {
            if (renderer == null)
            {
                return;
            }

            var material = Resources.Load<Material>(SurfaceMaterialResource);
            if (material != null)
            {
                renderer.sharedMaterial = material;
            }
        }

        public static Material CreateBuildSafeLitMaterial(string name)
        {
            var template = Resources.Load<Material>(SurfaceMaterialResource);
            if (template != null)
            {
                return new Material(template) { name = name };
            }

            var shader = Shader.Find("Universal Render Pipeline/Lit") ??
                         Shader.Find("Standard");
            return shader != null ? new Material(shader) { name = name } : null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void InstallSceneHook()
        {
            SceneManager.sceneLoaded -= ApplyToLoadedScene;
            SceneManager.sceneLoaded += ApplyToLoadedScene;

            for (var index = 0; index < SceneManager.sceneCount; index++)
            {
                ApplyToScene(SceneManager.GetSceneAt(index));
            }
        }

        private static void ApplyToLoadedScene(Scene scene, LoadSceneMode _)
        {
            ApplyToScene(scene);
        }

        private static void ApplyToScene(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded)
            {
                return;
            }

            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0; rootIndex < roots.Length; rootIndex++)
            {
                var labels = roots[rootIndex].GetComponentsInChildren<TextMesh>(true);
                for (var labelIndex = 0; labelIndex < labels.Length; labelIndex++)
                {
                    Apply(labels[labelIndex]);
                }
            }
        }
    }
}
