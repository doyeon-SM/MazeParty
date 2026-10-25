using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    [DisallowMultipleComponent]
    public sealed class PrivacyMenuBindings : MonoBehaviour
    {
        [SerializeField] private Button openButton;
        [SerializeField] private GameObject panelRoot;
        [SerializeField] private Text accountText, statusText;
        [SerializeField] private Button copyButton, emailButton, deleteButton, resumeButton, closeButton;
        [SerializeField] private GameObject confirmRoot;
        [SerializeField] private Text confirmText, confirmButtonText;
        [SerializeField] private Button confirmButton, cancelButton;
        public Button OpenButton => openButton;
        public GameObject PanelRoot => panelRoot;
        public Text AccountText => accountText;
        public Text StatusText => statusText;
        public Button CopyButton => copyButton;
        public Button EmailButton => emailButton;
        public Button DeleteButton => deleteButton;
        public Button ResumeButton => resumeButton;
        public Button CloseButton => closeButton;
        public GameObject ConfirmRoot => confirmRoot;
        public Text ConfirmText => confirmText;
        public Text ConfirmButtonText => confirmButtonText;
        public Button ConfirmButton => confirmButton;
        public Button CancelButton => cancelButton;
        public bool HasRequiredReferences => openButton && panelRoot && accountText && statusText && copyButton &&
            emailButton && deleteButton && resumeButton && closeButton && confirmRoot && confirmText &&
            confirmButtonText && confirmButton && cancelButton && panelRoot.transform.IsChildOf(transform) &&
            confirmRoot.transform.IsChildOf(transform) && openButton.transform.IsChildOf(transform);
    }
}
