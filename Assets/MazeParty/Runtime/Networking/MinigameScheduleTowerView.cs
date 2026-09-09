using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Client-only pre-intro visualization of the immutable turn schedule.
    /// Future blocks stay hidden; only the current top block is revealed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinigameScheduleTowerView : MonoBehaviour
    {
        public const float RevealDelaySeconds = 0.7f;
        private const int MaximumVisibleBlocks =
            MinigameScheduleRules.DefaultTurnCount;

        private Canvas _canvas;
        private RectTransform _panel;
        private Text _title;
        private Text _subtitle;
        private readonly Image[] _blocks = new Image[MaximumVisibleBlocks];
        private readonly Text[] _blockLabels = new Text[MaximumVisibleBlocks];
        private int _observedRevision = -1;
        private float _revisionObservedAt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (FindAnyObjectByType<MinigameScheduleTowerView>() != null)
            {
                return;
            }

            var root = new GameObject("[UI] Minigame Schedule Tower");
            DontDestroyOnLoad(root);
            root.AddComponent<MinigameScheduleTowerView>();
        }

        private void Awake()
        {
            EnsureUi();
            _canvas.enabled = false;
        }

        private void Update()
        {
            EnsureUi();
            var match = NetworkMatchState.Instance;
            var visible =
                match != null &&
                match.IsSpawned &&
                match.GameplayEnabled &&
                match.FlowState == BoardFlowState.MinigameIntroReady &&
                !match.IsGlobalSimulationPaused;
            if (_canvas.enabled != visible)
            {
                _canvas.enabled = visible;
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
            Refresh(match, revealed);
        }

        private void EnsureUi()
        {
            if (_canvas != null)
            {
                return;
            }

            _canvas = gameObject.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 55;
            var scaler = gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            gameObject.AddComponent<GraphicRaycaster>();

            var panelObject = new GameObject("Tower Panel");
            panelObject.transform.SetParent(transform, false);
            _panel = panelObject.AddComponent<RectTransform>();
            _panel.anchorMin = new Vector2(1f, 0.5f);
            _panel.anchorMax = new Vector2(1f, 0.5f);
            _panel.pivot = new Vector2(1f, 0.5f);
            _panel.anchoredPosition = new Vector2(-30f, 0f);
            _panel.sizeDelta = new Vector2(310f, 860f);
            var panelImage = panelObject.AddComponent<Image>();
            panelImage.color = new Color(0.02f, 0.03f, 0.055f, 0.93f);
            panelImage.raycastTarget = false;

            _title = CreateText(
                "Title",
                _panel,
                new Vector2(0f, -24f),
                new Vector2(280f, 42f),
                25,
                FontStyle.Bold);
            _subtitle = CreateText(
                "Subtitle",
                _panel,
                new Vector2(0f, -66f),
                new Vector2(280f, 32f),
                17,
                FontStyle.Normal);

            for (var index = 0; index < MaximumVisibleBlocks; index++)
            {
                var block = new GameObject("Block " + (index + 1));
                block.transform.SetParent(_panel, false);
                var rect = block.AddComponent<RectTransform>();
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition =
                    new Vector2(0f, -108f - index * 48f);
                rect.sizeDelta = new Vector2(
                    264f - Mathf.Min(index, 8) * 4f,
                    40f);
                _blocks[index] = block.AddComponent<Image>();
                _blocks[index].raycastTarget = false;
                _blockLabels[index] = CreateText(
                    "Label",
                    rect,
                    Vector2.zero,
                    rect.sizeDelta,
                    18,
                    index == 0 ? FontStyle.Bold : FontStyle.Normal);
            }
        }

        private void Refresh(NetworkMatchState match, bool revealed)
        {
            _title.text = "MINIGAME TOWER";
            _subtitle.text =
                "TURN " + match.Turn + "  ·  " +
                match.RemainingMinigameSlots + " BLOCKS LEFT";

            var visibleCount = Mathf.Clamp(
                match.RemainingMinigameSlots,
                0,
                MaximumVisibleBlocks);
            for (var index = 0; index < _blocks.Length; index++)
            {
                var active = index < visibleCount;
                if (_blocks[index].gameObject.activeSelf != active)
                {
                    _blocks[index].gameObject.SetActive(active);
                }

                if (!active)
                {
                    continue;
                }

                var current = index == 0;
                _blockLabels[index].text =
                    current && revealed
                        ? DisplayName(match.CurrentMinigame)
                        : "???";
                _blocks[index].color =
                    current
                        ? CurrentColor(match.CurrentMinigame, revealed)
                        : new Color(0.12f, 0.16f, 0.23f, 0.98f);

                var pulse = current && !revealed
                    ? 1f + Mathf.Sin(Time.unscaledTime * 10f) * 0.025f
                    : current && revealed
                        ? 1.04f
                        : 1f;
                _blocks[index].rectTransform.localScale =
                    new Vector3(pulse, pulse, 1f);
            }
        }

        private static Text CreateText(
            string name,
            Transform parent,
            Vector2 anchoredPosition,
            Vector2 size,
            int fontSize,
            FontStyle style)
        {
            var textObject = new GameObject(name);
            textObject.transform.SetParent(parent, false);
            var rect = textObject.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            var text = textObject.AddComponent<Text>();
            text.font =
                Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        private static string DisplayName(ScheduledMinigameId minigame)
        {
            switch (minigame)
            {
                case ScheduledMinigameId.Minefield: return "MINEFIELD";
                case ScheduledMinigameId.WrongWay: return "WRONG WAY";
                default: return "SKIP";
            }
        }

        private static Color CurrentColor(
            ScheduledMinigameId minigame,
            bool revealed)
        {
            if (!revealed)
            {
                return new Color(0.28f, 0.32f, 0.43f, 1f);
            }

            switch (minigame)
            {
                case ScheduledMinigameId.Minefield:
                    return new Color(0.12f, 0.58f, 0.42f, 1f);
                case ScheduledMinigameId.WrongWay:
                    return new Color(0.95f, 0.42f, 0.12f, 1f);
                default:
                    return new Color(0.44f, 0.46f, 0.52f, 1f);
            }
        }
    }
}