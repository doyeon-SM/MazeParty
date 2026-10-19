using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.CoinCollect;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    public sealed class CoinCollectSoloTestController : MonoBehaviour
    {
        private MinigameSoloHudView _hud;
        private CoinCollectNetworkView _view;
        private readonly List<Camera> _disabledCameras = new List<Camera>();
        private readonly List<AudioListener> _disabledListeners = new List<AudioListener>();
        private CoinCollectMatchState _match;
        private Camera _camera;
        private int _seed;
        private double _phaseElapsed;
        private NetworkCoinCollectPhase _phase;
        public CoinCollectMatchState Match => _match;
        public Camera RuntimeCamera => _camera;
        public bool IsInitialized => _match != null;
        public int Seed => _seed;
        public void ConfigureHud(MinigameSoloHudView hud) { _hud = hud; }
        public void Begin(int seed)
        {
            if (_hud == null || !_hud.HasRequiredReferences) throw new InvalidOperationException("Authored solo HUD required.");
            _view = FindAnyObjectByType<CoinCollectNetworkView>(FindObjectsInactive.Include);
            if (_view == null || !_view.HasRequiredReferences) throw new InvalidOperationException("CoinCollect scene is incomplete.");
            foreach (var camera in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if (camera.enabled) { _disabledCameras.Add(camera); camera.enabled = false; }
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener.enabled) { _disabledListeners.Add(listener); listener.enabled = false; }
            var cameraRoot = new GameObject("Coin Collect Solo Camera"); cameraRoot.transform.SetParent(transform);
            cameraRoot.transform.SetPositionAndRotation(CoinCollectNetworkView.SharedCameraPosition, CoinCollectNetworkView.SharedCameraRotation);
            _camera = cameraRoot.AddComponent<Camera>(); _camera.orthographic = true;
            _camera.orthographicSize = CoinCollectNetworkView.SharedCameraSize; _camera.farClipPlane = 80f;
            cameraRoot.AddComponent<AudioListener>();
            _hud.BindActions(RestartMatch, StartNextSeed, StopSoloTest);
            BeginMatch(seed);
        }
        private void BeginMatch(int seed)
        {
            _seed = seed; _match = new CoinCollectMatchState(unchecked((ulong)seed));
            _match.ItemCollected += (kind, slot) => NetworkCoinCollectState.PlayCollectionSound(kind);
            _match.PushStarted += _view.PlayPushAnimation;
            _phase = NetworkCoinCollectPhase.Countdown; _phaseElapsed = 0d;
            _view.RenderPractice(_match);
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
            var dt = Math.Max(0d, Time.unscaledDeltaTime);
            _phaseElapsed += dt;
            if (_phase == NetworkCoinCollectPhase.Countdown && _phaseElapsed >= NetworkCoinCollectState.CountdownSeconds)
            { _phase = NetworkCoinCollectPhase.Running; _phaseElapsed = 0d; }
            if (_phase == NetworkCoinCollectPhase.Running)
            {
                var input = keyboard == null ? Vector2.zero : new Vector2(
                    (keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                    (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                _match.SetMovementInput(0, input);
                if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) _match.TryPush(0);
                for (var slot = 1; slot < 4; slot++) DriveBot(slot);
                _match.AdvanceTo(_match.Elapsed + dt);
                if (_match.IsRoundComplete) { _phase = NetworkCoinCollectPhase.RoundResult; _phaseElapsed = 0d; }
            }
            else if (_phase == NetworkCoinCollectPhase.RoundResult && _phaseElapsed >= NetworkCoinCollectState.RoundResultSeconds)
            {
                _phase = _match.BeginNextRound() ? NetworkCoinCollectPhase.Countdown : NetworkCoinCollectPhase.Complete;
                _phaseElapsed = 0d;
            }
            _view.RenderPractice(_match);
            _hud.SetContent("COIN COLLECT · SOLO", "ROUND " + _match.RoundNumber + " / 2 · " + _phase,
                Math.Max(0d, CoinCollectRules.RoundSeconds - _match.Elapsed).ToString("0.0") + "s · SEED " + _seed,
                "SCORE " + _match.GetPlayer(0).Score + (_phase == NetworkCoinCollectPhase.Complete ? " · RANK " + _match.GetRank(0) : ""),
                "WASD MOVE · LMB PUSH · R RESTART · N NEXT SEED · ESC STOP",
                "COIN +1 · MONEY +5 · BOMB: DROP HALF + 1s STUN", MinigameSoloFeedbackStyle.Neutral);
        }
        private void DriveBot(int slot)
        {
            var p = _match.GetPlayer(slot); var best = float.MaxValue; var target = p.Position;
            for (var i = 0; i < _match.ItemCount; i++)
            {
                var item = _match.GetItem(i);
                if (!item.Active || item.Kind == CoinCollectItemKind.Bomb) continue;
                var distance = (item.Position - p.Position).sqrMagnitude;
                if (distance < best) { best = distance; target = item.Position; }
            }
            _match.SetMovementInput(slot, best < .02f ? Vector2.zero : (target - p.Position).normalized);
            for (var other = 0; other < 4; other++)
                if (other != slot && (_match.GetPlayer(other).Position - p.Position).sqrMagnitude < 1.5f) _match.TryPush(slot);
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
