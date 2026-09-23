using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

namespace MazeParty.Multiplayer
{
    public sealed class BoardUtilityItemView : MonoBehaviour
    {
        [SerializeField] private GameObject targetPanel;
        [SerializeField] private Button[] targetButtons;
        [SerializeField] private Text[] targetLabels;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Text statusText;
        [SerializeField] private Text noticeText;
        [SerializeField] private string playerFormat = "P{0}  {1}";
        [SerializeField] private string castingFormat = "POSITION SWAP  {0:0.0}s / ACTION LOCKED";
        [SerializeField] private string cloakText = "CLOAK ACTIVE / ENDS BEFORE COMBAT";
        [SerializeField] private string[] noticeFormats = { "", "Position exchanged with P{0}.", "P{0} exchanged positions with you!", "Hit! Position swap cancelled; item consumed.", "Position swap cancelled; item consumed.", "That player is unavailable." };
        public static BoardUtilityItemView Instance { get; private set; }
        public static bool IsTargetPickerOpen => Instance != null && Instance.targetPanel != null && Instance.targetPanel.activeSelf;
        private NetworkPlayerAvatar _local;
        private bool _awaitOpeningRelease;
        public bool HasRequiredReferences => targetPanel != null && cancelButton != null && statusText != null &&
            noticeText != null && targetButtons != null && targetLabels != null && targetButtons.Length == 4 &&
            targetLabels.Length == 4 && System.Array.TrueForAll(targetButtons, x => x != null) &&
            System.Array.TrueForAll(targetLabels, x => x != null) && noticeFormats != null && noticeFormats.Length == 6;
        private void Awake()
        {
            Instance = this;
            if (!HasRequiredReferences) { Debug.LogError("Board utility item UI bindings missing.", this); enabled = false; return; }
            for (int slot = 0; slot < 4; slot++) { int target = slot; targetButtons[slot].onClick.AddListener(() => Select(target)); }
            cancelButton.onClick.AddListener(Close);
            Close();
        }
        private void OnDestroy() { if (Instance == this) Instance = null; }
        public void Open(NetworkPlayerAvatar local)
        {
            var match = NetworkMatchState.Instance;
            if (!HasRequiredReferences || local == null || local.IsSwapping || match == null || !match.CanAcceptActionInput ||
                local.LocalEquippedItem != PrototypeItemId.PositionSwapper) return;
            _local = local; _awaitOpeningRelease = true; targetPanel.SetActive(true); RefreshTargets(match);
        }
        public void Close() { if (targetPanel != null) targetPanel.SetActive(false); }
        private void Select(int slot) { if (_local != null) _local.ChooseSwapTarget(slot); Close(); }
        private void Update()
        {
            if (!HasRequiredReferences) return;
            var match = NetworkMatchState.Instance;
            if (_local == null || !_local.IsSpawned)
                foreach (var avatar in FindObjectsByType<NetworkPlayerAvatar>()) if (avatar.IsOwner && avatar.IsSpawned) { _local = avatar; break; }
            bool board = match != null && match.IsSpawned && match.GameplayEnabled && match.FlowState <= BoardFlowState.LandingEffectResolve;
            if (!board || _local == null)
            { Close(); statusText.text = ""; noticeText.text = ""; return; }
            if (!match.CanAcceptActionInput || _local.IsSwapping || _local.LocalEquippedItem != PrototypeItemId.PositionSwapper ||
                Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) Close();
            if (Mouse.current == null || !Mouse.current.leftButton.isPressed) _awaitOpeningRelease = false;
            if (targetPanel.activeSelf) RefreshTargets(match);
            statusText.text = _local.IsSwapping ? string.Format(castingFormat, _local.LocalSwapRemaining) : _local.IsCloaked ? cloakText : "";
            int notice = (int)_local.LocalUtilityNotice;
            noticeText.text = Time.unscaledTime < _local.LocalUtilityNoticeUntil && notice > 0 && notice < noticeFormats.Length
                ? string.Format(noticeFormats[notice], _local.LocalUtilityNoticeSlot + 1) : "";
        }
        private void RefreshTargets(NetworkMatchState match)
        {
            for (int slot = 0; slot < 4; slot++)
            {
                var target = match.GetAvatarForSlot(slot);
                targetButtons[slot].gameObject.SetActive(slot != _local.AssignedSlot);
                targetButtons[slot].interactable = !_awaitOpeningRelease && NetworkPlayerAvatar.IsValidSwapTarget(target);
                targetLabels[slot].text = string.Format(playerFormat, slot + 1, target != null ? target.DisplayName : "--");
            }
        }
    }
}
