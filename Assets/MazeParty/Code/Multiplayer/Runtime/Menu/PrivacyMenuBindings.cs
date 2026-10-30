using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class PrivacyMenuBindings : MonoBehaviour
    {
        [SerializeField] private Button openButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text statusText;
        [SerializeField] private Button policyButton, deleteButton, resumeButton, closeButton;
        [Tooltip("Published HTTPS privacy policy URL. Leave empty until the final policy is public.")]
        [SerializeField] private string privacyPolicyUrl = "";
        [SerializeField] private GameObject confirmRoot;
        [SerializeField] private Text confirmText, confirmButtonText;
        [SerializeField] private Button confirmButton, cancelButton;
        public Button OpenButton => openButton;
        public GameObject PanelRoot => panelRoot;
        public Text StatusText => statusText;
        public Button PolicyButton => policyButton;
        public string PrivacyPolicyUrl => privacyPolicyUrl;
        public Button DeleteButton => deleteButton;
        public Button ResumeButton => resumeButton;
        public Button CloseButton => closeButton;
        public GameObject ConfirmRoot => confirmRoot;
        public Text ConfirmText => confirmText;
        public Text ConfirmButtonText => confirmButtonText;
        public Button ConfirmButton => confirmButton;
        public Button CancelButton => cancelButton;
        public bool HasRequiredReferences => openButton && panelRoot && statusText && policyButton && deleteButton && resumeButton && closeButton && confirmRoot && confirmText &&
            confirmButtonText && confirmButton && cancelButton && panelRoot.transform.IsChildOf(transform) &&
            confirmRoot.transform.IsChildOf(transform) && openButton.transform.IsChildOf(transform) &&
            policyButton.transform.IsChildOf(panelRoot.transform) &&
            deleteButton.transform.IsChildOf(panelRoot.transform) &&
            resumeButton.transform.IsChildOf(panelRoot.transform);
    }
}
