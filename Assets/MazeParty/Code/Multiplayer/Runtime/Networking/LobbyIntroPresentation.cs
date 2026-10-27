using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class LobbyIntroPresentation : MonoBehaviour
    {
        [SerializeField] private OnlineLobbyView lobby;
        [SerializeField] private GameObject connectionPanel;
        [SerializeField] private RawImage videoSurface;
        [SerializeField] private VideoPlayer introPlayer;
        [SerializeField] private VideoPlayer loopPlayer;
        [SerializeField] private CanvasGroup[] logoGroups;
        [SerializeField] private CanvasGroup[] controls;
        [SerializeField, Min(0.1f)] private float logoDuration = 1.2f;
        [SerializeField, Min(0f)] private float dropHeight = 550f;
        [SerializeField] private AnimationCurve dropCurve;

        private static LobbyIntroSequence sequence = new LobbyIntroSequence();
        private static LobbyIntroPresentation active;
        private static int escapeConsumedFrame = -1;
        private Vector2[] logoPositions;
        private bool visible;
        private bool introStarted;
        private bool loopFailed;
        private float introWaitStartedAt;

        public static bool BlocksMenu => active != null && active.visible &&
            sequence.Stage != LobbyIntroStage.Ready;
        public static bool ConsumedEscapeThisFrame => escapeConsumedFrame == Time.frameCount;
        public LobbyIntroStage Stage => sequence.Stage;
        public bool HasRequiredReferences => lobby != null && connectionPanel != null &&
            videoSurface != null && introPlayer != null && loopPlayer != null &&
            introPlayer != loopPlayer && introPlayer.clip != null && loopPlayer.clip != null &&
            HasAudioBinding(introPlayer) && HasAudioBinding(loopPlayer) &&
            logoGroups != null && logoGroups.Length == 2 &&
            System.Array.TrueForAll(logoGroups, group => group != null) &&
            controls != null && controls.Length > 0 &&
            System.Array.TrueForAll(controls, group => group != null) &&
            dropCurve != null && dropCurve.length >= 2;

        private static bool HasAudioBinding(VideoPlayer player) =>
            player.audioOutputMode == VideoAudioOutputMode.AudioSource &&
            player.controlledAudioTrackCount > 0 && player.GetTargetAudioSource(0) != null &&
            player.GetTargetAudioSource(0).GetComponent<AudioChannelSource>() != null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetRun()
        {
            sequence = new LobbyIntroSequence();
            active = null;
            escapeConsumedFrame = -1;
        }

        private void Awake()
        {
            if (!HasRequiredReferences)
            {
                Debug.LogError("Lobby intro requires its authored prefab bindings.", this);
                enabled = false;
                return;
            }
            logoPositions = new Vector2[logoGroups.Length];
            for (var i = 0; i < logoGroups.Length; i++)
                logoPositions[i] = ((RectTransform)logoGroups[i].transform).anchoredPosition;
            videoSurface.enabled = false;
            SetControls(sequence.HasPlayed);
            SetLogos(sequence.HasPlayed, 0f);
        }

        private void OnEnable()
        {
            if (!HasRequiredReferences) return;
            active = this;
            introPlayer.prepareCompleted += OnPrepared;
            loopPlayer.prepareCompleted += OnPrepared;
            introPlayer.loopPointReached += OnIntroFinished;
            introPlayer.frameReady += OnFrameReady;
            loopPlayer.frameReady += OnFrameReady;
            introPlayer.errorReceived += OnVideoError;
            loopPlayer.errorReceived += OnVideoError;
        }

        private void OnDisable()
        {
            if (introPlayer != null)
            {
                introPlayer.prepareCompleted -= OnPrepared;
                introPlayer.loopPointReached -= OnIntroFinished;
                introPlayer.frameReady -= OnFrameReady;
                introPlayer.errorReceived -= OnVideoError;
            }
            if (loopPlayer != null)
            {
                loopPlayer.prepareCompleted -= OnPrepared;
                loopPlayer.frameReady -= OnFrameReady;
                loopPlayer.errorReceived -= OnVideoError;
            }
            if (visible) EndPresentation();
            if (active == this) active = null;
        }

        private void Update()
        {
            var shouldShow = lobby.PresentationVisible && connectionPanel.activeInHierarchy;
            if (!shouldShow)
            {
                if (visible) EndPresentation();
                return;
            }
            if (!visible) BeginPresentation();

            if (sequence.Stage != LobbyIntroStage.Ready && Keyboard.current != null &&
                Keyboard.current.escapeKey.wasPressedThisFrame)
            {
                escapeConsumedFrame = Time.frameCount;
                FinishIntro();
            }

            // A missing/failed decoder must never lock the connection buttons.
            var timeout = introStarted ? (float)introPlayer.clip.length + 5f : 8f;
            if (sequence.Stage == LobbyIntroStage.Intro &&
                Time.unscaledTime - introWaitStartedAt > timeout)
                FinishIntro();

            sequence.Advance(Time.unscaledTimeAsDouble, logoDuration);
            var progress = Mathf.Clamp01((float)((Time.unscaledTimeAsDouble - sequence.LogoStartedAt) / logoDuration));
            SetLogos(sequence.Stage != LobbyIntroStage.Intro,
                sequence.Stage == LobbyIntroStage.Logo ? dropCurve.Evaluate(progress) * dropHeight : 0f);
            SetControls(sequence.Stage == LobbyIntroStage.Ready);
        }

        private void BeginPresentation()
        {
            visible = true;
            active = this;
            introStarted = false;
            loopFailed = false;
            introPlayer.GetTargetAudioSource(0).mute = false;
            loopPlayer.GetTargetAudioSource(0).mute = false;
            introWaitStartedAt = Time.unscaledTime;
            sequence.Begin();
            SetControls(sequence.Stage == LobbyIntroStage.Ready);
            SetLogos(sequence.Stage == LobbyIntroStage.Ready, 0f);
            loopPlayer.Prepare();
            if (sequence.Stage == LobbyIntroStage.Intro) introPlayer.Prepare();
        }

        private void EndPresentation()
        {
            visible = false;
            sequence.Hide();
            videoSurface.enabled = false;
            videoSurface.texture = null;
            introPlayer.GetTargetAudioSource(0).mute = true;
            loopPlayer.GetTargetAudioSource(0).mute = true;
            introPlayer.Stop();
            loopPlayer.Stop();
            SetControls(true);
            SetLogos(true, 0f);
        }

        private void OnPrepared(VideoPlayer player)
        {
            if (!visible) return;
            if (player == introPlayer && sequence.Stage == LobbyIntroStage.Intro)
            {
                introStarted = true;
                introWaitStartedAt = Time.unscaledTime;
                player.Play();
            }
            else if (player == loopPlayer && sequence.Stage != LobbyIntroStage.Intro)
                player.Play();
        }

        private void OnIntroFinished(VideoPlayer player) => FinishIntro();

        private void FinishIntro()
        {
            if (!visible || sequence.Stage != LobbyIntroStage.Intro) return;
            sequence.FinishIntro(Time.unscaledTimeAsDouble);
            introPlayer.GetTargetAudioSource(0).mute = true;
            introPlayer.Pause();
            if (!loopFailed && loopPlayer.isPrepared) loopPlayer.Play();
        }

        private void OnFrameReady(VideoPlayer player, long frame)
        {
            if (!visible || player.texture == null) return;
            if (player == introPlayer && sequence.Stage != LobbyIntroStage.Intro) return;
            if (player == loopPlayer && sequence.Stage == LobbyIntroStage.Intro) return;
            videoSurface.texture = player.texture;
            videoSurface.enabled = true;
            if (player == loopPlayer && introPlayer.isPrepared) introPlayer.Stop();
        }

        private void OnVideoError(VideoPlayer player, string message)
        {
            if (!visible) return;
            Debug.LogWarning("Lobby video playback failed: " + message, this);
            if (player == introPlayer) FinishIntro();
            else
            {
                loopFailed = true;
                if (videoSurface.texture == player.texture)
                {
                    videoSurface.enabled = false;
                    videoSurface.texture = null;
                }
                player.Stop();
            }
        }

        private void SetControls(bool show)
        {
            foreach (var group in controls)
            {
                group.alpha = show ? 1f : 0f;
                group.interactable = show;
                group.blocksRaycasts = show;
            }
        }

        private void SetLogos(bool show, float offset)
        {
            if (logoPositions == null) return;
            for (var i = 0; i < logoGroups.Length; i++)
            {
                logoGroups[i].alpha = show ? 1f : 0f;
                ((RectTransform)logoGroups[i].transform).anchoredPosition =
                    logoPositions[i] + Vector2.up * offset;
            }
        }
    }
}
