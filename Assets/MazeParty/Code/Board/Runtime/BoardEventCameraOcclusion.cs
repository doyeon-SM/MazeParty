using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace MazeParty.Gameplay
{
    /// <summary>Hides obstructing scenery only while an event camera renders.</summary>
    public sealed class BoardEventCameraOcclusion : IDisposable
    {
        private readonly Dictionary<Camera, Transform> _focus = new Dictionary<Camera, Transform>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<TerrainState> _terrainChanges = new List<TerrainState>();
        private readonly Vector3[] _targets = new Vector3[7];
        private Renderer[] _scenery;
        private Terrain[] _terrains;
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
            Restore(); // Also restores a render interrupted before its end callback.
            if (!_focus.TryGetValue(camera, out var player) || player == null) return;
            _renderingCamera = camera;
            if (_scenery == null || Time.unscaledTimeAsDouble >= _nextScan)
            {
                _scenery = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                _terrains = Terrain.activeTerrains;
                _nextScan = Time.unscaledTimeAsDouble + 0.5d;
            }
            var origin = camera.transform.position;
            var center = player.position + Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            _targets[0] = center;
            _targets[1] = center - Vector3.up * 0.45f;
            _targets[2] = center - Vector3.up * 1.25f;
            _targets[3] = center + Vector3.up * 0.45f;
            _targets[4] = center + camera.transform.right * 0.45f;
            _targets[5] = center - camera.transform.right * 0.45f;
            _targets[6] = center + Vector3.up * 1.1f;
            var visual = player.GetComponent<PlayerAvatarVisual>();
            if (visual != null && visual.TryGetHeadViewportTop(camera, out var top))
            {
                var viewport = camera.WorldToViewportPoint(center);
                if (viewport.z > camera.nearClipPlane)
                {
                    viewport.y = top + 0.1f;
                    _targets[6] = camera.ViewportToWorldPoint(viewport);
                }
            }
            foreach (var renderer in _scenery)
            {
                if (renderer == null || !renderer.enabled || renderer.forceRenderingOff ||
                    !renderer.gameObject.activeInHierarchy ||
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0 ||
                    (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))) continue;
                if (renderer.GetComponentInParent<PlayerAvatarVisual>() != null ||
                    renderer.transform.IsChildOf(player) || renderer.GetComponent<TextMesh>() != null) continue;
                var bounds = renderer.bounds;
                if (bounds.max.y < player.position.y - 0.65f) continue;
                bounds.Expand(0.16f);
                if (!BlocksPortrait(bounds, origin)) continue;
                // Opaque, alpha-clipped and custom foliage shaders all obey this.
                // Materials, colliders and the other cameras retain their normal state.
                _hidden.Add(renderer);
                renderer.forceRenderingOff = true;
            }
            foreach (var terrain in _terrains)
            {
                if (terrain == null || !terrain.enabled || !terrain.gameObject.activeInHierarchy ||
                    terrain.terrainData == null || (camera.cullingMask & (1 << terrain.gameObject.layer)) == 0) continue;
                var size = terrain.terrainData.size;
                var footprint = new Bounds(terrain.transform.position + size * 0.5f,
                    new Vector3(size.x, 20000f, size.z));
                if (!BlocksPortrait(footprint, origin)) continue;
                // Terrain vegetation is batched internally and has no Renderer to
                // hide individually. Suppress it only in this close-up camera.
                _terrainChanges.Add(new TerrainState(terrain));
                terrain.drawTreesAndFoliage = false;
                if (TerrainBlocksPortrait(terrain, origin)) terrain.drawHeightmap = false;
            }
        }

        private bool BlocksPortrait(Bounds bounds, Vector3 origin)
        {
            foreach (var target in _targets) if (Blocks(bounds, origin, target)) return true;
            return false;
        }

        private bool TerrainBlocksPortrait(Terrain terrain, Vector3 origin)
        {
            var collider = terrain.GetComponent<TerrainCollider>();
            var basePosition = terrain.transform.position;
            var size = terrain.terrainData.size;
            foreach (var target in _targets)
            {
                var offset = target - origin;
                if (collider != null && collider.enabled && offset.sqrMagnitude > 0.0001f &&
                    collider.Raycast(new Ray(origin, offset.normalized), out _, Mathf.Max(0f, offset.magnitude - 0.05f))) return true;
                // Raycasts cannot detect a camera already inside the hillside.
                // Height samples also cover authored Terrain without a collider.
                for (var step = 0; step < 16; step++)
                {
                    var point = Vector3.Lerp(origin, target, step / 16f) - basePosition;
                    if (point.x < 0f || point.z < 0f || point.x > size.x || point.z > size.z) continue;
                    if (point.y < terrain.terrainData.GetInterpolatedHeight(point.x / size.x, point.z / size.z)) return true;
                }
            }
            return false;
        }

        private static bool Blocks(Bounds bounds, Vector3 origin, Vector3 target)
        {
            var offset = target - origin;
            return bounds.Contains(origin) || offset.sqrMagnitude > 0.0001f &&
                bounds.IntersectRay(new Ray(origin, offset.normalized), out var distance) &&
                distance < offset.magnitude - 0.05f;
        }

        private void EndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_renderingCamera == camera) Restore();
        }

        private void Restore()
        {
            foreach (var renderer in _hidden) if (renderer != null) renderer.forceRenderingOff = false;
            _hidden.Clear();
            foreach (var state in _terrainChanges) state.Restore();
            _terrainChanges.Clear();
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
            _terrains = null;
        }

        private readonly struct TerrainState
        {
            private readonly Terrain _terrain;
            private readonly bool _heightmap, _foliage;
            public TerrainState(Terrain terrain)
            {
                _terrain = terrain;
                _heightmap = terrain.drawHeightmap;
                _foliage = terrain.drawTreesAndFoliage;
            }
            public void Restore()
            {
                if (_terrain == null) return;
                _terrain.drawHeightmap = _heightmap;
                _terrain.drawTreesAndFoliage = _foliage;
            }
        }
    }
}
