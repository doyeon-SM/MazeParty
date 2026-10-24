using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed class CeremonyStatisticsBindings : MonoBehaviour
    {
        public Canvas RootCanvas;
        public GraphicRaycaster RootRaycaster;
        public Button OpenButton, CloseButton, GoldOption;
        public GameObject PopupRoot, TooltipRoot;
        public Button[] Tabs;
        public Text Title, OpenButtonText, Countdown, Description, GoldOptionText, AxisTitle, TooltipTitle, TooltipText;
        public Text[] TabLabels, PlayerNames, Summaries, XLabels, YLabels, BarValues, CombatNames, CombatTitles, CombatPlayerNames;
        public RawImage[] Heads;
        public ScrollRect TooltipScroll;
        public StatisticsChartGraphic Chart;
        public StatisticsPortraitRenderer Portraits;
        public bool HasRequiredReferences => RootCanvas && RootRaycaster && OpenButton && CloseButton && GoldOption && PopupRoot && TooltipRoot &&
            Title && OpenButtonText && Countdown && Description && GoldOptionText && AxisTitle && TooltipTitle && TooltipText && TooltipScroll && Chart && Portraits && Portraits.HasRequiredReferences &&
            Complete(Tabs, 4) && Complete(TabLabels, 4) && Complete(PlayerNames, 4) && Complete(Summaries, 4) &&
            Complete(XLabels, 21) && Complete(YLabels, 5) && Complete(BarValues, 16) && Complete(CombatNames, 12) && Complete(CombatTitles, 3) && Complete(CombatPlayerNames, 4) && Complete(Heads, 4);
        private static bool Complete<T>(T[] entries, int length) where T : Object => entries != null && entries.Length == length && entries.All(x => x != null);
    }
}
