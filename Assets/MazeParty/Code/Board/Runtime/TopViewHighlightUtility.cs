using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Creates a build-safe white ground outline without relying on a runtime-only
    /// shader lookup. Four thin cubes remain crisp from the board top view and use
    /// the same Resources material that is already included in player builds.
    /// </summary>
    public static class TopViewHighlightUtility
    {
        public static GameObject CreateSquareOutline(
            Transform parent,
            string name,
            float halfExtent,
            float lineWidth,
            float localY)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = new Vector3(0f, localY, 0f);

            var extent = Mathf.Max(0.1f, halfExtent);
            var width = Mathf.Max(0.02f, lineWidth);
            CreateLine(root.transform, "North", new Vector3(0f, 0f, extent),
                new Vector3(extent * 2f + width, 0.025f, width));
            CreateLine(root.transform, "South", new Vector3(0f, 0f, -extent),
                new Vector3(extent * 2f + width, 0.025f, width));
            CreateLine(root.transform, "East", new Vector3(extent, 0f, 0f),
                new Vector3(width, 0.025f, extent * 2f + width));
            CreateLine(root.transform, "West", new Vector3(-extent, 0f, 0f),
                new Vector3(width, 0.025f, extent * 2f + width));
            return root;
        }

        private static void CreateLine(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 localScale)
        {
            var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
            line.name = name;
            line.transform.SetParent(parent, false);
            line.transform.localPosition = localPosition;
            line.transform.localScale = localScale;

            var collider = line.GetComponent<Collider>();
            if (collider != null)
            {
                if (Application.isPlaying)
                {
                    Object.Destroy(collider);
                }
                else
                {
                    Object.DestroyImmediate(collider);
                }
            }

            var renderer = line.GetComponent<Renderer>();
            WorldTextOcclusion.ApplyBuildSafeSurface(renderer);
            if (renderer != null)
            {
                var properties = new MaterialPropertyBlock();
                properties.SetColor("_BaseColor", Color.white);
                properties.SetColor("_Color", Color.white);
                properties.SetColor("_EmissionColor", Color.white * 0.35f);
                renderer.SetPropertyBlock(properties);
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }
    }
}
