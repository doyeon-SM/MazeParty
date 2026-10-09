using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client-only pre-intro visualization of the immutable turn schedule.
    /// Revealed turns remain open as a match record while future games stay hidden.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameScheduleTowerView : MonoBehaviour
    {
        public const float RevealDelaySeconds = 0.7f;
        public const int MaximumVisibleBlocks =
            MinigameScheduleRules.DefaultTurnCount;

        [Header("Prefab UI Bindings")]
        [SerializeField] private Canvas canvas;
        [SerializeField] private RectTransform panel;
        [SerializeField] private Text title;
        [SerializeField] private Text subtitle;
        [SerializeField] private Image[] blocks = Array.Empty<Image>();
        [SerializeField] private Text[] blockLabels = Array.Empty<Text>();

        [Header("State Colors")]
        [SerializeField] private Color futureBlockColor =
            new Color(0.12f, 0.16f, 0.23f, 0.98f);
        [SerializeField] private Color hiddenCurrentColor =
            new Color(0.28f, 0.32f, 0.43f, 1f);
        [SerializeField] private Color skipColor =
            new Color(0.44f, 0.46f, 0.52f, 1f);
        private Vector3[] _blockBaseScales = Array.Empty<Vector3>();
        private int _observedRevision = -1;
        private float _revisionObservedAt;
        private int _revealSoundRevision = -1;

        public bool HasRequiredReferences =>
            canvas != null &&
            panel != null &&
            title != null &&
            subtitle != null &&
            blocks != null &&
            blocks.Length == MaximumVisibleBlocks &&
            Array.TrueForAll(blocks, block => block != null) &&
            blockLabels != null &&
            blockLabels.Length == MaximumVisibleBlocks &&
            Array.TrueForAll(blockLabels, label => label != null);

        public int BlockCount => blocks != null ? blocks.Length : 0;

        public void Configure(
            Canvas configuredCanvas,
            RectTransform configuredPanel,
            Text configuredTitle,
            Text configuredSubtitle,
            Image[] configuredBlocks,
            Text[] configuredBlockLabels)
        {
            canvas = configuredCanvas;
            panel = configuredPanel;
            title = configuredTitle;
            subtitle = configuredSubtitle;
            blocks = configuredBlocks ?? Array.Empty<Image>();
            blockLabels = configuredBlockLabels ?? Array.Empty<Text>();
        }

        private void Awake()
        {
            if (!HasRequiredReferences)
            {
                Debug.LogError(
                    "MinigameScheduleTowerView is missing one or more prefab UI bindings.",
                    this);
                enabled = false;
                return;
            }

            CacheBlockBaseScales();
            canvas.enabled = false;
        }

        private void OnDisable()
        {
            ResetBlockScales();
        }

        private void Update()
        {
            var match = NetworkMatchState.Instance;
            var visible =
                match != null &&
                match.IsSpawned &&
                match.GameplayEnabled &&
                match.FlowState == BoardFlowState.MinigameIntroReady &&
                !match.IsGlobalSimulationPaused;
            if (canvas.enabled != visible)
            {
                canvas.enabled = visible;
                if (!visible)
                {
                    ResetBlockScales();
                }
            }

            if (!visible)
            {
                return;
            }

            if (_observedRevision != match.MinigameRevealRevision)
            {
                _observedRevision = match.MinigameRevealRevision;
                _revisionObservedAt = Time.unscaledTime;
            }

            var revealed =
                Time.unscaledTime - _revisionObservedAt >= RevealDelaySeconds;
            if (revealed && _revealSoundRevision != _observedRevision)
            {
                _revealSoundRevision = _observedRevision;
                GameSound.Play(SoundKeys.MinigameReveal);
            }

            Refresh(match, revealed);
        }

        private void Refresh(NetworkMatchState match, bool revealed)
        {
            subtitle.text = GameText.F(
                "TURN {0}  ·  {1} BLOCKS LEFT",
                match.Turn,
                match.RemainingMinigameSlots);

            for (var index = 0; index < blocks.Length; index++)
            {
                if (!blocks[index].gameObject.activeSelf)
                {
                    blocks[index].gameObject.SetActive(true);
                }

                var oneBasedTurn = index + 1;
                var current = oneBasedTurn == match.Turn;
                var hasRevealedMinigame =
                    match.TryGetRevealedMinigame(
                        oneBasedTurn,
                        out var minigame);
                var showName = hasRevealedMinigame &&
                               (!current || revealed);
                blockLabels[index].text =
                    showName
                        ? DisplayName(minigame)
                        : "???";
                blocks[index].color =
                    current && !revealed
                        ? hiddenCurrentColor
                        : showName
                            ? RevealedColor(minigame)
                        : futureBlockColor;

                var pulse = current && !revealed
                    ? 1f + Mathf.Sin(Time.unscaledTime * 10f) * 0.025f
                    : current && revealed
                        ? 1.04f
                        : 1f;
                ApplyBlockScale(index, pulse);
            }
        }

        private void CacheBlockBaseScales()
        {
            _blockBaseScales = new Vector3[blocks.Length];
            for (var index = 0; index < blocks.Length; index++)
            {
                _blockBaseScales[index] =
                    blocks[index].rectTransform.localScale;
            }
        }

        private void ResetBlockScales()
        {
            var count = Mathf.Min(
                blocks != null ? blocks.Length : 0,
                _blockBaseScales.Length);
            for (var index = 0; index < count; index++)
            {
                if (blocks[index] != null)
                {
                    blocks[index].rectTransform.localScale =
                        _blockBaseScales[index];
                }
            }
        }

        private void ApplyBlockScale(int index, float multiplier)
        {
            if (index < 0 || index >= _blockBaseScales.Length)
            {
                return;
            }

            blocks[index].rectTransform.localScale = Vector3.Scale(
                _blockBaseScales[index],
                new Vector3(multiplier, multiplier, 1f));
        }

        private static string DisplayName(ScheduledMinigameId minigame)
        {
            return GameText.T(MinigameCatalog.GetDisplayName(minigame));
        }

        private Color RevealedColor(ScheduledMinigameId minigame)
        {
            if (minigame == ScheduledMinigameId.Skip)
            {
                return skipColor;
            }

            return MinigameCatalog.GetTowerColor(minigame);
        }
    }
}
