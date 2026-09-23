using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace MazeParty.Multiplayer
{
    [DefaultExecutionOrder(-200)]
    public sealed class HandEmoteWheelView : MonoBehaviour
    {
        [SerializeField] private bool lobbyWheel;
        [SerializeField] private GameObject panel;
        [SerializeField] private RectTransform pointer;
        [SerializeField] private Image[] sectors;
        [SerializeField] private Text[] labels;
        [SerializeField] private Text selectionText;
        [SerializeField] private float deadZone = 32, radius = 130;
        [SerializeField] private Color idleColor = new Color(.07f, .12f, .18f, .96f), selectedColor = new Color(.15f, .6f, .72f, 1f);
        [SerializeField] private string neutralText = "DRAG TO SELECT", releaseText = "RELEASE T: {0}";
        private NetworkPlayerAvatar _local;
        private Vector2 _offset;
        private bool _cursorWasVisible;
        private int _selected = -1;
        private static HandEmoteWheelView _open;
        private static int _closedFrame = -1;
        public static bool BlocksPointerInput => _open != null || _closedFrame == Time.frameCount;
        public bool IsOpen => panel != null && panel.activeSelf;
        public bool HasRequiredReferences => panel != null && pointer != null && selectionText != null && sectors != null && labels != null &&
            sectors.Length == 3 && labels.Length == 3 && System.Array.TrueForAll(sectors, x => x != null) && System.Array.TrueForAll(labels, x => x != null);
        private void Awake() { if (!HasRequiredReferences) { Debug.LogError("Hand emote wheel bindings missing.", this); enabled = false; return; } panel.SetActive(false); }
        private void OnDisable() { Close(false); }
        private void OnApplicationFocus(bool focus) { if (!focus) Close(false); }
        private void Update()
        {
            var keyboard = Keyboard.current; var mouse = Mouse.current;
            if (_local == null || !_local.IsSpawned)
                foreach (var p in FindObjectsByType<NetworkPlayerAvatar>()) if (p.IsOwner && p.IsSpawned) { _local = p; break; }
            if (keyboard == null || mouse == null || _local == null || !_local.CanUseHandGestures ||
                _local.IsInLobbyForExpressions != lobbyWheel || BoardFlowView.IsItemShopOpen || BoardUtilityItemView.IsTargetPickerOpen)
            { Close(false); return; }
            if (keyboard.tKey.wasPressedThisFrame && _open == null && !Typing())
            { _cursorWasVisible = Cursor.visible; Cursor.visible = false; _open = this; _offset = Vector2.zero; _selected = -1; panel.SetActive(true); }
            if (_open != this) return;
            if (keyboard.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) { Close(false); return; }
            var scale = GetComponentInParent<Canvas>().scaleFactor;
            _offset = Vector2.ClampMagnitude(_offset + mouse.delta.ReadValue() / Mathf.Max(.01f, scale), radius);
            _selected = HandEmoteRules.Select(_offset, deadZone, sectors.Length);
            pointer.anchoredPosition = _offset;
            var catalog = PlayerExpressionCatalog.Instance;
            for (int i = 0; i < sectors.Length; i++)
            { sectors[i].color = i == _selected ? selectedColor : idleColor; labels[i].text = catalog.Gestures[i].Name; }
            selectionText.text = _selected < 0 ? neutralText : string.Format(releaseText, catalog.Gestures[_selected].Name);
            if (!keyboard.tKey.isPressed) Close(true);
        }
        private static bool Typing()
        {
            var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            var input = selected != null ? selected.GetComponent<InputField>() : null;
            return input != null && input.isFocused;
        }
        private void Close(bool commit)
        {
            if (_open == this)
            {
                if (commit && _selected >= 0 && _local != null) _local.RequestHandGesture((byte)(_selected + 1));
                Cursor.visible = _cursorWasVisible; _open = null; _closedFrame = Time.frameCount;
            }
            if (panel != null) panel.SetActive(false);
        }
    }
}
