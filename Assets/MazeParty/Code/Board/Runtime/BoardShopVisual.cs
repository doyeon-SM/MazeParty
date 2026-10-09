using System;
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
        [SerializeField] private Collider[] interactionColliders =
            Array.Empty<Collider>();
        [SerializeField] private Collider[] physicalColliders =
            Array.Empty<Collider>();
        [SerializeField, TextArea] private string availableText = GameText.N("ITEM SHOP {0}\nRMB OPEN");
        [SerializeField, TextArea] private string soldOutText = GameText.N("ITEM SHOP {0}\nSOLD OUT");
        private float _locationHighlightEndsAt = float.NegativeInfinity;
        private Camera _facingCamera;
        private bool _topViewHighlightVisible;
        private bool _locationHighlightVisible;

        public TextMesh Label => label;
        public GameObject TopViewHighlight => topViewHighlight;
        public GameObject LocationHighlightVfx => locationHighlightVfx;
        public Collider[] InteractionColliders => interactionColliders;
        public Collider[] PhysicalColliders => physicalColliders;

        private void Awake()
        {
            if (Application.isPlaying && label != null) WorldTextOcclusion.Apply(label);
            RefreshHighlightVisibility();
        }

        private void OnEnable()
        {
            RefreshHighlightVisibility();
        }

        private void Update()
        {
            if (_locationHighlightVisible &&
                Time.unscaledTime >= _locationHighlightEndsAt)
            {
                SetLocationHighlightVisible(false);
            }
        }

        private void LateUpdate()
        {
            if (label == null)
            {
                return;
            }

            if (_facingCamera == null || !_facingCamera.isActiveAndEnabled)
            {
                _facingCamera = Camera.main;
            }

            if (_facingCamera == null)
            {
                return;
            }

            var direction =
                label.transform.position - _facingCamera.transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= Mathf.Epsilon)
            {
                return;
            }

            label.transform.rotation = Quaternion.LookRotation(
                direction.normalized,
                Vector3.up);
        }

        private void OnDisable()
        {
            _facingCamera = null;
            _locationHighlightVisible = false;
            _locationHighlightEndsAt = float.NegativeInfinity;
            SetHighlightObjectVisible(topViewHighlight, false);
            if (locationHighlightVfx != topViewHighlight)
            {
                SetHighlightObjectVisible(locationHighlightVfx, false);
            }
        }

        public bool HasRequiredReferences
        {
            get
            {
                if (label == null || topViewHighlight == null ||
                    locationHighlightVfx == null || interactionColliders == null ||
                    interactionColliders.Length == 0 || physicalColliders == null ||
                    physicalColliders.Length == 0)
                {
                    return false;
                }
                foreach (var target in interactionColliders)
                {
                    if (target == null || !target.isTrigger ||
                        (target.GetComponent<KeyShopWorldTarget>() == null &&
                         target.GetComponent<ItemShopWorldTarget>() == null))
                    {
                        return false;
                    }
                }
                foreach (var physical in physicalColliders)
                {
                    if (physical == null || physical.isTrigger ||
                        physical.GetComponent<KeyShopWorldTarget>() != null ||
                        physical.GetComponent<ItemShopWorldTarget>() != null)
                    {
                        return false;
                    }
                }
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

        public void SetTopViewHighlightVisible(bool visible)
        {
            _topViewHighlightVisible = visible;
            RefreshHighlightVisibility();
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
            _locationHighlightVisible = visible;
            if (!visible)
            {
                _locationHighlightEndsAt = float.NegativeInfinity;
            }

            RefreshHighlightVisibility();
        }

        private void RefreshHighlightVisibility()
        {
            var canShow = isActiveAndEnabled;
            if (topViewHighlight == locationHighlightVfx)
            {
                SetHighlightObjectVisible(
                    topViewHighlight,
                    canShow && (_topViewHighlightVisible ||
                                _locationHighlightVisible));
                return;
            }

            SetHighlightObjectVisible(
                topViewHighlight,
                canShow && _topViewHighlightVisible);
            SetHighlightObjectVisible(
                locationHighlightVfx,
                canShow && _locationHighlightVisible);
        }

        private static void SetHighlightObjectVisible(
            GameObject highlight,
            bool visible)
        {
            if (highlight != null && highlight.activeSelf != visible)
            {
                highlight.SetActive(visible);
            }
        }
    }
}
