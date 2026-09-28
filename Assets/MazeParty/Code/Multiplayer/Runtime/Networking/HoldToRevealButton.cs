using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class HoldToRevealButton : MonoBehaviour,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerExitHandler,
        ICancelHandler
    {
        [SerializeField] private Button button;

        public event Action<bool> HoldChanged;

        public bool HasRequiredReferences => button != null;
        public bool IsHeld { get; private set; }

        public void Configure(Button configuredButton)
        {
            button = configuredButton;
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left &&
                button != null && button.IsInteractable())
            {
                SetHeld(true);
            }
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left)
            {
                SetHeld(false);
            }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHeld(false);
        }

        public void OnCancel(BaseEventData eventData)
        {
            SetHeld(false);
        }

        private void Awake()
        {
            if (HasRequiredReferences)
            {
                return;
            }

            Debug.LogError(
                "HoldToRevealButton requires its authored Button binding.",
                this);
            enabled = false;
        }

        private void OnDisable()
        {
            SetHeld(false);
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
            {
                SetHeld(false);
            }
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
            {
                SetHeld(false);
            }
        }

        private void SetHeld(bool held)
        {
            if (IsHeld == held)
            {
                return;
            }

            IsHeld = held;
            HoldChanged?.Invoke(held);
        }
    }
}
