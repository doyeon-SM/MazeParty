using System;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class PrivacyMenuView : MonoBehaviour
    {
        public const string DeleteWarning = "Delete this Unity sign-in account permanently?\nPlayer ID: {0}\n\nOnline save recovery may be lost. Steam/STOVE accounts, other service records and local saves are handled separately. Online access stays paused until you resume it.";
        public const string ResumeWarning = "Resume online play? A deleted account will be replaced with a new Unity sign-in account. Its old identity cannot be restored. Any pending deletion request remains in effect.";
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
            bindings.PolicyButton.onClick.AddListener(OpenPolicy);
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
            bindings.PolicyButton.onClick.RemoveListener(OpenPolicy);
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
            bindings.PolicyButton.interactable = TryGetPolicyUrl(out _);
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
            if (string.IsNullOrEmpty(status) && (c == null || string.IsNullOrEmpty(c.PrivacyPlayerId)))
                status = "No authenticated account is available. Contact support if needed.";
            bindings.StatusText.text = GameText.T(status);
            bindings.StatusText.gameObject.SetActive(!string.IsNullOrEmpty(status));
            if (bindings.ConfirmRoot.activeSelf)
            {
                bindings.ConfirmButton.interactable = c != null && c.PrivacyProfile == _expectedProfile &&
                    (_resumeConfirmation ? c.CanResumePrivacyOnline : c.CanDeletePrivacyAccount && c.PrivacyPlayerId == _expectedId);
                bindings.ConfirmText.text = _resumeConfirmation ? GameText.T(ResumeWarning) : GameText.F(DeleteWarning, _expectedId);
                bindings.ConfirmButtonText.text = GameText.T(_resumeConfirmation ? "Use Online Again" : "Delete Permanently");
            }
        }
        private bool TryGetPolicyUrl(out Uri url)
        {
            // Do not open workspace/editor URLs as though they were a published policy.
            return Uri.TryCreate(bindings.PrivacyPolicyUrl, UriKind.Absolute, out url) &&
                url.Scheme == Uri.UriSchemeHttps && !string.IsNullOrEmpty(url.Host) &&
                !url.Host.Equals("app.notion.com", StringComparison.OrdinalIgnoreCase);
        }
        private void OpenPolicy()
        {
            if (TryGetPolicyUrl(out var url)) Application.OpenURL(url.AbsoluteUri);
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
                if (this != null) _statusSource = "Deletion could not be confirmed. Retry if the account is available, or contact us through Privacy Policy & Support.";
            }
            if (this != null && isActiveAndEnabled) Refresh();
        }
    }
}
