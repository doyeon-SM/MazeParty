using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DefaultExecutionOrder(1100)]
    public sealed class ReconnectWaitView : MonoBehaviour
    {
        [SerializeField] private GameObject panel;
        [SerializeField] private Text message;
        [SerializeField] private Button returnToLobbyButton;
        public bool HasRequiredReferences => panel && message && returnToLobbyButton;
        public Button ReturnToLobbyButton => returnToLobbyButton;
        private void Awake()
        {
            if (!HasRequiredReferences) { Debug.LogError("ReconnectWaitView requires authored prefab bindings.", this); enabled = false; return; }
            returnToLobbyButton.onClick.AddListener(() => OnlineSessionController.Instance?.ReturnToLobbyDuringReconnect());
            panel.SetActive(false);
        }
        private void Update()
        {
            var controller = OnlineSessionController.Instance;
            var waiting = controller != null && controller.IsWaitingForReconnect;
            panel.SetActive(waiting);
            if (!waiting) return;
            message.text = GameText.F("Waiting for reconnection: {0}", GameMenuRules.FormatPauseClock(controller.ReconnectWaitRemaining));
            var host = controller.CurrentSession.IsHost;
            returnToLobbyButton.gameObject.SetActive(host);
            returnToLobbyButton.interactable = controller.CanReturnToLobbyDuringReconnect;
            if (host) LocalInputGate.SetPausePointerRequested(true);
        }
    }
}
