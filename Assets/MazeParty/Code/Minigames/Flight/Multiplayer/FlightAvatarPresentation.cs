using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.Rendering;

namespace MazeParty.Multiplayer
{
    // Apply after the shared avatar's idle pose and body-colour property block.
    [DefaultExecutionOrder(1200)]
    public sealed class FlightAvatarPresentation : MonoBehaviour
    {
        private readonly Dictionary<Material, Material> _ghosts = new Dictionary<Material, Material>();
        private readonly Dictionary<Material, Material> _originals = new Dictionary<Material, Material>();
        private static readonly string[] ColorProperties = { "_BaseColor", "_Color" };
        private MaterialPropertyBlock _block;
        private PlayerAvatarVisual _visual;
        private Renderer[] _renderers;
        private Vector3 _leftHome, _rightHome;
        private int _flaps;
        private double _flappedAt = -10;
        private bool _ghost;
        public void Initialize(PlayerAvatarVisual visual, bool ghost)
        {
            _block = new MaterialPropertyBlock();
            _visual = visual; _ghost = ghost;
            _leftHome = visual.Bindings.LeftHandAnchor.localPosition;
            _rightHome = visual.Bindings.RightHandAnchor.localPosition;
            _renderers = visual.Bindings.WorldModel.GetComponentsInChildren<Renderer>(true);
        }
        public void SetGhost(bool ghost)
        {
            if (_ghost == ghost) return;
            _ghost = ghost;
            if (ghost) return;
            foreach (var renderer in _renderers)
            {
                if (renderer == null) continue;
                var materials = renderer.sharedMaterials;
                for (var i = 0; i < materials.Length; i++)
                    if (materials[i] != null && _originals.TryGetValue(materials[i], out var original)) materials[i] = original;
                renderer.sharedMaterials = materials;
                renderer.GetPropertyBlock(_block);
                foreach (var property in ColorProperties)
                    if (_block.HasColor(property)) { var c = _block.GetColor(property); c.a = 1; _block.SetColor(property, c); }
                renderer.SetPropertyBlock(_block);
                if (renderer is SpriteRenderer sprite) { var color = sprite.color; color.a = 1; sprite.color = color; }
            }
        }
        public void SetFlaps(int count)
        {
            if (count > _flaps) _flappedAt = Time.unscaledTimeAsDouble;
            _flaps = count;
        }
        private void LateUpdate()
        {
            if (_visual == null) return;
            var age = (float)(Time.unscaledTimeAsDouble - _flappedAt);
            var stroke = age < .3f ? Mathf.Sin(Mathf.Clamp01(age / .3f) * Mathf.PI) : 0f;
            _visual.Bindings.LeftHandAnchor.localPosition = _leftHome + new Vector3(-.12f, .18f - stroke * .48f, 0);
            _visual.Bindings.RightHandAnchor.localPosition = _rightHome + new Vector3(.12f, .18f - stroke * .48f, 0);
            if (!_ghost) return;
            // Appearance changes may swap a hat/material, so resolve those as well.
            foreach (var renderer in _renderers)
            {
                if (renderer == null || !renderer.gameObject.activeInHierarchy) continue;
                var materials = renderer.sharedMaterials; var changed = false;
                for (var i = 0; i < materials.Length; i++)
                {
                    var source = materials[i]; if (source == null) continue;
                    if (!_ghosts.TryGetValue(source, out var ghost))
                    {
                        ghost = new Material(source) { name = source.name + " (Flight Ghost)", renderQueue = (int)RenderQueue.Transparent };
                        if (ghost.HasProperty("_Surface")) ghost.SetFloat("_Surface", 1);
                        if (ghost.HasProperty("_SrcBlend")) ghost.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                        if (ghost.HasProperty("_DstBlend")) ghost.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                        if (ghost.HasProperty("_ZWrite")) ghost.SetFloat("_ZWrite", 0);
                        ghost.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        ghost.SetOverrideTag("RenderType", "Transparent");
                        ghost.SetShaderPassEnabled("ShadowCaster", false);
                        ghost.SetShaderPassEnabled("DepthOnly", false);
                        ghost.SetShaderPassEnabled("DepthNormals", false);
                        foreach (var property in ColorProperties)
                            if (ghost.HasProperty(property)) { var c = ghost.GetColor(property); c.a = .5f; ghost.SetColor(property, c); }
                        _ghosts[source] = ghost; _ghosts[ghost] = ghost;
                        _originals[ghost] = source;
                    }
                    if (materials[i] != ghost) { materials[i] = ghost; changed = true; }
                }
                if (changed) renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.GetPropertyBlock(_block);
                foreach (var property in ColorProperties)
                    if (_block.HasColor(property)) { var c = _block.GetColor(property); c.a = .5f; _block.SetColor(property, c); }
                renderer.SetPropertyBlock(_block);
                if (renderer is SpriteRenderer sprite) { var color = sprite.color; color.a = .5f; sprite.color = color; }
            }
        }
        private void OnDestroy()
        {
            foreach (var pair in _ghosts) if (pair.Key == pair.Value && pair.Value != null) Destroy(pair.Value);
            _ghosts.Clear();
            _originals.Clear();
        }
    }
}
