using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.ShellGame;
using MazeParty.Multiplayer;
using UnityEngine;
using UnityEngine.InputSystem;

namespace MazeParty.Dev.MinigameSoloTest
{
    [DefaultExecutionOrder(-100)]
    public sealed class ShellGameSoloTestController : MonoBehaviour
    {
        private MinigameSoloHudView _hud;
        private ShellGameNetworkView _view;
        private readonly List<Camera> _disabledCameras=new List<Camera>();
        private readonly List<AudioListener> _disabledListeners=new List<AudioListener>();
        private ShellGameMatchState _match;
        private Camera _camera;
        private int _seed, _plannedRound;
        private System.Random _random;
        private readonly double[] _botTimes=new double[4];
        private readonly int[] _botChoices=new int[4];
        public ShellGameMatchState Match => _match;
        public Camera RuntimeCamera => _camera;
        public bool IsInitialized => _match!=null;
        public int Seed => _seed;
        public void ConfigureHud(MinigameSoloHudView hud) { _hud=hud; }
        public void Begin(int seed)
        {
            if(_hud==null || !_hud.HasRequiredReferences) throw new InvalidOperationException("Authored solo HUD required.");
            _view=FindAnyObjectByType<ShellGameNetworkView>(FindObjectsInactive.Include);
            if(_view==null || !_view.HasRequiredReferences) throw new InvalidOperationException("ShellGame scene is incomplete.");
            foreach(var c in FindObjectsByType<Camera>(FindObjectsSortMode.None))
                if(c.enabled) { _disabledCameras.Add(c); c.enabled=false; }
            foreach(var l in FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if(l.enabled) { _disabledListeners.Add(l); l.enabled=false; }
            var root=new GameObject("Shell Game Solo Camera"); root.tag="MainCamera"; root.transform.SetParent(transform);
            root.transform.SetPositionAndRotation(_view.CameraPosition,_view.CameraRotation);
            _camera=root.AddComponent<Camera>(); _camera.orthographic=true;
            _camera.orthographicSize=_view.CameraSize; _camera.farClipPlane=120f;
            root.AddComponent<AudioListener>();
            _hud.BindActions(RestartMatch,StartNextSeed,StopSoloTest);
            _hud.gameObject.SetActive(false);
            BeginMatch(seed);
        }
        private void BeginMatch(int seed)
        {
            _seed=seed; _random=new System.Random(seed); _match=new ShellGameMatchState(unchecked((ulong)seed));
            _plannedRound=0; _view.ResetPracticePresentation(); _view.RenderPractice(_match);
        }
        private void Update()
        {
            if(_match==null) return;
            var keyboard=Keyboard.current;
            if(keyboard!=null)
            {
                if(keyboard.escapeKey.wasPressedThisFrame) { StopSoloTest(); return; }
                if(keyboard.rKey.wasPressedThisFrame) { RestartMatch(); return; }
                if(keyboard.nKey.wasPressedThisFrame) { StartNextSeed(); return; }
            }
            _match.AdvanceTo(_match.Elapsed+Math.Max(0,Time.unscaledDeltaTime));
            if(_match.Phase==ShellGamePhase.Choosing)
            {
                if(_plannedRound!=_match.RoundNumber)
                {
                    _plannedRound=_match.RoundNumber;
                    for(var i=1;i<4;i++)
                    { _botTimes[i]=_match.Elapsed+.5+_random.NextDouble()*4; _botChoices[i]=_random.Next(3); }
                }
                // The same collider selection path used by the online owner.
                if(Mouse.current!=null && Mouse.current.leftButton.wasPressedThisFrame &&
                    _view.TryPickBox(_camera,Mouse.current.position.ReadValue(),out var box) && _match.TryChoose(0,box))
                    GameSound.Play(SoundKeys.MinigameShellSelect);
                for(var i=1;i<4;i++)
                    if(_match.Elapsed>=_botTimes[i]) _match.TryChoose(i,_botChoices[i]);
            }
            _view.RenderPractice(_match);
        }
        public void RestartMatch() { BeginMatch(_seed); }
        public void StartNextSeed() { BeginMatch(unchecked(_seed+1)); }
        public void StopSoloTest()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
#endif
        }
        private void OnDestroy()
        {
            if(_hud!=null) _hud.BindActions(null,null,null);
            foreach(var c in _disabledCameras) if(c!=null) c.enabled=true;
            foreach(var l in _disabledListeners) if(l!=null) l.enabled=true;
        }
    }
}
