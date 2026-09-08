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

        public void Configure(BoardFlowLocalSimulator owner, int slot)
        {
            simulator = owner;
            slotIndex = slot;
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            simulator?.ShowItemTooltip(slotIndex);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            simulator?.HideItemTooltip();
        }
    }
}
