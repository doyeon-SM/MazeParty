using System;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Gameplay.Testbed
{
    /// <summary>
    /// Serialized design contract owned by GameplayTestbedCanvas.prefab.
    /// Gameplay code only reads and updates these references; hierarchy,
    /// layout and styling remain editable in the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayTestbedUiBindings : MonoBehaviour
    {
        [Serializable]
        public sealed class StatePalette
        {
            public Color ShieldActive = new Color(0.25f, 1f, 0.75f);
            public Color ShieldInactive = new Color(1f, 0.45f, 0.45f);
            public Color InventorySelected =
                new Color(1f, 0.72f, 0.15f, 0.96f);
            public Color InventoryEmpty =
                new Color(0.12f, 0.14f, 0.18f, 0.9f);
            public Color InventoryOccupied =
                new Color(0.18f, 0.32f, 0.5f, 0.94f);
        }

        [Header("Panels")]
        [SerializeField] private GameObject selectionPanel;

        [Header("Status text")]
        [SerializeField] private Text actionTimerText;
        [SerializeField] private Text shieldTimerText;
        [SerializeField] private Text choiceTimerText;
        [SerializeField] private Text healthText;
        [SerializeField] private Text ammoText;
        [SerializeField] private Text statusText;
        [SerializeField] private Text tooltipText;

        [Header("Inventory")]
        [SerializeField] private Image[] slotBackgrounds =
            new Image[GameplayInventory.Capacity];
        [SerializeField] private Text[] slotLabels =
            new Text[GameplayInventory.Capacity];
        [SerializeField] private Button[] choiceButtons =
            new Button[GameplayInventory.Capacity];
        [SerializeField] private Text[] choiceButtonLabels =
            new Text[GameplayInventory.Capacity];
        [SerializeField] private TestbedItemChoiceButton[] choicePresenters =
            new TestbedItemChoiceButton[GameplayInventory.Capacity];

        [Header("Actions")]
        [SerializeField] private Button startActionButton;
        [SerializeField] private Button noItemButton;
        [SerializeField] private Button incomingHitButton;
        [SerializeField] private Button incomingPushButton;
        [SerializeField] private Button addRewardButton;
        [SerializeField] private Button endActionButton;
        [SerializeField] private Button resetButton;

        [Header("Camera modes")]
        [SerializeField] private Button firstPersonButton;
        [SerializeField] private Button boardButton;
        [SerializeField] private Button minigameButton;

        [Header("State palette")]
        [SerializeField] private StatePalette statePalette = new StatePalette();

        public GameObject SelectionPanel => selectionPanel;
        public Text ActionTimerText => actionTimerText;
        public Text ShieldTimerText => shieldTimerText;
        public Text ChoiceTimerText => choiceTimerText;
        public Text HealthText => healthText;
        public Text AmmoText => ammoText;
        public Text StatusText => statusText;
        public Text TooltipText => tooltipText;
        public Image[] SlotBackgrounds => slotBackgrounds;
        public Text[] SlotLabels => slotLabels;
        public Button[] ChoiceButtons => choiceButtons;
        public Text[] ChoiceButtonLabels => choiceButtonLabels;
        public TestbedItemChoiceButton[] ChoicePresenters => choicePresenters;
        public Button StartActionButton => startActionButton;
        public Button NoItemButton => noItemButton;
        public Button IncomingHitButton => incomingHitButton;
        public Button IncomingPushButton => incomingPushButton;
        public Button AddRewardButton => addRewardButton;
        public Button EndActionButton => endActionButton;
        public Button ResetButton => resetButton;
        public Button FirstPersonButton => firstPersonButton;
        public Button BoardButton => boardButton;
        public Button MinigameButton => minigameButton;
        public Color ShieldActiveColor => statePalette.ShieldActive;
        public Color ShieldInactiveColor => statePalette.ShieldInactive;
        public Color InventorySelectedColor => statePalette.InventorySelected;
        public Color InventoryEmptyColor => statePalette.InventoryEmpty;
        public Color InventoryOccupiedColor => statePalette.InventoryOccupied;

        public bool HasRequiredReferences =>
            selectionPanel != null &&
            actionTimerText != null &&
            shieldTimerText != null &&
            choiceTimerText != null &&
            healthText != null &&
            ammoText != null &&
            statusText != null &&
            tooltipText != null &&
            HasCompleteArray(slotBackgrounds) &&
            HasCompleteArray(slotLabels) &&
            HasCompleteArray(choiceButtons) &&
            HasCompleteArray(choiceButtonLabels) &&
            HasCompleteArray(choicePresenters) &&
            startActionButton != null &&
            noItemButton != null &&
            incomingHitButton != null &&
            incomingPushButton != null &&
            addRewardButton != null &&
            endActionButton != null &&
            resetButton != null &&
            firstPersonButton != null &&
            boardButton != null &&
            minigameButton != null &&
            statePalette != null;

        public void Configure(
            GameObject targetSelectionPanel,
            Text targetActionTimerText,
            Text targetShieldTimerText,
            Text targetChoiceTimerText,
            Text targetHealthText,
            Text targetAmmoText,
            Text targetStatusText,
            Text targetTooltipText,
            Image[] targetSlotBackgrounds,
            Text[] targetSlotLabels,
            Button[] targetChoiceButtons,
            Text[] targetChoiceButtonLabels,
            TestbedItemChoiceButton[] targetChoicePresenters,
            Button targetStartActionButton,
            Button targetNoItemButton,
            Button targetIncomingHitButton,
            Button targetIncomingPushButton,
            Button targetAddRewardButton,
            Button targetEndActionButton,
            Button targetResetButton,
            Button targetFirstPersonButton,
            Button targetBoardButton,
            Button targetMinigameButton)
        {
            selectionPanel = targetSelectionPanel;
            actionTimerText = targetActionTimerText;
            shieldTimerText = targetShieldTimerText;
            choiceTimerText = targetChoiceTimerText;
            healthText = targetHealthText;
            ammoText = targetAmmoText;
            statusText = targetStatusText;
            tooltipText = targetTooltipText;
            slotBackgrounds = targetSlotBackgrounds;
            slotLabels = targetSlotLabels;
            choiceButtons = targetChoiceButtons;
            choiceButtonLabels = targetChoiceButtonLabels;
            choicePresenters = targetChoicePresenters;
            startActionButton = targetStartActionButton;
            noItemButton = targetNoItemButton;
            incomingHitButton = targetIncomingHitButton;
            incomingPushButton = targetIncomingPushButton;
            addRewardButton = targetAddRewardButton;
            endActionButton = targetEndActionButton;
            resetButton = targetResetButton;
            firstPersonButton = targetFirstPersonButton;
            boardButton = targetBoardButton;
            minigameButton = targetMinigameButton;
        }

        private static bool HasCompleteArray<T>(T[] references)
            where T : UnityEngine.Object
        {
            if (references == null ||
                references.Length != GameplayInventory.Capacity)
            {
                return false;
            }

            for (var index = 0; index < references.Length; index++)
            {
                if (references[index] == null)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
