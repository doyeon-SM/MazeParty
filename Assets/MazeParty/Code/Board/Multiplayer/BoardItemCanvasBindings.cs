using System;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized contract owned by BoardItemCanvas.prefab. It contains item
    /// selection, inventory, utility-item and shop presentation.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardItemCanvasBindings : MonoBehaviour
    {
        [Serializable]
        public sealed class References
        {
            public Canvas RootCanvas;
            public GraphicRaycaster RootRaycaster;

            public GameObject InventoryPanel;
            public GameObject ItemSelectionPanel;
            public GameObject ItemShopPanel;

            public Text TooltipText;
            public Text InteractionPrompt;
            public Text ItemShopTitle;
            public Text ItemShopTooltip;
            public Text ItemShopStatus;

            public Button NoItemButton;
            public Button ItemShopCloseButton;

            public Image[] InventorySlotBackgrounds;
            public Image[] InventorySlotIcons;
            public Button[] ItemChoiceButtons;
            public Text[] ItemChoiceLabels;
            public BoardItemChoiceButton[] ItemChoiceHovers;

            public Button[] ShopOfferButtons;
            public Image[] ShopOfferIcons;
            public Text[] ShopOfferLabels;
            public BoardItemChoiceButton[] ShopOfferHovers;
        }

        [Serializable]
        public sealed class StatePalette
        {
            public Color InventorySelected =
                new Color(1f, 0.72f, 0.15f, 0.97f);
            public Color InventoryOccupied =
                new Color(0.18f, 0.36f, 0.58f, 0.94f);
            public Color InventoryEmpty =
                new Color(0.1f, 0.12f, 0.16f, 0.88f);
        }

        [SerializeField] private BoardChestNoticeView chestNotice;
        public BoardChestNoticeView ChestNotice => chestNotice;
        [SerializeField] private References references = new References();
        [SerializeField] private StatePalette statePalette = new StatePalette();

        public Canvas RootCanvas => references.RootCanvas;
        public GraphicRaycaster RootRaycaster => references.RootRaycaster;
        public GameObject InventoryPanel => references.InventoryPanel;
        public GameObject ItemSelectionPanel => references.ItemSelectionPanel;
        public GameObject ItemShopPanel => references.ItemShopPanel;
        public Text TooltipText => references.TooltipText;
        public Text InteractionPrompt => references.InteractionPrompt;
        public Text ItemShopTitle => references.ItemShopTitle;
        public Text ItemShopTooltip => references.ItemShopTooltip;
        public Text ItemShopStatus => references.ItemShopStatus;
        public Button NoItemButton => references.NoItemButton;
        public Button ItemShopCloseButton => references.ItemShopCloseButton;
        public Image[] InventorySlotBackgrounds =>
            references.InventorySlotBackgrounds;
        public Image[] InventorySlotIcons => references.InventorySlotIcons;
        public Button[] ItemChoiceButtons => references.ItemChoiceButtons;
        public Text[] ItemChoiceLabels => references.ItemChoiceLabels;
        public BoardItemChoiceButton[] ItemChoiceHovers =>
            references.ItemChoiceHovers;
        public Button[] ShopOfferButtons => references.ShopOfferButtons;
        public Image[] ShopOfferIcons => references.ShopOfferIcons;
        public Text[] ShopOfferLabels => references.ShopOfferLabels;
        public BoardItemChoiceButton[] ShopOfferHovers =>
            references.ShopOfferHovers;

        public Color InventorySelectedColor => statePalette.InventorySelected;
        public Color InventoryOccupiedColor => statePalette.InventoryOccupied;
        public Color InventoryEmptyColor => statePalette.InventoryEmpty;

        public bool HasRequiredReferences =>
            chestNotice != null && chestNotice.HasRequiredReferences &&
            references != null &&
            statePalette != null &&
            references.RootCanvas != null &&
            references.RootRaycaster != null &&
            references.InventoryPanel != null &&
            references.ItemSelectionPanel != null &&
            references.ItemShopPanel != null &&
            references.TooltipText != null &&
            references.InteractionPrompt != null &&
            references.ItemShopTitle != null &&
            references.ItemShopTooltip != null &&
            references.ItemShopStatus != null &&
            references.NoItemButton != null &&
            references.ItemShopCloseButton != null &&
            HasArray(
                references.InventorySlotBackgrounds,
                GameplayInventory.Capacity) &&
            HasArray(
                references.InventorySlotIcons,
                GameplayInventory.Capacity) &&
            HasArray(
                references.ItemChoiceButtons,
                GameplayInventory.Capacity) &&
            HasArray(
                references.ItemChoiceLabels,
                GameplayInventory.Capacity) &&
            HasArray(
                references.ItemChoiceHovers,
                GameplayInventory.Capacity) &&
            HasArray(references.ShopOfferButtons, ItemShopRules.OfferCount) &&
            HasArray(references.ShopOfferIcons, ItemShopRules.OfferCount) &&
            HasArray(references.ShopOfferLabels, ItemShopRules.OfferCount) &&
            HasArray(references.ShopOfferHovers, ItemShopRules.OfferCount);

        public void SetInventoryVisible(bool visible)
        {
            if (references.InventoryPanel != null && references.InventoryPanel.activeSelf != visible)
                references.InventoryPanel.SetActive(visible);
        }

        public void Configure(References targetReferences)
        {
            references = targetReferences;
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
