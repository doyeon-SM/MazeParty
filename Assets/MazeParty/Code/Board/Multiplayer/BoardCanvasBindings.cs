using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Non-visual aggregate owned by BoardCanvas.prefab. The actual presentation
    /// is authored by the HUD, map and item child Canvas prefabs.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardCanvasBindings : MonoBehaviour
    {
        [SerializeField] private BoardHudCanvasBindings hud;
        [SerializeField] private BoardMapCanvasBindings map;
        [SerializeField] private BoardItemCanvasBindings item;

        public BoardHudCanvasBindings Hud => hud;
        public BoardMapCanvasBindings Map => map;
        public BoardItemCanvasBindings Item => item;

        // Compatibility surface for consumers that need the aggregate. New
        // presentation code should prefer Hud, Map or Item explicitly.
        public Canvas RootCanvas => hud != null ? hud.RootCanvas : null;
        public GraphicRaycaster RootRaycaster =>
            hud != null ? hud.RootRaycaster : null;
        public GameObject ItemSelectionPanel =>
            item != null ? item.ItemSelectionPanel : null;
        public GameObject MinigameReadyPanel =>
            hud != null ? hud.MinigameReadyPanel : null;
        public GameObject ReconnectOverlay =>
            hud != null ? hud.ReconnectOverlay : null;
        public GameObject Reticle => hud != null ? hud.Reticle : null;
        public GameObject ItemShopPanel =>
            item != null ? item.ItemShopPanel : null;
        public GameObject BoardEventPopupPanel =>
            hud != null ? hud.BoardEventPopupPanel : null;
        public GameObject BoardKillFeedPanel =>
            hud != null ? hud.BoardKillFeedPanel : null;
        public Image ReticleImage =>
            hud != null ? hud.ReticleImage : null;
        public Text TurnText => hud != null ? hud.TurnText : null;
        public Text PhaseText => hud != null ? hud.PhaseText : null;
        public Text PhaseTimerText => hud != null ? hud.PhaseTimerText : null;
        public Text MovesText => map != null ? map.MovesText : null;
        public Text AmmoText => item != null ? item.AmmoText : null;
        public Text StatusText => hud != null ? hud.StatusText : null;
        public Text TooltipText => item != null ? item.TooltipText : null;
        public Text InteractionPrompt =>
            item != null ? item.InteractionPrompt : null;
        public Text ReconnectText => hud != null ? hud.ReconnectText : null;
        public Text ItemShopTitle =>
            item != null ? item.ItemShopTitle : null;
        public Text ItemShopTooltip =>
            item != null ? item.ItemShopTooltip : null;
        public Text ItemShopStatus =>
            item != null ? item.ItemShopStatus : null;
        public Text BoardEventPopupMessage =>
            hud != null ? hud.BoardEventPopupMessage : null;
        public Text BoardKillFeedMessage =>
            hud != null ? hud.BoardKillFeedMessage : null;
        public Text MinigameReadyTitle =>
            hud != null ? hud.MinigameReadyTitle : null;
        public Text MinigameDescription =>
            hud != null ? hud.MinigameDescription : null;
        public Text MinigameReadyStatus =>
            hud != null ? hud.MinigameReadyStatus : null;
        public Text[] MinigameReadyPlayerStates =>
            hud != null ? hud.MinigameReadyPlayerStates : null;
        public Text MinigamePreviewPlaceholder =>
            hud != null ? hud.MinigamePreviewPlaceholder : null;
        public TMP_Text[] MinigameControlRows =>
            hud != null ? hud.MinigameControlRows : null;
        public Text ReadyButtonLabel =>
            hud != null ? hud.ReadyButtonLabel : null;
        public RawImage MinigamePreviewImage =>
            hud != null ? hud.MinigamePreviewImage : null;
        public VideoPlayer MinigamePreviewPlayer =>
            hud != null ? hud.MinigamePreviewPlayer : null;
        public Button NoItemButton =>
            item != null ? item.NoItemButton : null;
        public Button ReadyButton => hud != null ? hud.ReadyButton : null;
        public Button ItemShopCloseButton =>
            item != null ? item.ItemShopCloseButton : null;
        public Image[] InventorySlotBackgrounds =>
            item != null ? item.InventorySlotBackgrounds : null;
        public Image[] InventorySlotIcons =>
            item != null ? item.InventorySlotIcons : null;
        public Button[] ItemChoiceButtons =>
            item != null ? item.ItemChoiceButtons : null;
        public Text[] ItemChoiceLabels =>
            item != null ? item.ItemChoiceLabels : null;
        public BoardItemChoiceButton[] ItemChoiceHovers =>
            item != null ? item.ItemChoiceHovers : null;
        public Button[] ShopOfferButtons =>
            item != null ? item.ShopOfferButtons : null;
        public Image[] ShopOfferIcons =>
            item != null ? item.ShopOfferIcons : null;
        public Text[] ShopOfferLabels =>
            item != null ? item.ShopOfferLabels : null;
        public BoardItemChoiceButton[] ShopOfferHovers =>
            item != null ? item.ShopOfferHovers : null;
        public Text[] PlayerRows => hud != null ? hud.PlayerRows : null;
        public Image[] PlayerCards => hud != null ? hud.PlayerCards : null;
        public Slider[] PlayerHealthSliders =>
            hud != null ? hud.PlayerHealthSliders : null;
        public Image[] PlayerHealthFills =>
            hud != null ? hud.PlayerHealthFills : null;
        public Text[] PlayerHealthTexts =>
            hud != null ? hud.PlayerHealthTexts : null;
        public RawImage[] PlayerKeyIcons =>
            hud != null ? hud.PlayerKeyIcons : null;
        public Text[] PlayerKeyTexts =>
            hud != null ? hud.PlayerKeyTexts : null;
        public RawImage[] PlayerGoldIcons =>
            hud != null ? hud.PlayerGoldIcons : null;
        public Text[] PlayerGoldTexts =>
            hud != null ? hud.PlayerGoldTexts : null;
        public Text[] PlayerActionIcons =>
            hud != null ? hud.PlayerActionIcons : null;
        public Text[] PlayerRankTexts =>
            hud != null ? hud.PlayerRankTexts : null;

        public Color InventorySelectedColor => item != null
            ? item.InventorySelectedColor
            : Color.white;
        public Color InventoryOccupiedColor => item != null
            ? item.InventoryOccupiedColor
            : Color.white;
        public Color InventoryEmptyColor => item != null
            ? item.InventoryEmptyColor
            : Color.clear;
        public Color DisconnectedPlayerColor => hud != null
            ? hud.DisconnectedPlayerColor
            : Color.white;
        public Color HealthyHealthColor => hud != null
            ? hud.HealthyHealthColor
            : Color.green;
        public Color WoundedHealthColor => hud != null
            ? hud.WoundedHealthColor
            : Color.yellow;
        public Color CriticalHealthColor => hud != null
            ? hud.CriticalHealthColor
            : Color.red;
        public Color CombatOutColor => hud != null
            ? hud.CombatOutColor
            : Color.red;
        public Color ReadyPlayerWaitingColor => hud != null
            ? hud.ReadyPlayerWaitingColor
            : Color.white;
        public Color ReadyPlayerCompleteColor => hud != null
            ? hud.ReadyPlayerCompleteColor
            : Color.green;
        public Color ReticleDefaultColor =>
            hud != null ? hud.ReticleDefaultColor : Color.white;
        public Color ReticleDamageableTargetColor =>
            hud != null ? hud.ReticleDamageableTargetColor : Color.red;

        public static int RequiredMinigamePreviewClipSlotCount =>
            BoardHudCanvasBindings.RequiredMinigamePreviewClipSlotCount;

        public int MinigamePreviewClipSlotCount =>
            hud != null ? hud.MinigamePreviewClipSlotCount : 0;

        public VideoClip GetMinigamePreviewClip(ScheduledMinigameId minigame)
        {
            return hud != null ? hud.GetMinigamePreviewClip(minigame) : null;
        }

        public Color GetPlayerColor(int slot)
        {
            return hud != null ? hud.GetPlayerColor(slot) : Color.white;
        }

        public Color GetPlayerCardColor(int slot)
        {
            return hud != null ? hud.GetPlayerCardColor(slot) : Color.white;
        }

        public Color GetPlayerCardColor(Color playerColor)
        {
            return hud != null
                ? hud.GetPlayerCardColor(playerColor)
                : playerColor;
        }

        public Color GetPlayerTextColor(Color playerColor)
        {
            return hud != null
                ? hud.GetPlayerTextColor(playerColor)
                : playerColor;
        }

        public Color GetActionIconColor(PlayerBoardActionState state)
        {
            return hud != null ? hud.GetActionIconColor(state) : Color.clear;
        }

        public bool HasRequiredReferences =>
            hud != null && hud.HasRequiredReferences &&
            map != null && map.HasRequiredReferences &&
            item != null && item.HasRequiredReferences;

        public void Configure(
            BoardHudCanvasBindings hudBindings,
            BoardMapCanvasBindings mapBindings,
            BoardItemCanvasBindings itemBindings)
        {
            hud = hudBindings;
            map = mapBindings;
            item = itemBindings;
        }

        internal static Color EnsureMinimumValue(
            Color color,
            float minimumValue)
        {
            Color.RGBToHSV(color, out var hue, out var saturation, out var value);
            var result = Color.HSVToRGB(
                hue,
                saturation,
                Mathf.Max(value, Mathf.Clamp01(minimumValue)));
            result.a = color.a;
            return result;
        }
    }
}
