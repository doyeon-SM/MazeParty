using System;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    public sealed class SessionRecoveryView : MonoBehaviour
    {
        [SerializeField] private Button restartButton;
        [SerializeField] private Button reconnectEntryButton;
        [SerializeField] private GameObject reconnectPopup;
        [SerializeField] private Button reconnectButton;
        [SerializeField] private Button closeReconnectButton;
        [SerializeField] private Text reconnectMessage;
        [SerializeField] private GameObject detailsRoot;
        [SerializeField] private Text detailsText;
        [SerializeField] private GameObject createConfirmationPopup;
        [SerializeField] private Button confirmCreateButton;
        [SerializeField] private Button cancelCreateButton;
        private bool _reconnectOpen;
        private bool _createOpen;
        public event Action ReconnectRequested;
        public event Action RestartRequested;
        public event Action FreshCreateConfirmed;
        public bool HasRequiredReferences => restartButton && reconnectEntryButton && reconnectPopup &&
            reconnectButton && closeReconnectButton && reconnectMessage && detailsRoot && detailsText &&
            createConfirmationPopup && confirmCreateButton && cancelCreateButton;
        public Button RestartButton => restartButton;
        public Button ReconnectButton => reconnectButton;
        public GameObject ReconnectPopup => reconnectPopup;
        public GameObject CreateConfirmationPopup => createConfirmationPopup;
        public Text DetailsText => detailsText;

        private void Awake()
        {
            if (!HasRequiredReferences) { Debug.LogError("SessionRecoveryView requires authored prefab bindings.", this); enabled = false; return; }
            restartButton.onClick.AddListener(() => RestartRequested?.Invoke());
            reconnectEntryButton.onClick.AddListener(OpenReconnect);
            reconnectButton.onClick.AddListener(() => ReconnectRequested?.Invoke());
            closeReconnectButton.onClick.AddListener(CloseReconnect);
            confirmCreateButton.onClick.AddListener(() => { CloseCreateConfirmation(); FreshCreateConfirmed?.Invoke(); });
            cancelCreateButton.onClick.AddListener(CloseCreateConfirmation);
            detailsRoot.SetActive(false);
            reconnectPopup.SetActive(false);
            createConfirmationPopup.SetActive(false);
        }

        public void Initialize(bool reconnectAvailable) { if (reconnectAvailable) OpenReconnect(); }
        private void OpenReconnect()
        {
            _reconnectOpen = true;
            reconnectPopup.SetActive(true);
            UiPopupStack.Push(reconnectPopup, CloseReconnect);
        }
        private void CloseReconnect()
        {
            _reconnectOpen = false;
            reconnectPopup.SetActive(false);
            UiPopupStack.Remove(reconnectPopup);
        }
        public void ShowCreateConfirmation()
        {
            CloseReconnect();
            _createOpen = true;
            createConfirmationPopup.SetActive(true);
            UiPopupStack.Push(createConfirmationPopup, CloseCreateConfirmation);
        }
        private void CloseCreateConfirmation()
        {
            _createOpen = false;
            createConfirmationPopup.SetActive(false);
            UiPopupStack.Remove(createConfirmationPopup);
        }
        public void Render(bool inSession, bool busy, bool hasReconnect, bool canRestart, string details, string status)
        {
            if (!HasRequiredReferences) return;
            restartButton.gameObject.SetActive(!inSession && canRestart);
            reconnectEntryButton.gameObject.SetActive(!inSession && hasReconnect);
            restartButton.interactable = reconnectEntryButton.interactable = reconnectButton.interactable = !busy;
            confirmCreateButton.interactable = !busy;
            detailsText.text = details ?? string.Empty;
            if (inSession) { CloseReconnect(); CloseCreateConfirmation(); }
            if (!canRestart || inSession) detailsRoot.SetActive(false);
            reconnectPopup.SetActive(!inSession && hasReconnect && _reconnectOpen);
            createConfirmationPopup.SetActive(!inSession && _createOpen);
            reconnectMessage.text = busy ? GameText.T("Reconnecting to the interrupted online game...") :
                GameText.T("An interrupted game was found. Reconnect to return to the waiting players.") +
                (string.IsNullOrWhiteSpace(status) ? "" : "\n" + status);
        }
        private void OnDestroy()
        {
            UiPopupStack.Remove(reconnectPopup);
            UiPopupStack.Remove(createConfirmationPopup);
        }
    }
}
