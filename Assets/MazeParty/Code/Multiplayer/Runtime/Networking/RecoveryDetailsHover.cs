using UnityEngine;
using UnityEngine.EventSystems;

namespace MazeParty.Multiplayer
{
    public sealed class RecoveryDetailsHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        ISelectHandler, IDeselectHandler
    {
        [SerializeField] private GameObject detailsRoot;
        public void OnPointerEnter(PointerEventData data) => detailsRoot.SetActive(true);
        public void OnPointerExit(PointerEventData data) => detailsRoot.SetActive(false);
        public void OnSelect(BaseEventData data) => detailsRoot.SetActive(true);
        public void OnDeselect(BaseEventData data) => detailsRoot.SetActive(false);
        private void OnDisable() { if (detailsRoot != null) detailsRoot.SetActive(false); }
    }
}
