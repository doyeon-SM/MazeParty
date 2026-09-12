using UnityEngine;
using UnityEngine.EventSystems;

namespace MazeParty.Multiplayer
{
    public sealed class BoardItemChoiceButton : MonoBehaviour,
        IPointerEnterHandler,
        IPointerExitHandler
    {
        [SerializeField] private BoardFlowView view;
        [SerializeField] private int slotIndex;
        [SerializeField] private bool shopOffer;

        public bool IsConfiguredFor(
            BoardFlowView owner,
            int slot,
            bool isShopOffer)
        {
            return view == owner &&
                   slotIndex == slot &&
                   shopOffer == isShopOffer;
        }

        public void Configure(BoardFlowView owner, int slot)
        {
            view = owner;
            slotIndex = slot;
            shopOffer = false;
        }

        public void ConfigureShop(BoardFlowView owner, int offerIndex)
        {
            view = owner;
            slotIndex = offerIndex;
            shopOffer = true;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (view == null || !view.isActiveAndEnabled)
            {
                return;
            }

            if (shopOffer)
            {
                view.ShowShopItemTooltip(slotIndex);
            }
            else
            {
                view.ShowItemTooltip(slotIndex);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (view == null || !view.isActiveAndEnabled)
            {
                return;
            }

            if (shopOffer)
            {
                view.HideShopItemTooltip();
            }
            else
            {
                view.HideItemTooltip();
            }
        }
    }
}
