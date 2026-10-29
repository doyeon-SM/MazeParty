using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace MazeParty.Multiplayer
{
    // Lives below the minigame's authored presentation root. Additive scene loading
    // does not switch RenderSettings, so scope the night environment to its display.
    [DefaultExecutionOrder(1200)]
    public sealed class MinigameNightLighting : MonoBehaviour
    {
        [SerializeField] private Material skybox;
        [SerializeField] private Light moonlight;
        [SerializeField] private Volume colorGrade;
        [SerializeField] private float ambientIntensity = 0.16f;
        [SerializeField] private float reflectionIntensity = 0.22f;

        private static MinigameNightLighting _owner;
        private readonly List<Light> _suppressedLights = new List<Light>();
        private Material _previousSkybox;
        private Light _previousSun;
        private AmbientMode _previousAmbientMode;
        private float _previousAmbient, _previousReflection;
        private Camera _camera;
        private UniversalAdditionalCameraData _cameraData;
        private bool _previousPostProcessing;
        private LayerMask _previousVolumeMask;
        private CameraClearFlags _previousClearFlags;

        public Light Moonlight => moonlight;
        public Material Skybox => skybox;
        public Volume ColorGrade => colorGrade;
        public bool HasRequiredReferences => skybox != null && moonlight != null &&
            colorGrade != null && colorGrade.sharedProfile != null;

        public void Configure(Material material, Light light, Volume volume)
        { skybox = material; moonlight = light; colorGrade = volume; }

        private void LateUpdate()
        {
            var match = NetworkMatchState.Instance;
            var selected = match == null || !match.IsSpawned ||
                (match.GameplayEnabled && MinigameCatalog.IsRegistered(match.CurrentMinigame) &&
                 MinigameCatalog.GetSceneName(match.CurrentMinigame) == gameObject.scene.name &&
                 (match.FlowState == BoardFlowState.MinigamePlaying ||
                  match.FlowState == BoardFlowState.MinigameResult));
            RefreshPresentation(selected, ResolveOutputCamera());
        }

        private Camera ResolveOutputCamera()
        {
            var main = Camera.main;
            if (main != null) return main;
            if (_camera != null && _camera.isActiveAndEnabled &&
                _camera.gameObject.scene == gameObject.scene) return _camera;
            // Some solo harnesses use an untagged output camera.
            foreach (var camera in Camera.allCameras)
                if (camera.cameraType == CameraType.Game && camera.gameObject.scene == gameObject.scene)
                    return camera;
            return null;
        }

        internal void RefreshPresentation(bool visible, Camera output)
        {
            if (!visible || !HasRequiredReferences)
            {
                Release();
                if (colorGrade != null) colorGrade.enabled = false;
                return;
            }
            if (_owner != this)
            {
                if (_owner != null) _owner.Release();
                _previousSkybox = RenderSettings.skybox;
                _previousSun = RenderSettings.sun;
                _previousAmbientMode = RenderSettings.ambientMode;
                _previousAmbient = RenderSettings.ambientIntensity;
                _previousReflection = RenderSettings.reflectionIntensity;
                foreach (var light in FindObjectsByType<Light>(
                             FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (light != moonlight && light.enabled && light.type == LightType.Directional)
                    { _suppressedLights.Add(light); light.enabled = false; }
                }
                _owner = this;
                RenderSettings.skybox = skybox;
                RenderSettings.sun = moonlight;
                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientIntensity = ambientIntensity;
                RenderSettings.reflectionIntensity = reflectionIntensity;
                DynamicGI.UpdateEnvironment();
            }
            colorGrade.enabled = true;
            if (_camera != output)
            {
                RestoreCamera();
                _camera = output;
                if (_camera != null)
                {
                    _cameraData = _camera.GetUniversalAdditionalCameraData();
                    _previousPostProcessing = _cameraData.renderPostProcessing;
                    _previousVolumeMask = _cameraData.volumeLayerMask;
                    _previousClearFlags = _camera.clearFlags;
                    _cameraData.renderPostProcessing = true;
                    _cameraData.volumeLayerMask |= 1 << colorGrade.gameObject.layer;
                    _camera.clearFlags = CameraClearFlags.Skybox;
                }
            }
        }

        private void OnDisable() => Release();

        private void Release()
        {
            if (_owner != this) return;
            RestoreCamera();
            RenderSettings.skybox = _previousSkybox;
            RenderSettings.sun = _previousSun;
            RenderSettings.ambientMode = _previousAmbientMode;
            RenderSettings.ambientIntensity = _previousAmbient;
            RenderSettings.reflectionIntensity = _previousReflection;
            foreach (var light in _suppressedLights)
                if (light != null) light.enabled = true;
            _suppressedLights.Clear();
            if (colorGrade != null) colorGrade.enabled = false;
            _owner = null;
            DynamicGI.UpdateEnvironment();
        }

        private void RestoreCamera()
        {
            if (_camera != null) _camera.clearFlags = _previousClearFlags;
            if (_cameraData != null)
            {
                _cameraData.renderPostProcessing = _previousPostProcessing;
                _cameraData.volumeLayerMask = _previousVolumeMask;
            }
            _camera = null;
            _cameraData = null;
        }
    }
}
