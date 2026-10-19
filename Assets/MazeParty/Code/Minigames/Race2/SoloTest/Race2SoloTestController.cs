using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.Race2;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    public sealed class Race2SoloTestController : MonoBehaviour
    {
        private MinigameSoloHudView _hud;
        private Race2NetworkView _view;
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<AudioListener> _disabledListeners = new List<AudioListener>();
        private Camera _camera;
        private int _seed, _plannedRound;
        private readonly int[] _lastBotBox = new int[4];
        private readonly double[] _botAt = new double[4];
        private System.Random _random;
        public Race2MatchState Match { get; private set; }
        public Camera RuntimeCamera => _camera;
        public bool IsInitialized => Match != null;
        public int Seed => _seed;
        public void ConfigureHud(MinigameSoloHudView hud) { _hud = hud; }
        public void Begin(int seed)
        {
            _view = FindAnyObjectByType<Race2NetworkView>(FindObjectsInactive.Include);
            if (_view == null || !_view.HasRequiredReferences) throw new InvalidOperationException("Race2 scene is incomplete.");
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (camera.enabled) { _disabledCameras.Add(camera); camera.enabled = false; }
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (listener.enabled) { _disabledListeners.Add(listener); listener.enabled = false; }
            var cameraRoot = new GameObject("Race 2 Solo Camera"); cameraRoot.tag = "MainCamera"; cameraRoot.transform.SetParent(transform);
            _camera = cameraRoot.AddComponent<Camera>(); _camera.orthographic = false;
            _camera.fieldOfView = Race2NetworkView.PlayerCameraFieldOfView; _camera.farClipPlane = 500f;
            cameraRoot.AddComponent<AudioListener>();
            // Race2 deliberately shows only the authored common time/round HUD. Developer shortcuts still work.
            if (_hud != null) _hud.gameObject.SetActive(false);
            BeginMatch(seed);
        }
        private void BeginMatch(int seed)
        {
            _seed = seed; _random = new System.Random(seed); Match = new Race2MatchState(unchecked((ulong)seed));
            _plannedRound = 0; _view.ResetPracticePresentation(); Render();
        }
        private void Update()
        {
            if (Match == null) return;
            var keys = Keyboard.current;
            if (keys != null)
            {
                if (keys.escapeKey.wasPressedThisFrame) { StopSoloTest(); return; }
                if (keys.rKey.wasPressedThisFrame) { RestartMatch(); return; }
                if (keys.nKey.wasPressedThisFrame) { StartNextSeed(); return; }
            }
            Match.AdvanceTo(Match.Elapsed + Math.Max(0d, Time.unscaledDeltaTime));
            if (Match.Phase == Race2Phase.Running)
            {
                if (keys != null && keys.aKey.wasPressedThisFrame != keys.dKey.wasPressedThisFrame &&
                    Match.TrySetLane(0, keys.dKey.wasPressedThisFrame ? (byte)1 : (byte)0)) GameSound.Play(SoundKeys.MinigameRace2Lane);
                UpdateBots();
            }
            Render();
        }
        private void UpdateBots()
        {
            if (_plannedRound != Match.RoundNumber)
            {
                _plannedRound = Match.RoundNumber;
                for (var i = 1; i < 4; i++) { _lastBotBox[i] = -1; _botAt[i] = 0d; }
            }
            var nearest = -1; var distance = double.MaxValue;
            for (var b = 0; b < Race2Rules.BoxCount; b++)
            {
                Match.Boxes.Box(b, Match.RoundElapsed, out var sequence, out var z);
                if (z > Race2Rules.CollisionHalfDepth && z < distance) { nearest = sequence; distance = z; }
            }
            for (var i = 1; i < 4; i++)
            {
                if (!Match.IsAlive(i) || nearest < 0) continue;
                if (_lastBotBox[i] != nearest)
                {
                    _lastBotBox[i] = nearest;
                    _botAt[i] = Match.RoundElapsed + .08d + _random.NextDouble() * .22d;
                    if (_random.NextDouble() < .035d * i) _botAt[i] = double.PositiveInfinity;
                }
                if (Match.RoundElapsed >= _botAt[i]) Match.TrySetLane(i, (byte)(1 - Match.Boxes.Lane(nearest)));
            }
        }
        private void Render()
        {
            _view.RenderPractice(Match);
            _camera.fieldOfView = _view.CameraFieldOfView;
            _camera.transform.SetPositionAndRotation(_view.CameraPosition, _view.CameraRotation);
        }
        public void RestartMatch() { BeginMatch(_seed); }
        public void StartNextSeed() { BeginMatch(unchecked(_seed + 1)); }
        public void StopSoloTest()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }
        private void OnDestroy()
        {
            foreach (var camera in _disabledCameras) if (camera != null) camera.enabled = true;
            foreach (var listener in _disabledListeners) if (listener != null) listener.enabled = true;
        }
    }
}
