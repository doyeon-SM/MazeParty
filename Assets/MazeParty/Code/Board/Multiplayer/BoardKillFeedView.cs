using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized presentation owned by BoardKillFeedPanel.prefab.
    /// The layout stays authored; runtime only supplies content and visibility.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BoardKillFeedView : MonoBehaviour
    {
        [SerializeField] private Text beforeItem;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Text itemName;
        [SerializeField] private Text afterItem;

        public Text BeforeItem => beforeItem;
        public Image ItemIcon => itemIcon;
        public Text ItemName => itemName;
        public Text AfterItem => afterItem;
        public bool HasRequiredReferences =>
            beforeItem != null && itemIcon != null &&
            itemName != null && afterItem != null;

        public void Present(
            string before,
            Sprite icon,
            string name,
            string after)
        {
            if (!HasRequiredReferences)
                return;

            beforeItem.text = before ?? string.Empty;
            itemIcon.sprite = icon;
            itemIcon.gameObject.SetActive(icon != null);
            itemName.text = name ?? string.Empty;
            afterItem.text = after ?? string.Empty;
        }

        public void Clear()
        {
            if (!HasRequiredReferences)
                return;

            beforeItem.text = string.Empty;
            itemIcon.sprite = null;
            itemIcon.gameObject.SetActive(false);
            itemName.text = string.Empty;
            afterItem.text = string.Empty;
        }
    }
}
