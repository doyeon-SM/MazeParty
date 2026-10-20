using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.Flight;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    public sealed class FlightSoloTestController : MonoBehaviour
    {
        private MinigameSoloHudView _hud;
        private FlightNetworkView _view;
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<AudioListener> _disabledListeners = new List<AudioListener>();
        private Camera _camera;
        private int _seed, _botTick;
        public FlightMatchState Match { get; private set; }
        public Camera RuntimeCamera => _camera;
        public bool IsInitialized => Match != null;
        public int Seed => _seed;
        public void ConfigureHud(MinigameSoloHudView hud) { _hud = hud; }
        public void Begin(int seed)
        {
            _view = FindAnyObjectByType<FlightNetworkView>(FindObjectsInactive.Include);
            if (_view == null || !_view.HasRequiredReferences) throw new InvalidOperationException("Flight scene is incomplete.");
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None)) if (camera.enabled) { _disabledCameras.Add(camera); camera.enabled = false; }
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None)) if (listener.enabled) { _disabledListeners.Add(listener); listener.enabled = false; }
            var root = new GameObject("Flight Solo Camera"); root.tag = "MainCamera"; root.transform.SetParent(transform);
            _camera = root.AddComponent<Camera>(); _camera.farClipPlane = 100; root.AddComponent<AudioListener>();
            if (_hud != null) _hud.gameObject.SetActive(false);
            BeginMatch(seed);
        }
        private void BeginMatch(int seed)
        {
            _seed = seed; _botTick = 0; Match = new FlightMatchState(unchecked((ulong)seed));
            _view.ResetPracticePresentation(); Render();
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
            AdvancePractice(Math.Max(0, Time.unscaledDeltaTime));
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame && Match.TryFlap(0))
                GameSound.PlayPitched(SoundKeys.MinigameFlightFlap, 1.2f, 1f);
            Render();
        }
        private void AdvancePractice(double delta)
        {
            var target = Match.Elapsed + delta;
            // Bots follow the constructed path at its exact input times, independent of render FPS.
            while (FlightRules.CountdownSeconds + _botTick * FlightRules.Step <= target &&
                Match.Phase != FlightPhase.Complete && _botTick < Match.Course.WitnessTicks)
            {
                Match.AdvanceTo(FlightRules.CountdownSeconds + _botTick * FlightRules.Step);
                if (Match.Phase != FlightPhase.Running) break;
                if (Match.Course.WitnessFlap(_botTick))
                    for (var slot = 1; slot < 4; slot++)
                        // Small phase differences make the translucent opponents readable.
                        if (slot == 1 || _botTick % (slot == 2 ? 137 : 223) != 0) Match.TryFlap(slot);
                _botTick++;
            }
            Match.AdvanceTo(target);
        }
        private void Render() { _view.RenderPractice(Match, _camera); }
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
