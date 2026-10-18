using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed class BoardChestNoticeView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Image itemIcon;
        [SerializeField] private Text message;
        [SerializeField, Min(0.1f)] private float displaySeconds = 3f;
        private readonly Queue<PrototypeItemId> _pending = new();
        private PrototypeItemId _showing;
        private float _remaining;
        public static BoardChestNoticeView Instance { get; private set; }
        public bool HasRequiredReferences => panel != null && itemIcon != null && message != null;
        private void OnEnable() { Instance = this; }
        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            _pending.Clear();
            _showing = PrototypeItemId.None;
            if (panel != null) panel.SetActive(false);
        }
        public void Present(PrototypeItemId id)
        {
            if (PrototypeItemCatalog.IsValid(id)) _pending.Enqueue(id);
        }
        private void Update()
        {
            if (!HasRequiredReferences) return;
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsActionPhase)
            {
                _pending.Clear(); _showing = PrototypeItemId.None;
                panel.SetActive(false);
                return;
            }
            if (!match.IsGlobalSimulationPaused) _remaining -= Time.unscaledDeltaTime;
            if (_remaining <= 0f || _showing == PrototypeItemId.None)
            {
                _showing = _pending.Count > 0 ? _pending.Dequeue() : PrototypeItemId.None;
                _remaining = displaySeconds;
            }
            panel.SetActive(_showing != PrototypeItemId.None);
            if (_showing == PrototypeItemId.None) return;
            var item = PrototypeItemCatalog.Get(_showing);
            itemIcon.sprite = item.Icon;
            message.text = GameText.F("Acquired \"{0}\".", GameText.T(item.DisplayName));
        }
    }
}
