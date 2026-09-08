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

        public void Configure(BoardFlowView owner, int slot)
        {
            view = owner;
            slotIndex = slot;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            view?.ShowItemTooltip(slotIndex);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            view?.HideItemTooltip();
        }
    }
}
