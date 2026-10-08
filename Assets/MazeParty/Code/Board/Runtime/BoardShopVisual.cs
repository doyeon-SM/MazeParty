using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>Keep the interaction targets when replacing the Visuals children.</summary>
    public sealed class BoardShopVisual : MonoBehaviour
    {
        public const float LocationHighlightSeconds = 3f;

        [SerializeField] private TextMesh label;
        [SerializeField] private GameObject topViewHighlight;
        [SerializeField] private GameObject locationHighlightVfx;
        [SerializeField] private Collider[] interactionColliders;
        [SerializeField, TextArea] private string availableText = GameText.N("ITEM SHOP {0}\nRMB OPEN");
        [SerializeField, TextArea] private string soldOutText = GameText.N("ITEM SHOP {0}\nSOLD OUT");
        private float _locationHighlightEndsAt = float.NegativeInfinity;

        public TextMesh Label => label;
        public GameObject TopViewHighlight => topViewHighlight;
        public GameObject LocationHighlightVfx => locationHighlightVfx;

        private void Awake()
        {
            if (Application.isPlaying && label != null) WorldTextOcclusion.Apply(label);
            SetLocationHighlightVisible(false);
        }

        private void Update()
        {
            if (locationHighlightVfx != null &&
                locationHighlightVfx.activeSelf &&
                Time.unscaledTime >= _locationHighlightEndsAt)
            {
                SetLocationHighlightVisible(false);
            }
        }

        private void OnDisable()
        {
            SetLocationHighlightVisible(false);
        }

        public bool HasRequiredReferences
        {
            get
            {
                if (label == null || topViewHighlight == null ||
                    locationHighlightVfx == null || interactionColliders == null ||
                    interactionColliders.Length == 0)
                {
                    return false;
                }
                foreach (var target in interactionColliders)
                    if (target == null || (target.GetComponent<KeyShopWorldTarget>() == null && target.GetComponent<ItemShopWorldTarget>() == null)) return false;
                return true;
            }
        }

        public void PlayLocationHighlight()
        {
            if (locationHighlightVfx == null)
            {
                return;
            }

            _locationHighlightEndsAt = Time.unscaledTime +
                                       LocationHighlightSeconds;
            SetLocationHighlightVisible(true);
        }

        public void StopLocationHighlight()
        {
            SetLocationHighlightVisible(false);
        }

        public void SetItemState(int index, bool soldOut)
        {
            label.text = GameText.T(soldOut ? soldOutText : availableText).Replace("{0}", (index + 1).ToString());
            foreach (var target in interactionColliders)
            {
                var item = target.GetComponent<ItemShopWorldTarget>();
                if (item != null) item.Configure(index);
            }
        }

        private void SetLocationHighlightVisible(bool visible)
        {
            if (locationHighlightVfx != null &&
                locationHighlightVfx.activeSelf != visible)
            {
                locationHighlightVfx.SetActive(visible);
            }

            if (!visible)
            {
                _locationHighlightEndsAt = float.NegativeInfinity;
            }
        }
    }
}
