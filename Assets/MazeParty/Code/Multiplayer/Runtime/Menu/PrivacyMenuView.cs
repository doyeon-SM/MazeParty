using System;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class PrivacyMenuView : MonoBehaviour
    {
        public const string SupportEmail = "hwangdoyeon423@gmail.com";
        public const string ScopeText = "Delete the Unity sign-in account used on this PC. This cannot be undone and may prevent recovery of saved online games. Steam/STOVE accounts, purchases, achievements, other service records and local save files are handled separately. For Lobby/Relay records or other privacy requests, copy your support details and email us. A draft opens; nothing is sent automatically.";
        public const string DeleteWarning = "Permanently delete this Unity sign-in account?\nPlayer ID: {0}\n\nRecovery may be lost. Copy the support details before leaving this screen if you need help with other records. Other service records and local saves are not deleted by this button. Online access stays paused until you choose to use it again.";
        public const string ResumeWarning = "Use online features again? The game will reconnect to a remaining account or create a new Unity sign-in account if the previous one was deleted. A new account cannot restore the deleted identity. This does not cancel a previous deletion request.";
        [SerializeField] private PrivacyMenuBindings bindings;
        private bool _wired, _resumeConfirmation;
        private string _expectedId, _expectedProfile, _statusSource = "";
        public PrivacyMenuBindings Bindings => bindings;

        private void Awake()
        {
            if (bindings == null || !bindings.HasRequiredReferences)
            { Debug.LogError("Privacy menu bindings are missing.", this); enabled = false; return; }
            bindings.PanelRoot.SetActive(false);
            bindings.ConfirmRoot.SetActive(false);
        }
        private void OnEnable()
        {
            if (bindings == null || !bindings.HasRequiredReferences) return;
            bindings.OpenButton.onClick.AddListener(Open);
            bindings.CloseButton.onClick.AddListener(Close);
            bindings.CopyButton.onClick.AddListener(CopySupport);
            bindings.EmailButton.onClick.AddListener(OpenEmail);
            bindings.DeleteButton.onClick.AddListener(AskDelete);
            bindings.ResumeButton.onClick.AddListener(AskResume);
            bindings.ConfirmButton.onClick.AddListener(Confirm);
            bindings.CancelButton.onClick.AddListener(Cancel);
            _wired = true;
        }
        private void OnDisable()
        {
            if (!_wired) return;
            bindings.OpenButton.onClick.RemoveListener(Open);
            bindings.CloseButton.onClick.RemoveListener(Close);
            bindings.CopyButton.onClick.RemoveListener(CopySupport);
            bindings.EmailButton.onClick.RemoveListener(OpenEmail);
            bindings.DeleteButton.onClick.RemoveListener(AskDelete);
            bindings.ResumeButton.onClick.RemoveListener(AskResume);
            bindings.ConfirmButton.onClick.RemoveListener(Confirm);
            bindings.CancelButton.onClick.RemoveListener(Cancel);
            bindings.PanelRoot.SetActive(false);
            bindings.ConfirmRoot.SetActive(false);
            UiPopupStack.Remove(bindings.PanelRoot);
            UiPopupStack.Remove(bindings.ConfirmRoot);
            _wired = false;
        }
        public void Open()
        {
            _statusSource = "";
            bindings.PanelRoot.SetActive(true);
            UiPopupStack.Push(bindings.PanelRoot, Close);
            Refresh();
        }
        public void Close()
        {
            Cancel();
            bindings.PanelRoot.SetActive(false);
            UiPopupStack.Remove(bindings.PanelRoot);
        }
        private void Update() { if (_wired && bindings.PanelRoot.activeSelf) Refresh(); }
        private void Refresh()
        {
            var c = OnlineSessionController.Instance;
            bindings.AccountText.text = c != null && !string.IsNullOrEmpty(c.PrivacyPlayerId)
                ? GameText.F("Player ID: {0}", c.PrivacyPlayerId) : GameText.T("No authenticated account is available. Contact support if needed.");
            bindings.CopyButton.interactable = c != null && !string.IsNullOrEmpty(c.PrivacyPlayerId);
            bindings.DeleteButton.interactable = c != null && c.CanDeletePrivacyAccount;
            bindings.ResumeButton.gameObject.SetActive(c != null && c.PrivacyOnlinePaused);
            bindings.ResumeButton.interactable = c != null && c.CanResumePrivacyOnline;
            var status = _statusSource;
            if (c != null && c.PrivacyAccountDeleted && string.IsNullOrEmpty(status))
                status = "Unity account deleted. Other records require separate handling. Online access is paused.";
            else if (c != null && c.IsBusy) status = "Please wait for the current operation to finish.";
            else if (c != null && c.IsInSession) status = "Leave the room before deleting your account.";
            else if (string.IsNullOrEmpty(status) && c != null && c.PrivacyOnlinePaused)
                status = "Online access is paused after a deletion request. Open Settings > Privacy & Support to continue.";
            bindings.StatusText.text = GameText.T(status);
            if (bindings.ConfirmRoot.activeSelf)
            {
                bindings.ConfirmButton.interactable = c != null && c.PrivacyProfile == _expectedProfile &&
                    (_resumeConfirmation ? c.CanResumePrivacyOnline : c.CanDeletePrivacyAccount && c.PrivacyPlayerId == _expectedId);
                bindings.ConfirmText.text = _resumeConfirmation ? GameText.T(ResumeWarning) : GameText.F(DeleteWarning, _expectedId);
                bindings.ConfirmButtonText.text = GameText.T(_resumeConfirmation ? "Use Online Again" : "Delete Permanently");
            }
        }
        private void CopySupport()
        {
            var c = OnlineSessionController.Instance;
            if (c == null || string.IsNullOrEmpty(c.PrivacyPlayerId)) return;
            GUIUtility.systemCopyBuffer = c.PrivacySupportDetails;
            _statusSource = "Support details copied. Include them in your email; never send passwords or sign-in tokens.";
        }
        private void OpenEmail()
        {
            var c = OnlineSessionController.Instance;
            var body = GameText.T("Describe your request and which records you want deleted:") + "\n\n" + (c?.PrivacySupportDetails ?? "MazeParty");
            Application.OpenURL("mailto:" + SupportEmail + "?subject=" + Uri.EscapeDataString("MazeParty - Privacy / Support") + "&body=" + Uri.EscapeDataString(body));
            _statusSource = "If no email app opens, copy the support details and email the address shown above.";
        }
        private void AskDelete() { Ask(false); }
        private void AskResume() { Ask(true); }
        private void Ask(bool resume)
        {
            var c = OnlineSessionController.Instance;
            if (c == null || (resume ? !c.CanResumePrivacyOnline : !c.CanDeletePrivacyAccount)) return;
            _resumeConfirmation = resume;
            _expectedId = c.PrivacyPlayerId;
            _expectedProfile = c.PrivacyProfile;
            bindings.ConfirmRoot.SetActive(true);
            UiPopupStack.Push(bindings.ConfirmRoot, Cancel);
            Refresh();
        }
        private void Cancel()
        {
            bindings.ConfirmRoot.SetActive(false);
            UiPopupStack.Remove(bindings.ConfirmRoot);
            _expectedId = _expectedProfile = null;
        }
        private async void Confirm()
        {
            var c = OnlineSessionController.Instance;
            if (!bindings.ConfirmRoot.activeSelf || c == null) return;
            var id = _expectedId; var profile = _expectedProfile; var resume = _resumeConfirmation;
            Cancel();
            if (resume) { _statusSource = ""; c.ResumePrivacyOnline(profile); return; }
            _statusSource = "Please wait for the current operation to finish.";
            try
            {
                await c.DeletePrivacyAccountAsync(id, profile);
                if (this != null) _statusSource = "Unity account deleted. Other records require separate handling. Online access is paused.";
            }
            catch (Exception)
            {
                // Do not expose service exceptions/tokens or erase credentials after an unknown outcome.
                if (this != null) _statusSource = "Deletion could not be confirmed. Keep your support details and contact us, or retry if the account is still available.";
            }
            if (this != null && isActiveAndEnabled) Refresh();
        }
    }
}
