using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MazeParty.Gameplay.Testbed
{
    [RequireComponent(typeof(Button))]
    public sealed class TestbedItemChoiceButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [SerializeField] private int slotIndex;

        private GameplayTestbedController _controller;
        private Button _button;

        public int SlotIndex => slotIndex;

        public void Configure(int index)
        {
            slotIndex = index;
        }

        private void Awake()
        {
            _controller = FindAnyObjectByType<GameplayTestbedController>();
            _button = GetComponent<Button>();
            _button.onClick.AddListener(ChooseItem);
        }

        private void OnDestroy()
        {
            if (_button != null)
                _button.onClick.RemoveListener(ChooseItem);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _controller?.ShowItemTooltip(slotIndex);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _controller?.HideItemTooltip();
        }

        private void ChooseItem()
        {
            _controller?.SelectItem(slotIndex);
        }
    }
}
