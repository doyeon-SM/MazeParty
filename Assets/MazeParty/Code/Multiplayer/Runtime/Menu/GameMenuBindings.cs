using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Serialized presentation contract owned by GameMenuCanvas.prefab: the
    /// common settings menu, the lobby/waiting-room gear button, the in-game
    /// leave confirmation, the notice popup and the player pause banner.
    /// Runtime code only changes copy, values, visibility and interaction state.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameMenuBindings : MonoBehaviour
    {
        [SerializeField] private Canvas rootCanvas;
        [SerializeField] private GraphicRaycaster rootRaycaster;
        [SerializeField] private Button gearButton;

        [Header("Menu")]
        [SerializeField] private GameObject menuRoot;
        [SerializeField] private Button closeButton;
        [SerializeField] private Slider masterSlider;
        [SerializeField] private Text masterValueText;
        [SerializeField] private Slider sfxSlider;
        [SerializeField] private Text sfxValueText;
        [SerializeField] private Slider bgmSlider;
        [SerializeField] private Text bgmValueText;
        [SerializeField] private Dropdown languageDropdown;
        [SerializeField] private Button displayPreviousButton;
        [SerializeField] private Button displayNextButton;
        [SerializeField] private Text displayValueText;
        [SerializeField] private Button pauseButton;
        [SerializeField] private Text pauseButtonText;
        [SerializeField] private Button applyButton;
        [SerializeField] private Button exitButton;
        [SerializeField] private Text exitButtonText;

        [Header("Leave confirmation")]
        [SerializeField] private GameObject confirmRoot;
        [SerializeField] private Button confirmLeaveButton;
        [SerializeField] private Button confirmCancelButton;

        [Header("Notice popup")]
        [SerializeField] private GameObject noticeRoot;
        [SerializeField] private Text noticeMessageText;
        [SerializeField] private Button noticeOkButton;

        [Header("Player pause banner")]
        [SerializeField] private GameObject pauseBannerRoot;
        [SerializeField] private Text pauseBannerText;
        [SerializeField] private Text pauseTimerText;
        [SerializeField] private Button pauseReleaseButton;

        public Canvas RootCanvas => rootCanvas;
        public GraphicRaycaster RootRaycaster => rootRaycaster;
        public Button GearButton => gearButton;
        public GameObject MenuRoot => menuRoot;
        public Button CloseButton => closeButton;
        public Slider MasterSlider => masterSlider;
        public Text MasterValueText => masterValueText;
        public Slider SfxSlider => sfxSlider;
        public Text SfxValueText => sfxValueText;
        public Slider BgmSlider => bgmSlider;
        public Text BgmValueText => bgmValueText;
        public Dropdown LanguageDropdown => languageDropdown;
        public Button DisplayPreviousButton => displayPreviousButton;
        public Button DisplayNextButton => displayNextButton;
        public Text DisplayValueText => displayValueText;
        public Button PauseButton => pauseButton;
        public Text PauseButtonText => pauseButtonText;
        public Button ApplyButton => applyButton;
        public Button ExitButton => exitButton;
        public Text ExitButtonText => exitButtonText;
        public GameObject ConfirmRoot => confirmRoot;
        public Button ConfirmLeaveButton => confirmLeaveButton;
        public Button ConfirmCancelButton => confirmCancelButton;
        public GameObject NoticeRoot => noticeRoot;
        public Text NoticeMessageText => noticeMessageText;
        public Button NoticeOkButton => noticeOkButton;
        public GameObject PauseBannerRoot => pauseBannerRoot;
        public Text PauseBannerText => pauseBannerText;
        public Text PauseTimerText => pauseTimerText;
        public Button PauseReleaseButton => pauseReleaseButton;

        public bool HasRequiredReferences =>
            rootCanvas != null &&
            rootRaycaster != null &&
            gearButton != null &&
            menuRoot != null &&
            closeButton != null &&
            masterSlider != null &&
            masterValueText != null &&
            sfxSlider != null &&
            sfxValueText != null &&
            bgmSlider != null &&
            bgmValueText != null &&
            languageDropdown != null &&
            displayPreviousButton != null &&
            displayNextButton != null &&
            displayValueText != null &&
            pauseButton != null &&
            pauseButtonText != null &&
            applyButton != null &&
            exitButton != null &&
            exitButtonText != null &&
            confirmRoot != null &&
            confirmLeaveButton != null &&
            confirmCancelButton != null &&
            noticeRoot != null &&
            noticeMessageText != null &&
            noticeOkButton != null &&
            pauseBannerRoot != null &&
            pauseBannerText != null &&
            pauseTimerText != null &&
            pauseReleaseButton != null &&
            menuRoot.transform.IsChildOf(transform) &&
            confirmRoot.transform.IsChildOf(transform) &&
            noticeRoot.transform.IsChildOf(transform) &&
            pauseBannerRoot.transform.IsChildOf(transform);

        public void Configure(
            Canvas canvas,
            GraphicRaycaster raycaster,
            Button gear,
            GameObject menu,
            Button close,
            Slider master,
            Text masterValue,
            Slider sfx,
            Text sfxValue,
            Slider bgm,
            Text bgmValue,
            Dropdown language,
            Button displayPrevious,
            Button displayNext,
            Text displayValue,
            Button pause,
            Text pauseText,
            Button apply,
            Button exit,
            Text exitText,
            GameObject confirm,
            Button confirmLeave,
            Button confirmCancel,
            GameObject notice,
            Text noticeMessage,
            Button noticeOk,
            GameObject pauseBanner,
            Text pauseBannerMessage,
            Text pauseTimer,
            Button pauseRelease)
        {
            rootCanvas = canvas;
            rootRaycaster = raycaster;
            gearButton = gear;
            menuRoot = menu;
            closeButton = close;
            masterSlider = master;
            masterValueText = masterValue;
            sfxSlider = sfx;
            sfxValueText = sfxValue;
            bgmSlider = bgm;
            bgmValueText = bgmValue;
            languageDropdown = language;
            displayPreviousButton = displayPrevious;
            displayNextButton = displayNext;
            displayValueText = displayValue;
            pauseButton = pause;
            pauseButtonText = pauseText;
            applyButton = apply;
            exitButton = exit;
            exitButtonText = exitText;
            confirmRoot = confirm;
            confirmLeaveButton = confirmLeave;
            confirmCancelButton = confirmCancel;
            noticeRoot = notice;
            noticeMessageText = noticeMessage;
            noticeOkButton = noticeOk;
            pauseBannerRoot = pauseBanner;
            pauseBannerText = pauseBannerMessage;
            pauseTimerText = pauseTimer;
            pauseReleaseButton = pauseRelease;
        }
    }
}
