using UnityEngine;
using UnityEngine.EventSystems;

namespace MazeParty.Gameplay.BoardFlowTestbed
{
    public sealed class BoardFlowLocalItemChoiceButton : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        [SerializeField] private BoardFlowLocalSimulator simulator;
        [SerializeField] private int slotIndex;
        [SerializeField] private bool shopOffer;

        public void Configure(BoardFlowLocalSimulator owner, int slot)
        {
            simulator = owner;
            slotIndex = slot;
            shopOffer = false;
        }

        public void ConfigureShop(BoardFlowLocalSimulator owner, int offerIndex)
        {
            simulator = owner;
            slotIndex = offerIndex;
            shopOffer = true;
        }


        public void OnPointerEnter(PointerEventData eventData)
        {
            if (shopOffer)
            {
                simulator?.ShowLocalShopTooltip(slotIndex);
            }
            else
            {
                simulator?.ShowItemTooltip(slotIndex);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (shopOffer)
            {
                simulator?.HideLocalShopTooltip();
            }
            else
            {
                simulator?.HideItemTooltip();
            }
        }
    }
}
