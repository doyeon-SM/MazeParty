using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.ShellGame;
using Unity.Cinemachine;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class ShellGameNetworkView : MonoBehaviour
    {
        [SerializeField] private NetworkShellGameState state;
        [SerializeField] private CinemachineCamera sharedCamera;
        [SerializeField] private GameObject arenaPresentation;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform[] playerAnchors;
        [SerializeField] private Transform[] boxes;
        [SerializeField] private Collider[] boxColliders;
        [SerializeField] private Transform coin;
        [SerializeField] private Transform selectionMarker;
        [SerializeField] private GameObject arrivalVfxPrefab;
        [SerializeField] private BouncingBallsHudBindings scoreHud;
        [SerializeField] private ShellGameHudBindings selectionHud;
        [SerializeField] private MinigameStartCountdownView practiceCountdown;
        [SerializeField] private MinigameCommonHudView practiceHud;
        [SerializeField] private Vector3 boxCenter;
        [SerializeField] private float boxSpacing = 3f;
        private readonly PlayerAvatarVisual[] _players = new PlayerAvatarVisual[4];
        private static readonly Color[] Colors = { new Color(.16f,.48f,.95f), new Color(.92f,.2f,.16f),
            new Color(.18f,.78f,.32f), new Color(.7f,.26f,.9f) };
        private readonly SoundHandle[] _resultSounds = new SoundHandle[4];
        private readonly PooledOneShotVfx[] _resultEffects = new PooledOneShotVfx[4];
        private readonly int[] _resultEffectRevisions = new int[4];
        private SoundHandle _wrongSound;
        private ShellGameRoundPlan _plan;
        private ulong _seed;
        private int _round, _lastRound, _lastSwap = -1;
        private ShellGamePhase _lastPhase;
        private bool _practice, _observed, _choosing;
        private GameplayCameraDirector _cameraDirector;
        public static ShellGameNetworkView Instance { get; private set; }
        public CinemachineCamera SharedCamera => sharedCamera;
        public Vector3 CameraPosition => sharedCamera.transform.position;
        public Quaternion CameraRotation => sharedCamera.transform.rotation;
        public float CameraSize => sharedCamera.Lens.OrthographicSize;
        public bool HasRequiredReferences => state != null && sharedCamera != null && arenaPresentation != null &&
            playerRoot != null && Valid(playerAnchors,4) && Valid(boxes,3) && coin != null && selectionMarker != null &&
            boxColliders != null && boxColliders.Length == 3 && Array.TrueForAll(boxColliders,c=>c!=null) &&
            arrivalVfxPrefab != null &&
            scoreHud != null && scoreHud.HasRequiredReferences && selectionHud != null && selectionHud.HasRequiredReferences &&
            practiceCountdown != null && practiceCountdown.HasRequiredReferences && practiceHud != null && practiceHud.HasRequiredReferences;
        private static bool Valid(Transform[] values,int length) => values!=null && values.Length==length && Array.TrueForAll(values,v=>v!=null);
        public void Configure(NetworkShellGameState authority, CinemachineCamera camera, GameObject arena, Transform players,
            Transform[] anchors, Transform[] boxObjects, Collider[] colliders, Transform coinObject, Transform marker, GameObject arrivalVfx,
            BouncingBallsHudBindings scores, ShellGameHudBindings selection, MinigameStartCountdownView countdown,
            MinigameCommonHudView common, Vector3 center)
        {
            state=authority; sharedCamera=camera; arenaPresentation=arena; playerRoot=players; playerAnchors=anchors;
            boxes=boxObjects; boxColliders=colliders; coin=coinObject; selectionMarker=marker; scoreHud=scores;
            arrivalVfxPrefab=arrivalVfx; selectionHud=selection; practiceCountdown=countdown; practiceHud=common; boxCenter=center;
        }
        private void Awake()
        {
            Instance=this;
            if(practiceCountdown!=null) practiceCountdown.enabled=false;
            if(practiceHud!=null) practiceHud.enabled=false;
            SetVisible(false);
        }
        private void OnDestroy() { if(Instance==this) Instance=null; }
        private void OnDisable()
        {
            SetVisible(false);
            if(_cameraDirector!=null) _cameraDirector.ClearMinigameCamera(sharedCamera);
        }
        public void ResetPracticePresentation()
        {
            ClearResultFeedback();
            _observed=false; _lastSwap=-1; _plan=null;
            practiceCountdown.SetCountdown(0,false,false);
        }
        private void LateUpdate()
        {
            if(_practice) return;
            var match=NetworkMatchState.Instance;
            var visible=state!=null && state.IsSpawned && match!=null && match.CurrentMinigame==ScheduledMinigameId.ShellGame &&
                (match.FlowState==BoardFlowState.MinigamePlaying || match.FlowState==BoardFlowState.MinigameResult);
            SetVisible(visible);
            if(!visible)
            {
                _observed=false;
                if(_cameraDirector!=null) _cameraDirector.ClearMinigameCamera(sharedCamera);
                return;
            }
            _cameraDirector ??= FindAnyObjectByType<GameplayCameraDirector>();
            _cameraDirector?.SetMinigameCamera(sharedCamera);
            _cameraDirector?.SetUiPointerVisible(true);
            var local=-1;
            for(var i=0;i<4;i++) if(match.GetAvatarForSlot(i)?.IsOwner==true) local=i;
            Render(state.Seed,state.Phase,state.RoundNumber,state.Remaining,
                i=>state.GetVisibleChoice(i,local),state.GetScore,local,state.IsPaused,match);
        }
        public void RenderPractice(ShellGameMatchState simulation)
        {
            _practice=true; SetVisible(true);
            Cursor.lockState=CursorLockMode.None; Cursor.visible=true;
            practiceCountdown.SetCountdown((int)Math.Ceiling(simulation.Remaining),
                simulation.Phase==ShellGamePhase.Countdown && simulation.Remaining>0);
            practiceHud.ShowPracticeRoundOnly(simulation.RoundNumber,5);
            Render(simulation.Seed,simulation.Phase,simulation.RoundNumber,simulation.Remaining,
                i=>simulation.GetVisibleChoice(i,0),simulation.GetScore,0,false,null);
        }
        private void Render(ulong seed, ShellGamePhase phase, int round, double remaining, Func<int,int> choice,
            Func<int,int> score, int local, bool paused, NetworkMatchState match)
        {
            if(round<1) return;
            if(_plan==null || _round!=round || _seed!=seed)
            { _plan=new ShellGameRoundPlan(seed,round); _round=round; _seed=seed; }
            EnsurePlayers();
            for(var i=0;i<4;i++)
            {
                var avatar=match!=null ? match.GetAvatarForSlot(i) : null;
                var name=avatar!=null ? avatar.DisplayName : GameText.F("Player {0}",i+1);
                var color=avatar!=null ? avatar.Appearance.BodyColor : Colors[i];
                var visual=_players[i];
                visual.transform.SetPositionAndRotation(playerAnchors[i].position,playerAnchors[i].rotation);
                visual.SetDisplayName(name); visual.SetBodyColor(color);
                if(avatar!=null)
                { var a=avatar.Appearance; visual.ApplyAppearance(a.EyeId,a.MouthId,a.HatId,a.ExpressionId); }
                scoreHud.SetPlayer(i,name,score(i),color);
            }
            var shuffleElapsed=phase==ShellGamePhase.Shuffling ? Math.Max(0,_plan.ShuffleSeconds-remaining) : 0;
            var completed=phase==ShellGamePhase.Choosing || phase==ShellGamePhase.Reveal || phase==ShellGamePhase.Complete
                ? _plan.ShuffleCount : phase==ShellGamePhase.Shuffling ? Math.Min(_plan.ShuffleCount,(int)(shuffleElapsed/_plan.SwapSeconds)) : 0;
            var t=phase==ShellGamePhase.Shuffling ? Mathf.Clamp01((float)(shuffleElapsed/_plan.SwapSeconds-completed)) : 0;
            var smooth=t*t*(3-2*t);
            var lift=phase==ShellGamePhase.Countdown || phase==ShellGamePhase.Preview ? 1f :
                phase==ShellGamePhase.Hiding ? Mathf.Clamp01((float)(remaining/ShellGameMatchState.LiftSeconds)) :
                phase==ShellGamePhase.Reveal ? Mathf.Clamp01((float)((ShellGameMatchState.ResultSeconds-remaining)/ShellGameMatchState.LiftSeconds)) :
                phase==ShellGamePhase.Complete ? 1f : 0f;
            for(var box=0;box<3;box++)
            {
                var from=_plan.Position(box,completed);
                var position=boxCenter + Vector3.right*((from-1)*boxSpacing) + Vector3.up*(lift*2.5f);
                if(phase==ShellGamePhase.Shuffling && completed<_plan.ShuffleCount)
                {
                    var to=_plan.Position(box,completed+1);
                    position.x=boxCenter.x+Mathf.Lerp(from-1,to-1,smooth)*boxSpacing;
                    if(from!=to) position.z += Mathf.Sin(t*Mathf.PI)*(box==_plan.FirstBox(completed)?2f:-2f);
                }
                boxes[box].position=position;
            }
            // Keep the coin hidden throughout shuffle/selection, including oblique camera views.
            coin.gameObject.SetActive(phase==ShellGamePhase.Preview || phase==ShellGamePhase.Hiding ||
                phase==ShellGamePhase.Reveal || phase==ShellGamePhase.Complete);
            coin.position=boxCenter+Vector3.right*((_plan.Position(_plan.CoinBox,completed)-1)*boxSpacing)+Vector3.up*.55f;
            _choosing=phase==ShellGamePhase.Choosing && remaining>0 && !paused;
            var selected=local>=0 ? choice(local) : -1;
            selectionMarker.gameObject.SetActive(selected>=0 && (phase==ShellGamePhase.Choosing || phase==ShellGamePhase.Reveal));
            if(selected>=0) selectionMarker.position=new Vector3(boxes[selected].position.x,boxCenter.y+.025f,boxCenter.z);
            selectionHud.SetSeconds(remaining,phase==ShellGamePhase.Choosing);
            if(_observed && !paused && phase==ShellGamePhase.Shuffling && completed<_plan.ShuffleCount &&
                (_lastRound!=round || _lastPhase!=phase || _lastSwap!=completed))
                GameSound.Play(SoundKeys.MinigameShellShuffle);
            if(phase!=ShellGamePhase.Reveal || paused) ClearResultFeedback();
            // The public reveal contains every choice; render each correct player on every client.
            // A repeated snapshot or joining during a result must not replay past celebrations.
            if(_observed && !paused && phase==ShellGamePhase.Reveal && (_lastRound!=round || _lastPhase!=phase))
            {
                ClearResultFeedback();
                var correctCount=0;
                for(var slot=0;slot<4;slot++) if(choice(slot)==_plan.CoinBox) correctCount++;
                for(var slot=0;slot<4;slot++)
                {
                    if(choice(slot)!=_plan.CoinBox) continue;
                    var position=_players[slot].transform.position+Vector3.up*.65f;
                    var effect=OneShotVfxPool.Play(arrivalVfxPrefab,position,Quaternion.identity,1.1f);
                    _resultEffects[slot]=effect;
                    _resultEffectRevisions[slot]=effect!=null ? effect.PlaybackRevision : 0;
                    // Keep a simultaneous four-player win at the same overall sound level.
                    _resultSounds[slot]=GameSound.PlayAt(SoundKeys.MinigameShellCorrect,position,1f/correctCount);
                }
                if(local>=0 && selected!=_plan.CoinBox)
                    _wrongSound=GameSound.Play(SoundKeys.MinigameShellWrong);
            }
            _observed=true; _lastRound=round; _lastPhase=phase; _lastSwap=completed;
        }
        private void ClearResultFeedback()
        {
            GameSound.Stop(_wrongSound,.1f); _wrongSound=default;
            for(var slot=0;slot<4;slot++)
            {
                GameSound.Stop(_resultSounds[slot],.1f); _resultSounds[slot]=default;
                if(_resultEffects[slot]!=null)
                    _resultEffects[slot].StopPresentation(_resultEffectRevisions[slot]);
                _resultEffects[slot]=null; _resultEffectRevisions[slot]=0;
            }
        }
        public bool TryPickBox(Camera camera, Vector2 screen, out int box)
        {
            box=-1;
            if(!_choosing || camera==null) return false;
            var ray=camera.ScreenPointToRay(screen); var nearest=float.MaxValue;
            for(var i=0;i<3;i++)
                if(boxColliders[i].Raycast(ray,out var hit,100f) && hit.distance<nearest)
                { nearest=hit.distance; box=i; }
            return box>=0;
        }
        private void SetVisible(bool value)
        {
            _choosing &= value;
            if(arenaPresentation!=null) arenaPresentation.SetActive(value);
            if(playerRoot!=null) playerRoot.gameObject.SetActive(value);
            if(boxes!=null) foreach(var box in boxes) if(box!=null) box.gameObject.SetActive(value);
            if(!value)
            {
                ClearResultFeedback();
                if(coin!=null) coin.gameObject.SetActive(false);
                if(selectionMarker!=null) selectionMarker.gameObject.SetActive(false);
            }
            if(scoreHud!=null) scoreHud.RootCanvas.gameObject.SetActive(value);
            if(selectionHud!=null) selectionHud.RootCanvas.gameObject.SetActive(value);
            if(practiceCountdown!=null)
            {
                practiceCountdown.gameObject.SetActive(value && _practice);
                if(!value) practiceCountdown.SetCountdown(0,false,false);
            }
        }
        private void EnsurePlayers()
        {
            for(var i=0;i<4;i++)
            {
                if(_players[i]!=null) continue;
                var root=new GameObject("Shell Game Player "+(i+1)); root.transform.SetParent(playerRoot,false);
                var visual=root.AddComponent<PlayerAvatarVisual>(); visual.EnsureBuilt();
                visual.SetOwnerFirstPerson(false); visual.SetNameplateVisible(true);
                foreach(var c in root.GetComponentsInChildren<Collider>(true)) c.enabled=false;
                _players[i]=visual;
            }
        }
    }
}
