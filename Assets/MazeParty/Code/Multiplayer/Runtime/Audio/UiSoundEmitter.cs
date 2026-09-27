using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Hover and click sounds of one uGUI control (button, toggle, slider,
    /// dropdown, input field). Added to every control in the UI prefabs by
    /// MazeParty/Audio/Add UI Sounds To Prefabs; change a key per control, or
    /// clear it for silence. Plays only while the control is interactable.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiSoundEmitter : MonoBehaviour,
        IPointerEnterHandler,
        IPointerClickHandler,
        ISubmitHandler
    {
        [SerializeField] private string hoverKey = SoundKeys.UiHover;
        [SerializeField] private string clickKey = SoundKeys.UiClick;

        private Selectable _selectable;

        public string HoverKey => hoverKey ?? string.Empty;
        public string ClickKey => clickKey ?? string.Empty;

        /// <summary>Editor setup: empty strings mean silence.</summary>
        public void Configure(string hover, string click)
        {
            hoverKey = hover ?? string.Empty;
            clickKey = click ?? string.Empty;
        }

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!eventData.dragging && CanPlay(hoverKey))
            {
                GameSound.Play(hoverKey);
            }
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left &&
                CanPlay(clickKey))
            {
                GameSound.Play(clickKey);
            }
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (CanPlay(clickKey))
            {
                GameSound.Play(clickKey);
            }
        }

        private bool CanPlay(string key)
        {
            return !string.IsNullOrEmpty(key) &&
                   isActiveAndEnabled &&
                   (_selectable == null || _selectable.IsInteractable());
        }
    }
}
