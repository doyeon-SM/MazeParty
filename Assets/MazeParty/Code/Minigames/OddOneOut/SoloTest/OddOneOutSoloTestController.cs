using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.OddOneOut;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    public sealed class OddOneOutSoloTestController : MonoBehaviour
    {
        private MinigameSoloHudView _hud;
        private OddOneOutNetworkView _view;
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<AudioListener> _disabledListeners = new List<AudioListener>();
        private OddOneOutMatchState _match;
        private Camera _camera;
        private int _seed;
        public OddOneOutMatchState Match => _match;
        public Camera RuntimeCamera => _camera;
        public bool IsInitialized => _match != null;
        public int Seed => _seed;
        public void ConfigureHud(MinigameSoloHudView hud) { _hud = hud; }
        public void Begin(int seed)
        {
            if (_hud == null || !_hud.HasRequiredReferences) throw new InvalidOperationException("Authored solo HUD required.");
            _view = FindAnyObjectByType<OddOneOutNetworkView>(FindObjectsInactive.Include);
            if (_view == null || !_view.HasRequiredReferences) throw new InvalidOperationException("OddOneOut scene is incomplete.");
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (camera.enabled) { _disabledCameras.Add(camera); camera.enabled = false; }
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener.enabled) { _disabledListeners.Add(listener); listener.enabled = false; }
            var cameraRoot = new GameObject("Odd One Out Solo Camera"); cameraRoot.tag = "MainCamera"; cameraRoot.transform.SetParent(transform);
            cameraRoot.transform.SetPositionAndRotation(_view.CameraPosition, _view.CameraRotation);
            _camera = cameraRoot.AddComponent<Camera>(); _camera.orthographic = true;
            _camera.orthographicSize = _view.CameraSize; _camera.farClipPlane = 80f;
            cameraRoot.AddComponent<AudioListener>();
            _hud.BindActions(RestartMatch, StartNextSeed, StopSoloTest);
            BeginMatch(seed);
        }

        private readonly double[] _botTimes = new double[4];
        private readonly bool[] _botChoices = new bool[4];
        private System.Random _random;
        private int _plannedRound;
        private void BeginMatch(int seed)
        {
            _seed = seed; _random = new System.Random(seed); _match = new OddOneOutMatchState();
            _plannedRound = 0; PlanBots(); _view.ResetPracticePresentation(); _view.RenderPractice(_match);
        }
        private void PlanBots()
        {
            if (_plannedRound == _match.RoundNumber) return;
            _plannedRound = _match.RoundNumber;
            for (var slot = 1; slot < 4; slot++)
            {
                _botTimes[slot] = _match.Elapsed + .3d + _random.NextDouble() * 2.3d;
                _botChoices[slot] = _random.Next(2) == 1;
            }
        }
        private void Update()
        {
            if (_match == null) return;
            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.escapeKey.wasPressedThisFrame) { StopSoloTest(); return; }
                if (keyboard.rKey.wasPressedThisFrame) { RestartMatch(); return; }
                if (keyboard.nKey.wasPressedThisFrame) { StartNextSeed(); return; }
            }
            _match.AdvanceTo(_match.Elapsed + Math.Max(0d, Time.unscaledDeltaTime));
            PlanBots();
            if (_match.Phase == OddOneOutPhase.Choosing)
            {
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
                {
                    var sitting = !_match.GetChoice(0);
                    if (_match.TrySetChoice(0, sitting)) GameSound.PlayOddOneOutToggle(sitting);
                }
                for (var slot = 1; slot < 4; slot++)
                    if (_match.Elapsed >= _botTimes[slot]) _match.TrySetChoice(slot, _botChoices[slot]);
            }
            _view.RenderPractice(_match);
            _hud.SetContent("ODD ONE OUT · SOLO", "ROUND " + _match.RoundNumber + " / 10 · " + _match.Phase,
                Math.Ceiling(_match.Remaining).ToString("0") + "s · SEED " + _seed,
                "SCORE " + _match.GetScore(0) + (_match.Phase == OddOneOutPhase.Complete ? " · RANK " + _match.GetRank(0) : ""),
                "LMB SIT / STAND · R RESTART · N NEXT SEED · ESC STOP",
                "SECRET CHOICE · ONE DIFFERENT PLAYER SCORES", MinigameSoloFeedbackStyle.Neutral);
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
            if (_hud != null) _hud.BindActions(null, null, null);
            foreach (var camera in _disabledCameras) if (camera != null) camera.enabled = true;
            foreach (var listener in _disabledListeners) if (listener != null) listener.enabled = true;
        }
    }
}
