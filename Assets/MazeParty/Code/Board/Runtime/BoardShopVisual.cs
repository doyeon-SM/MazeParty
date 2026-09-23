using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Keep the interaction targets when replacing the Visuals children.</summary>
    public sealed class BoardShopVisual : MonoBehaviour
    {
        [SerializeField] private TextMesh label;
        [SerializeField] private GameObject topViewHighlight;
        [SerializeField] private Collider[] interactionColliders;
        [SerializeField, TextArea] private string availableText = "ITEM SHOP {0}\nRMB OPEN";
        [SerializeField, TextArea] private string soldOutText = "ITEM SHOP {0}\nSOLD OUT";
        public TextMesh Label => label;
        public GameObject TopViewHighlight => topViewHighlight;
        private void Awake()
        {
            if (Application.isPlaying && label != null) WorldTextOcclusion.Apply(label);
        }
        public bool HasRequiredReferences
        {
            get
            {
                if (label == null || topViewHighlight == null || interactionColliders == null || interactionColliders.Length == 0) return false;
                foreach (var target in interactionColliders)
                    if (target == null || (target.GetComponent<KeyShopWorldTarget>() == null && target.GetComponent<ItemShopWorldTarget>() == null)) return false;
                return true;
            }
        }
        public void SetItemState(int index, bool soldOut)
        {
            label.text = (soldOut ? soldOutText : availableText).Replace("{0}", (index + 1).ToString());
            foreach (var target in interactionColliders)
            {
                var item = target.GetComponent<ItemShopWorldTarget>();
                if (item != null) item.Configure(index);
            }
        }
    }
}
