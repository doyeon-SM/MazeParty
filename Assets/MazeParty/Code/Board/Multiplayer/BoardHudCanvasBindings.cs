using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized contract owned by BoardHudCanvas.prefab. Runtime presenters
    /// update state through these references; layout remains prefab-authored.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed partial class BoardHudCanvasBindings : MonoBehaviour
    {
        [Serializable]
        public sealed class References
        {
            public Canvas RootCanvas;
            public GraphicRaycaster RootRaycaster;

            public GameObject MinigameReadyPanel;
            public GameObject ReconnectOverlay;
            public GameObject Reticle;
            public GameObject BoardEventPopupPanel;
            public BoardEventPanelView EventPanelView;
            public GameObject BoardKillFeedPanel;

            public Image ReticleImage;
            public Text TurnText;
            public Text PhaseText;
            public Text PhaseTimerText;
            public Text StatusText;
            public Text ReconnectText;
            public TMP_Text BoardEventPopupMessage;
            public Text BoardKillFeedMessage;
            public Text MinigameReadyTitle;
            public Text MinigameDescription;
            public Text MinigameReadyStatus;
            public Text[] MinigameReadyPlayerStates;
            public Text MinigamePreviewPlaceholder;
            public TMP_Text[] MinigameControlRows;
            public Text ReadyButtonLabel;

            public RawImage MinigamePreviewImage;
            public VideoPlayer MinigamePreviewPlayer;
            public Button ReadyButton;

            public Text[] PlayerRows;
            public Image[] PlayerCards;
            public VerticalLayoutGroup PlayerRankingLayout;
            public Slider[] PlayerHealthSliders;
            public BoardHudStatFeedbackView[] PlayerStatFeedback;
            public Image[] PlayerHealthFills;
            public Text[] PlayerHealthTexts;
            public Text[] PlayerHealthDeltaTexts;
            public RawImage[] PlayerKeyIcons;
            public Text[] PlayerKeyTexts;
            public Text[] PlayerKeyDeltaTexts;
            public RawImage[] PlayerGoldIcons;
            public Text[] PlayerGoldTexts;
            public Text[] PlayerGoldDeltaTexts;
            public Text[] PlayerActionIcons;
            public Text[] PlayerRankTexts;
        }

        [Serializable]
        public sealed class StatePalette
        {
            public Color DisconnectedPlayer =
                new Color(1f, 0.45f, 0.35f);
            public Color LocalPlayerCard =
                new Color(0.16f, 0.3f, 0.5f, 0.98f);
            public Color RemotePlayerCard =
                new Color(0.055f, 0.085f, 0.13f, 0.94f);
            [Range(0f, 1f)] public float PlayerCardDarken = 0.62f;
            [Range(0f, 1f)] public float PlayerCardAlpha = 0.94f;
            [Range(0f, 1f)] public float PlayerTextMinimumValue = 0.72f;
            public Color HealthyHealth =
                new Color(0.2f, 0.82f, 0.38f, 1f);
            public Color WoundedHealth =
                new Color(1f, 0.7f, 0.16f, 1f);
            public Color CriticalHealth =
                new Color(0.95f, 0.2f, 0.2f, 1f);
            public Color CombatOut = new Color(1f, 0.25f, 0.2f);
            public Color ReadyPlayerWaiting =
                new Color(0.68f, 0.74f, 0.82f, 1f);
            public Color ReadyPlayerComplete =
                new Color(0.35f, 1f, 0.55f, 1f);
            public Color ReticleDefault =
                new Color(0.93f, 0.96f, 1f, 1f);
            public Color ReticleDamageableTarget =
                new Color(1f, 0.2f, 0.2f, 1f);
            public Color[] Players =
            {
                new Color(1f, 0.42f, 0.42f),
                new Color(0.42f, 0.7f, 1f),
                new Color(0.42f, 1f, 0.58f),
                new Color(1f, 0.82f, 0.35f)
            };
            public Color ActionDice = new Color(0.4f, 0.75f, 1f);
            public Color ActionMoving = new Color(0.35f, 1f, 0.55f);
            public Color ActionArrived = new Color(1f, 0.82f, 0.3f);
            public Color ActionFighting = new Color(1f, 0.3f, 0.25f);
            public Color ActionHidden = Color.clear;
        }

        [SerializeField] private References references = new References();
        [SerializeField] private StatePalette statePalette = new StatePalette();
        // Index matches the append-only ScheduledMinigameId value. Clips are
        // intentionally optional until each preview has been captured.
        [SerializeField] private VideoClip[] minigamePreviewClips =
            Array.Empty<VideoClip>();

        public Canvas RootCanvas => references.RootCanvas;
        public GraphicRaycaster RootRaycaster => references.RootRaycaster;
        public GameObject MinigameReadyPanel => references.MinigameReadyPanel;
        public GameObject ReconnectOverlay => references.ReconnectOverlay;
        public GameObject Reticle => references.Reticle;
        public GameObject BoardEventPopupPanel =>
            references.BoardEventPopupPanel;
        public GameObject BoardKillFeedPanel => references.BoardKillFeedPanel;
        public Image ReticleImage => references.ReticleImage;
        public Text TurnText => references.TurnText;
        public Text PhaseText => references.PhaseText;
        public Text PhaseTimerText => references.PhaseTimerText;
        public Text StatusText => references.StatusText;
        public Text ReconnectText => references.ReconnectText;
        public BoardEventPanelView EventPanelView => references.EventPanelView;
        public TMP_Text BoardEventPopupMessage =>
            references.BoardEventPopupMessage;
        public Text BoardKillFeedMessage => references.BoardKillFeedMessage;
        public Text MinigameReadyTitle => references.MinigameReadyTitle;
        public Text MinigameDescription => references.MinigameDescription;
        public Text MinigameReadyStatus => references.MinigameReadyStatus;
        public Text[] MinigameReadyPlayerStates =>
            references.MinigameReadyPlayerStates;
        public Text MinigamePreviewPlaceholder =>
            references.MinigamePreviewPlaceholder;
        public TMP_Text[] MinigameControlRows => references.MinigameControlRows;
        public Text ReadyButtonLabel => references.ReadyButtonLabel;
        public RawImage MinigamePreviewImage => references.MinigamePreviewImage;
        public VideoPlayer MinigamePreviewPlayer =>
            references.MinigamePreviewPlayer;
        public Button ReadyButton => references.ReadyButton;
        public Text[] PlayerRows => references.PlayerRows;
        public Image[] PlayerCards => references.PlayerCards;
        public Slider[] PlayerHealthSliders => references.PlayerHealthSliders;
        public BoardHudStatFeedbackView[] PlayerStatFeedback => references.PlayerStatFeedback;
        public Image[] PlayerHealthFills => references.PlayerHealthFills;
        public Text[] PlayerHealthTexts => references.PlayerHealthTexts;
        public Text[] PlayerHealthDeltaTexts => references.PlayerHealthDeltaTexts;
        public RawImage[] PlayerKeyIcons => references.PlayerKeyIcons;
        public Text[] PlayerKeyTexts => references.PlayerKeyTexts;
        public Text[] PlayerKeyDeltaTexts => references.PlayerKeyDeltaTexts;
        public RawImage[] PlayerGoldIcons => references.PlayerGoldIcons;
        public Text[] PlayerGoldTexts => references.PlayerGoldTexts;
        public Text[] PlayerGoldDeltaTexts => references.PlayerGoldDeltaTexts;
        public Text[] PlayerActionIcons => references.PlayerActionIcons;
        public Text[] PlayerRankTexts => references.PlayerRankTexts;

        public Color DisconnectedPlayerColor =>
            statePalette.DisconnectedPlayer;
        public Color LocalPlayerCardColor => statePalette.LocalPlayerCard;
        public Color RemotePlayerCardColor => statePalette.RemotePlayerCard;
        public Color HealthyHealthColor => statePalette.HealthyHealth;
        public Color WoundedHealthColor => statePalette.WoundedHealth;
        public Color CriticalHealthColor => statePalette.CriticalHealth;
        public Color CombatOutColor => statePalette.CombatOut;
        public Color ReadyPlayerWaitingColor =>
            statePalette.ReadyPlayerWaiting;
        public Color ReadyPlayerCompleteColor =>
            statePalette.ReadyPlayerComplete;
        public Color ReticleDefaultColor => statePalette.ReticleDefault;
        public Color ReticleDamageableTargetColor =>
            statePalette.ReticleDamageableTarget;

        public static int RequiredMinigamePreviewClipSlotCount
        {
            get
            {
                var highestId = 0;
                foreach (var definition in MinigameCatalog.RegisteredMinigames)
                {
                    highestId = Math.Max(highestId, (int)definition.Id);
                }

                return highestId + 1;
            }
        }

        public int MinigamePreviewClipSlotCount =>
            minigamePreviewClips != null ? minigamePreviewClips.Length : 0;

        public VideoClip GetMinigamePreviewClip(ScheduledMinigameId minigame)
        {
            var index = (int)minigame;
            return index > 0 &&
                   minigamePreviewClips != null &&
                   index < minigamePreviewClips.Length
                ? minigamePreviewClips[index]
                : null;
        }

        public bool HasRequiredReferences =>
            references != null &&
            statePalette != null &&
            references.RootCanvas != null &&
            references.RootRaycaster != null &&
            references.MinigameReadyPanel != null &&
            references.ReconnectOverlay != null &&
            references.Reticle != null &&
            references.BoardEventPopupPanel != null &&
            references.EventPanelView != null && references.EventPanelView.HasRequiredReferences &&
            references.BoardKillFeedPanel != null &&
            references.ReticleImage != null &&
            references.TurnText != null &&
            references.PhaseText != null &&
            references.PhaseTimerText != null &&
            references.ReconnectText != null &&
            references.BoardEventPopupMessage != null &&
            references.BoardKillFeedMessage != null &&
            references.MinigameReadyTitle != null &&
            references.MinigameDescription != null &&
            references.MinigameReadyStatus != null &&
            HasArray(
                references.MinigameReadyPlayerStates,
                MultiplayerConstants.MaxPlayers) &&
            references.MinigamePreviewPlaceholder != null &&
            HasArray(references.MinigameControlRows, 4) &&
            references.ReadyButtonLabel != null &&
            references.MinigamePreviewImage != null &&
            references.MinigamePreviewPlayer != null &&
            minigamePreviewClips != null &&
            minigamePreviewClips.Length >=
                RequiredMinigamePreviewClipSlotCount &&
            references.ReadyButton != null &&
            references.PlayerRankingLayout != null &&
            HasArray(references.PlayerRows, MultiplayerConstants.MaxPlayers) &&
            HasArray(references.PlayerCards, MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerHealthSliders,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(references.PlayerStatFeedback, MultiplayerConstants.MaxPlayers) &&
            Array.TrueForAll(references.PlayerStatFeedback, value => value.HasRequiredReferences) &&
            HasArray(
                references.PlayerHealthFills,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerHealthTexts,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerHealthDeltaTexts,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerKeyIcons,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerKeyTexts,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerKeyDeltaTexts,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerGoldIcons,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerGoldTexts,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerGoldDeltaTexts,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerActionIcons,
                MultiplayerConstants.MaxPlayers) &&
            HasArray(
                references.PlayerRankTexts,
                MultiplayerConstants.MaxPlayers) &&
            statePalette.Players != null &&
            statePalette.Players.Length == MultiplayerConstants.MaxPlayers;

        public void Configure(References targetReferences)
        {
            references = targetReferences;
        }

        public Color GetPlayerColor(int slot)
        {
            return slot >= 0 && slot < statePalette.Players.Length
                ? statePalette.Players[slot]
                : statePalette.Players[statePalette.Players.Length - 1];
        }

        public Color GetPlayerCardColor(int slot)
        {
            return GetPlayerCardColor(GetPlayerColor(slot));
        }

        public Color GetPlayerCardColor(Color playerColor)
        {
            var brightness = 1f - Mathf.Clamp01(statePalette.PlayerCardDarken);
            return new Color(
                playerColor.r * brightness,
                playerColor.g * brightness,
                playerColor.b * brightness,
                Mathf.Clamp01(statePalette.PlayerCardAlpha));
        }

        public Color GetPlayerTextColor(Color playerColor)
        {
            return BoardCanvasBindings.EnsureMinimumValue(
                playerColor,
                statePalette.PlayerTextMinimumValue);
        }

        public Color GetActionIconColor(PlayerBoardActionState state)
        {
            switch (state)
            {
                case PlayerBoardActionState.Dice:
                    return statePalette.ActionDice;
                case PlayerBoardActionState.Moving:
                    return statePalette.ActionMoving;
                case PlayerBoardActionState.Arrived:
                    return statePalette.ActionArrived;
                case PlayerBoardActionState.Fighting:
                    return statePalette.ActionFighting;
                default:
                    return statePalette.ActionHidden;
            }
        }

        private static bool HasArray<T>(T[] values, int expectedLength)
            where T : UnityEngine.Object
        {
            if (values == null || values.Length != expectedLength)
            {
                return false;
            }

            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] == null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
