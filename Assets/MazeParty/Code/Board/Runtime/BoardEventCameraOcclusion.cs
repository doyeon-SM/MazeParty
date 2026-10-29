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
        private readonly HashSet<Camera> _isolatedPlayers = new HashSet<Camera>();
        private readonly List<Renderer> _hidden = new List<Renderer>();
        private readonly List<TerrainState> _terrainChanges = new List<TerrainState>();
        private readonly Dictionary<Terrain, TerrainState> _treeViews = new Dictionary<Terrain, TerrainState>();
        private readonly Vector3[] _targets = new Vector3[7];
        private Renderer[] _scenery;
        private Terrain[] _terrains;
        private double _nextScan;
        private Camera _renderingCamera;
        private bool _subscribed;

        public void SetFocus(Camera camera, Transform player, bool isolatePlayers = false)
        {
            if (camera == null) return;
            if (player == null)
            {
                _focus.Remove(camera);
                _isolatedPlayers.Remove(camera);
                if (_renderingCamera == camera) Restore();
                return;
            }
            _focus[camera] = player;
            if (isolatePlayers) _isolatedPlayers.Add(camera);
            else _isolatedPlayers.Remove(camera);
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
                    (camera.cullingMask & (1 << renderer.gameObject.layer)) == 0) continue;
                var avatar = renderer.GetComponentInParent<PlayerAvatarVisual>();
                if (avatar != null)
                {
                    // During simultaneous retreats each panel identifies one loser.
                    // Other players must not block that portrait along a shared route.
                    if (_isolatedPlayers.Contains(camera) && !renderer.transform.IsChildOf(player))
                    {
                        _hidden.Add(renderer);
                        renderer.forceRenderingOff = true;
                    }
                    continue;
                }
                if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) continue;
                if (renderer.transform.IsChildOf(player) || renderer.GetComponent<TextMesh>() != null ||
                    renderer.GetComponentInParent<BoardBoundaryWallVisual>() != null) continue;
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
                if (terrain.terrainData.treeInstanceCount == 0) continue;
                if (!_treeViews.TryGetValue(terrain, out var state))
                {
                    state = new TerrainState(terrain);
                    _treeViews.Add(terrain, state);
                }
                if (state.HideBlockingTrees(bounds => BlocksPortrait(bounds, origin)))
                    _terrainChanges.Add(state);
            }
        }

        private bool BlocksPortrait(Bounds bounds, Vector3 origin)
        {
            foreach (var target in _targets) if (Blocks(bounds, origin, target)) return true;
            return false;
        }

        // Raise the camera, never the player or an entire terrain tile.
        public static Vector3 ClearTerrainSightLine(Vector3 camera, Vector3 target)
        {
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain == null || !terrain.enabled || terrain.terrainData == null) continue;
                var size = terrain.terrainData.size;
                for (var step = 0; step < 15; step++)
                {
                    var t = step / 16f;
                    var point = Vector3.Lerp(camera, target, t);
                    var local = point - terrain.transform.position;
                    if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) continue;
                    var minimum = terrain.SampleHeight(point) + terrain.transform.position.y + .3f;
                    if (point.y < minimum) camera.y += (minimum - point.y) / (1f - t);
                }
            }
            return camera;
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
            _isolatedPlayers.Clear();
            _scenery = null;
            _terrains = null;
            foreach (var state in _treeViews.Values) state.Dispose();
            _treeViews.Clear();
        }

        private sealed class TerrainState : IDisposable
        {
            private readonly Terrain _terrain;
            private readonly TerrainData _original;
            private readonly TreeInstance[] _trees;
            private readonly Bounds[] _bounds;
            private readonly bool[] _hidden;
            private TerrainData _view;
            public TerrainState(Terrain terrain)
            {
                _terrain = terrain;
                _original = terrain.terrainData;
                _trees = _original.treeInstances;
                _bounds = new Bounds[_trees.Length];
                _hidden = new bool[_trees.Length];
                var prototypes = _original.treePrototypes;
                for (var i = 0; i < _trees.Length; i++)
                {
                    var tree = _trees[i];
                    var prefab = prototypes[tree.prototypeIndex].prefab;
                    var matrix = Matrix4x4.TRS(terrain.transform.position + Vector3.Scale(tree.position, _original.size),
                        Quaternion.Euler(0f, tree.rotation * Mathf.Rad2Deg, 0f),
                        new Vector3(tree.widthScale, tree.heightScale, tree.widthScale));
                    var first = true;
                    foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
                    {
                        var box = renderer.localBounds;
                        var relative = prefab.transform.worldToLocalMatrix * renderer.transform.localToWorldMatrix;
                        for (var corner = 0; corner < 8; corner++)
                        {
                            var point = box.center + Vector3.Scale(box.extents, new Vector3(
                                (corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                            point = matrix.MultiplyPoint3x4(relative.MultiplyPoint3x4(point));
                            if (first) { _bounds[i] = new Bounds(point, Vector3.zero); first = false; }
                            else _bounds[i].Encapsulate(point);
                        }
                    }
                }
            }
            public bool HideBlockingTrees(Func<Bounds, bool> blocks)
            {
                var changed = false;
                var any = false;
                for (var i = 0; i < _trees.Length; i++)
                {
                    var hide = blocks(_bounds[i]);
                    changed |= hide != _hidden[i];
                    _hidden[i] = hide;
                    any |= hide;
                }
                if (!any) return false;
                if (_view == null)
                {
                    _view = UnityEngine.Object.Instantiate(_original);
                    _view.hideFlags = HideFlags.HideAndDontSave;
                    changed = true;
                }
                if (changed)
                {
                    var trees = (TreeInstance[])_trees.Clone();
                    for (var i = 0; i < trees.Length; i++)
                        if (_hidden[i]) { trees[i].widthScale = 0f; trees[i].heightScale = 0f; }
                    _view.SetTreeInstances(trees, false);
                }
                // Only this render uses the temporary tree view. TerrainCollider and
                // the authored asset keep their original data and all background trees.
                _terrain.terrainData = _view;
                return true;
            }
            public void Restore()
            {
                if (_terrain != null && _terrain.terrainData == _view) _terrain.terrainData = _original;
            }
            public void Dispose()
            {
                Restore();
                if (_view == null) return;
                if (Application.isPlaying) UnityEngine.Object.Destroy(_view);
                else UnityEngine.Object.DestroyImmediate(_view);
            }
        }
    }
}
