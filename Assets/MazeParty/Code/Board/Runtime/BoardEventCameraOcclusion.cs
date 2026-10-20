using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Makes scenery between an event camera and its player transparent for that
    /// camera's render only. Colliders, shared assets and other views are untouched.
    /// </summary>
    public sealed class BoardEventCameraOcclusion : IDisposable
    {
        private const float Opacity = 0.12f;
        private static readonly int[] ColorProperties =
            { Shader.PropertyToID("_BaseColor"), Shader.PropertyToID("_Color") };
        private readonly Dictionary<Camera, Transform> _focus = new Dictionary<Camera, Transform>();
        private readonly Dictionary<Material, Material> _transparent = new Dictionary<Material, Material>();
        private readonly List<RendererState> _changed = new List<RendererState>();
        private Renderer[] _scenery;
        private double _nextScan;
        private Camera _renderingCamera;
        private bool _subscribed;

        public void SetFocus(Camera camera, Transform player)
        {
            if (camera == null) return;
            if (player == null)
            {
                _focus.Remove(camera);
                if (_renderingCamera == camera) Restore();
                return;
            }
            _focus[camera] = player;
            if (_subscribed) return;
            RenderPipelineManager.beginCameraRendering += BeginCameraRendering;
            RenderPipelineManager.endCameraRendering += EndCameraRendering;
            _subscribed = true;
        }

        private void BeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            // Also recovers the state if a camera render was interrupted.
            Restore();
            if (!_focus.TryGetValue(camera, out var player) || player == null) return;
            _renderingCamera = camera;
            if (_scenery == null || Time.unscaledTimeAsDouble >= _nextScan)
            {
                _scenery = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                _nextScan = Time.unscaledTimeAsDouble + 0.5d;
            }
            var origin = camera.transform.position;
            var center = player.position + Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            foreach (var renderer in _scenery)
            {
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                    !renderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))) continue;
                var bounds = renderer.bounds;
                // Flat ground and scenery behind the player cannot hide the portrait.
                if (bounds.max.y < player.position.y - 0.65f) continue;
                bounds.Expand(0.16f);
                if (!Blocks(bounds, origin, center) &&
                    !Blocks(bounds, origin, center - Vector3.up * 0.45f) &&
                    !Blocks(bounds, origin, center - Vector3.up * 1.25f) &&
                    !Blocks(bounds, origin, center + Vector3.up * 0.45f) &&
                    !Blocks(bounds, origin, center + camera.transform.right * 0.3f) &&
                    !Blocks(bounds, origin, center - camera.transform.right * 0.3f)) continue;
                if (renderer.GetComponentInParent<PlayerAvatarVisual>() != null ||
                    renderer.transform.IsChildOf(player) ||
                    renderer.GetComponent<TextMesh>() != null) continue;
                var saved = new RendererState(renderer);
                _changed.Add(saved);
                var materials = (Material[])saved.Materials.Clone();
                var supported = true;
                for (var index = 0; index < materials.Length; index++)
                {
                    if (materials[index] == null) continue;
                    var ghost = TransparentMaterial(materials[index]);
                    if (ghost == null) { supported = false; break; }
                    materials[index] = ghost;
                }
                if (!supported)
                {
                    // Unknown opaque shaders still must not cover the player.
                    renderer.forceRenderingOff = true;
                    continue;
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                ApplyAlpha(renderer, -1);
                for (var index = 0; index < materials.Length; index++) ApplyAlpha(renderer, index);
            }
        }

        private static bool Blocks(Bounds bounds, Vector3 origin, Vector3 target)
        {
            var offset = target - origin;
            return bounds.Contains(origin) || offset.sqrMagnitude > 0.0001f &&
                bounds.IntersectRay(new Ray(origin, offset.normalized), out var distance) &&
                distance < offset.magnitude - 0.05f;
        }

        private Material TransparentMaterial(Material source)
        {
            if (_transparent.TryGetValue(source, out var result)) return result;
            if (!source.HasProperty("_SrcBlend") || !source.HasProperty("_DstBlend") ||
                !source.HasProperty("_ZWrite") ||
                (!source.HasProperty("_BaseColor") && !source.HasProperty("_Color"))) return null;
            result = new Material(source)
            {
                name = source.name + " (Event camera transparency)",
                hideFlags = HideFlags.HideAndDontSave,
                renderQueue = (int)RenderQueue.Transparent
            };
            if (result.HasProperty("_Surface")) result.SetFloat("_Surface", 1f);
            if (result.HasProperty("_AlphaClip")) result.SetFloat("_AlphaClip", 0f);
            result.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            result.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            result.SetFloat("_ZWrite", 0f);
            result.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            result.DisableKeyword("_ALPHATEST_ON");
            result.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            result.SetOverrideTag("RenderType", "Transparent");
            result.SetShaderPassEnabled("ShadowCaster", false);
            result.SetShaderPassEnabled("DepthOnly", false);
            result.SetShaderPassEnabled("DepthNormals", false);
            foreach (var property in ColorProperties)
            {
                if (!result.HasProperty(property)) continue;
                var color = result.GetColor(property);
                color.a *= Opacity;
                result.SetColor(property, color);
            }
            _transparent.Add(source, result);
            return result;
        }

        private static void ApplyAlpha(Renderer renderer, int materialIndex)
        {
            var block = new MaterialPropertyBlock();
            if (materialIndex < 0) renderer.GetPropertyBlock(block);
            else renderer.GetPropertyBlock(block, materialIndex);
            if (block.isEmpty) return;
            foreach (var property in ColorProperties)
            {
                if (!block.HasColor(property)) continue;
                var color = block.GetColor(property);
                color.a *= Opacity;
                block.SetColor(property, color);
            }
            if (materialIndex < 0) renderer.SetPropertyBlock(block);
            else renderer.SetPropertyBlock(block, materialIndex);
        }

        private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_renderingCamera == camera) Restore();
        }

        private void Restore()
        {
            foreach (var state in _changed) state.Restore();
            _changed.Clear();
            _renderingCamera = null;
        }

        public void Dispose()
        {
            if (_subscribed)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCameraRendering;
                RenderPipelineManager.endCameraRendering -= EndCameraRendering;
            }
            _subscribed = false;
            Restore();
            _focus.Clear();
            _scenery = null;
            foreach (var material in _transparent.Values)
            {
                if (Application.isPlaying) UnityEngine.Object.Destroy(material);
                else UnityEngine.Object.DestroyImmediate(material);
            }
            _transparent.Clear();
        }

        private sealed class RendererState
        {
            private readonly Renderer _renderer;
            public readonly Material[] Materials;
            private readonly ShadowCastingMode _shadows;
            private readonly bool _renderingOff;
            private readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
            private readonly MaterialPropertyBlock[] _materialBlocks;

            public RendererState(Renderer renderer)
            {
                _renderer = renderer;
                Materials = renderer.sharedMaterials;
                _shadows = renderer.shadowCastingMode;
                _renderingOff = renderer.forceRenderingOff;
                renderer.GetPropertyBlock(_block);
                _materialBlocks = new MaterialPropertyBlock[Materials.Length];
                for (var index = 0; index < Materials.Length; index++)
                {
                    _materialBlocks[index] = new MaterialPropertyBlock();
                    renderer.GetPropertyBlock(_materialBlocks[index], index);
                }
            }

            public void Restore()
            {
                if (_renderer == null) return;
                _renderer.sharedMaterials = Materials;
                _renderer.shadowCastingMode = _shadows;
                _renderer.forceRenderingOff = _renderingOff;
                _renderer.SetPropertyBlock(_block.isEmpty ? null : _block);
                for (var index = 0; index < _materialBlocks.Length; index++)
                    _renderer.SetPropertyBlock(_materialBlocks[index].isEmpty ? null : _materialBlocks[index], index);
            }
        }
    }
}
