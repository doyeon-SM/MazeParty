using System;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized contract owned by BoardCanvas.prefab. Runtime presenters may
    /// update state through these references, but layout and visual values stay
    /// authored on the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardCanvasBindings : MonoBehaviour
    {
        [Serializable]
        public sealed class References
        {
            public Canvas RootCanvas;
            public GraphicRaycaster RootRaycaster;

            public GameObject ItemSelectionPanel;
            public GameObject MinigameReadyPanel;
            public GameObject ResultPanel;
            public GameObject ReconnectOverlay;
            public GameObject Reticle;
            public GameObject ItemShopPanel;

            public Text TurnText;
            public Text PhaseText;
            public Text PhaseTimerText;
            public Text ChoiceTimerText;
            public Text ShieldText;
            public Text DiceText;
            public Text MovesText;
            public Text AmmoText;
            public Text StatusText;
            public Text TooltipText;
            public Text ReconnectText;
            public Text ItemShopTitle;
            public Text ItemShopTooltip;
            public Text ItemShopStatus;
            public Text MinigameReadyTitle;
            public Text MinigameReadyNote;
            public Text MinigameReadyStatus;
            public Text MinigameRulePlaceholder;
            public Text ResultTitle;
            public Text ResultNote;
            public Text ResultSummary;
            public Text ReadyButtonLabel;

            public Image MinigameRuleImage;
            public Button NoItemButton;
            public Button ReadyButton;
            public Button ItemShopCloseButton;

            public Image[] InventorySlotBackgrounds;
            public Text[] InventorySlotLabels;
            public Button[] ItemChoiceButtons;
            public Text[] ItemChoiceLabels;
            public BoardItemChoiceButton[] ItemChoiceHovers;

            public Button[] ShopOfferButtons;
            public Text[] ShopOfferLabels;
            public BoardItemChoiceButton[] ShopOfferHovers;

            public Text[] PlayerRows;
            public Image[] PlayerCards;
            public Image[] PlayerHealthFills;
            public Text[] PlayerHealthTexts;
            public Text[] PlayerCurrencyTexts;
            public Text[] PlayerActionIcons;
            public Text[] PlayerRankTexts;
        }

        [Serializable]
        public sealed class StatePalette
        {
            public Color ShieldActive = new Color(0.25f, 1f, 0.75f);
            public Color ShieldInactive = new Color(1f, 0.45f, 0.45f);
            public Color InventorySelected =
                new Color(1f, 0.72f, 0.15f, 0.97f);
            public Color InventoryOccupied =
                new Color(0.18f, 0.36f, 0.58f, 0.94f);
            public Color InventoryEmpty =
                new Color(0.1f, 0.12f, 0.16f, 0.88f);
            public Color DisconnectedPlayer =
                new Color(1f, 0.45f, 0.35f);
            public Color LocalPlayerCard =
                new Color(0.16f, 0.3f, 0.5f, 0.98f);
            public Color RemotePlayerCard =
                new Color(0.055f, 0.085f, 0.13f, 0.94f);
            public Color HealthyHealth =
                new Color(0.2f, 0.82f, 0.38f, 1f);
            public Color WoundedHealth =
                new Color(1f, 0.7f, 0.16f, 1f);
            public Color CriticalHealth =
                new Color(0.95f, 0.2f, 0.2f, 1f);
            public Color CombatOut = new Color(1f, 0.25f, 0.2f);
            public Color RuleImageContent = Color.white;
            public Color RuleImagePlaceholder =
                new Color(0.055f, 0.09f, 0.14f, 1f);
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

        public Canvas RootCanvas => references.RootCanvas;
        public GraphicRaycaster RootRaycaster => references.RootRaycaster;
        public GameObject ItemSelectionPanel => references.ItemSelectionPanel;
        public GameObject MinigameReadyPanel => references.MinigameReadyPanel;
        public GameObject ResultPanel => references.ResultPanel;
        public GameObject ReconnectOverlay => references.ReconnectOverlay;
        public GameObject Reticle => references.Reticle;
        public GameObject ItemShopPanel => references.ItemShopPanel;
        public Text TurnText => references.TurnText;
        public Text PhaseText => references.PhaseText;
        public Text PhaseTimerText => references.PhaseTimerText;
        public Text ChoiceTimerText => references.ChoiceTimerText;
        public Text ShieldText => references.ShieldText;
        public Text DiceText => references.DiceText;
        public Text MovesText => references.MovesText;
        public Text AmmoText => references.AmmoText;
        public Text StatusText => references.StatusText;
        public Text TooltipText => references.TooltipText;
        public Text ReconnectText => references.ReconnectText;
        public Text ItemShopTitle => references.ItemShopTitle;
        public Text ItemShopTooltip => references.ItemShopTooltip;
        public Text ItemShopStatus => references.ItemShopStatus;
        public Text MinigameReadyTitle => references.MinigameReadyTitle;
        public Text MinigameReadyNote => references.MinigameReadyNote;
        public Text MinigameReadyStatus => references.MinigameReadyStatus;
        public Text MinigameRulePlaceholder =>
            references.MinigameRulePlaceholder;
        public Text ResultTitle => references.ResultTitle;
        public Text ResultNote => references.ResultNote;
        public Text ResultSummary => references.ResultSummary;
        public Text ReadyButtonLabel => references.ReadyButtonLabel;
        public Image MinigameRuleImage => references.MinigameRuleImage;
        public Button NoItemButton => references.NoItemButton;
        public Button ReadyButton => references.ReadyButton;
        public Button ItemShopCloseButton => references.ItemShopCloseButton;
        public Image[] InventorySlotBackgrounds =>
            references.InventorySlotBackgrounds;
        public Text[] InventorySlotLabels => references.InventorySlotLabels;
        public Button[] ItemChoiceButtons => references.ItemChoiceButtons;
        public Text[] ItemChoiceLabels => references.ItemChoiceLabels;
        public BoardItemChoiceButton[] ItemChoiceHovers =>
            references.ItemChoiceHovers;
        public Button[] ShopOfferButtons => references.ShopOfferButtons;
        public Text[] ShopOfferLabels => references.ShopOfferLabels;
        public BoardItemChoiceButton[] ShopOfferHovers =>
            references.ShopOfferHovers;
        public Text[] PlayerRows => references.PlayerRows;
        public Image[] PlayerCards => references.PlayerCards;
        public Image[] PlayerHealthFills => references.PlayerHealthFills;
        public Text[] PlayerHealthTexts => references.PlayerHealthTexts;
        public Text[] PlayerCurrencyTexts => references.PlayerCurrencyTexts;
        public Text[] PlayerActionIcons => references.PlayerActionIcons;
        public Text[] PlayerRankTexts => references.PlayerRankTexts;

        public Color ShieldActiveColor => statePalette.ShieldActive;
        public Color ShieldInactiveColor => statePalette.ShieldInactive;
        public Color InventorySelectedColor => statePalette.InventorySelected;
        public Color InventoryOccupiedColor => statePalette.InventoryOccupied;
        public Color InventoryEmptyColor => statePalette.InventoryEmpty;
        public Color DisconnectedPlayerColor => statePalette.DisconnectedPlayer;
        public Color LocalPlayerCardColor => statePalette.LocalPlayerCard;
        public Color RemotePlayerCardColor => statePalette.RemotePlayerCard;
        public Color HealthyHealthColor => statePalette.HealthyHealth;
        public Color WoundedHealthColor => statePalette.WoundedHealth;
        public Color CriticalHealthColor => statePalette.CriticalHealth;
        public Color CombatOutColor => statePalette.CombatOut;
        public Color RuleImageContentColor => statePalette.RuleImageContent;
        public Color RuleImagePlaceholderColor =>
            statePalette.RuleImagePlaceholder;

        public bool HasRequiredReferences =>
            references != null &&
            statePalette != null &&
            references.RootCanvas != null &&
            references.RootRaycaster != null &&
            references.ItemSelectionPanel != null &&
            references.MinigameReadyPanel != null &&
            references.ResultPanel != null &&
            references.ReconnectOverlay != null &&
            references.Reticle != null &&
            references.ItemShopPanel != null &&
            references.TurnText != null &&
            references.PhaseText != null &&
            references.PhaseTimerText != null &&
            references.ChoiceTimerText != null &&
            references.ShieldText != null &&
            references.DiceText != null &&
            references.MovesText != null &&
            references.AmmoText != null &&
            references.StatusText != null &&
            references.TooltipText != null &&
            references.ReconnectText != null &&
            references.ItemShopTitle != null &&
            references.ItemShopTooltip != null &&
            references.ItemShopStatus != null &&
            references.MinigameReadyTitle != null &&
            references.MinigameReadyNote != null &&
            references.MinigameReadyStatus != null &&
            references.MinigameRulePlaceholder != null &&
            references.ResultTitle != null &&
            references.ResultNote != null &&
            references.ResultSummary != null &&
            references.ReadyButtonLabel != null &&
            references.MinigameRuleImage != null &&
            references.NoItemButton != null &&
            references.ReadyButton != null &&
            references.ItemShopCloseButton != null &&
            HasArray(references.InventorySlotBackgrounds, GameplayInventory.Capacity) &&
            HasArray(references.InventorySlotLabels, GameplayInventory.Capacity) &&
            HasArray(references.ItemChoiceButtons, GameplayInventory.Capacity) &&
            HasArray(references.ItemChoiceLabels, GameplayInventory.Capacity) &&
            HasArray(references.ItemChoiceHovers, GameplayInventory.Capacity) &&
            HasArray(references.ShopOfferButtons, ItemShopRules.OfferCount) &&
            HasArray(references.ShopOfferLabels, ItemShopRules.OfferCount) &&
            HasArray(references.ShopOfferHovers, ItemShopRules.OfferCount) &&
            HasArray(references.PlayerRows, MultiplayerConstants.MaxPlayers) &&
            HasArray(references.PlayerCards, MultiplayerConstants.MaxPlayers) &&
            HasArray(references.PlayerHealthFills, MultiplayerConstants.MaxPlayers) &&
            HasArray(references.PlayerHealthTexts, MultiplayerConstants.MaxPlayers) &&
            HasArray(references.PlayerCurrencyTexts, MultiplayerConstants.MaxPlayers) &&
            HasArray(references.PlayerActionIcons, MultiplayerConstants.MaxPlayers) &&
            HasArray(references.PlayerRankTexts, MultiplayerConstants.MaxPlayers) &&
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
