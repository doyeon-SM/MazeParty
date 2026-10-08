using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Common menu shared by the lobby, the waiting room and the match.
    /// Sound sliders preview immediately; language and screen mode apply only
    /// with the Apply button; closing without Apply restores the saved values.
    /// Runs late so the hold-to-open emote wheel can consume Escape first;
    /// dismissible menus and gameplay popups otherwise share UiPopupStack.
    /// </summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class GameMenuView : MonoBehaviour
    {
        [SerializeField] private GameMenuBindings bindings;

        private readonly List<Dropdown.OptionData> _languageOptions =
            new List<Dropdown.OptionData>();
        private GameSettingsData _draft;
        private bool _menuOpen;
        private bool _confirmOpen;
        private bool _noticeOpen;
        private bool _wired;
        private bool _gameplayOverlayOpenLastFrame;
        private bool _dropdownExpandedLastFrame;

        public GameMenuBindings Bindings => bindings;
        public bool IsMenuOpen => _menuOpen;
        public bool IsConfirmOpen => _confirmOpen;
        public bool IsNoticeOpen => _noticeOpen;

        public void Configure(GameMenuBindings configuredBindings)
        {
            bindings = configuredBindings;
        }

        private void Awake()
        {
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                Debug.LogError("GameMenuCanvas bindings are missing.", this);
                enabled = false;
                return;
            }

            bindings.MenuRoot.SetActive(false);
            bindings.ConfirmRoot.SetActive(false);
            bindings.NoticeRoot.SetActive(false);
            bindings.PauseBannerRoot.SetActive(false);
            PopulateLanguageOptions();
        }

        private void OnEnable()
        {
            if (bindings == null || !bindings.HasRequiredReferences)
            {
                return;
            }

            Wire(true);
        }

        private void OnDisable()
        {
            if (_wired)
            {
                Wire(false);
            }

            if (_menuOpen)
            {
                GameSettings.RevertPreview();
            }

            _menuOpen = false;
            _confirmOpen = false;
            _noticeOpen = false;
            if (bindings != null)
            {
                bindings.NoticeRoot?.SetActive(false);
                bindings.ConfirmRoot?.SetActive(false);
                bindings.MenuRoot?.SetActive(false);
            }
            UiPopupStack.Remove(bindings != null ? bindings.NoticeRoot : null);
            UiPopupStack.Remove(bindings != null ? bindings.ConfirmRoot : null);
            UiPopupStack.Remove(bindings != null ? bindings.MenuRoot : null);
            LocalInputGate.SetPausePointerRequested(false);
        }

        private void Update()
        {
            UiPopupStack.Refresh();
            var controller = OnlineSessionController.Instance;
            var context = controller != null
                ? controller.MenuContext
                : GameMenuContext.Lobby;

            if (_confirmOpen && GameMenuRules.GetExitAction(context) !=
                GameMenuExitAction.ConfirmMatchLeave)
            {
                SetConfirmOpen(false);
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                HandleEscape();
            }

            if (!_noticeOpen && controller != null &&
                controller.TryDequeueNotice(out var notice))
            {
                ShowNotice(notice);
            }

            RefreshGearButton(context);
            RefreshPauseBanner();
            if (_menuOpen)
            {
                RefreshMenu(context, controller);
            }

        }

        private void LateUpdate()
        {
            _gameplayOverlayOpenLastFrame =
                HandEmoteWheelView.BlocksPointerInput ||
                BoardUtilityItemView.IsTargetPickerOpen ||
                BoardFlowView.IsItemShopOpen;
            _dropdownExpandedLastFrame = IsLanguageDropdownExpanded();

            // Gameplay views set their own cursor policy during Update; the menu,
            // the notice popup and the pause release button need it free.
            LocalInputGate.ApplyPointerOverride();
        }

        public void OpenMenu()
        {
            if (_menuOpen || !enabled)
            {
                return;
            }

            _draft = GameSettings.Applied;
            _menuOpen = true;
            bindings.MenuRoot.SetActive(true);
            UiPopupStack.Push(bindings.MenuRoot, CloseMenu);
            GameSound.Play(SoundKeys.UiPopupOpen);
            PushDraftToControls();
            ClearSelection();
            var controller = OnlineSessionController.Instance;
            RefreshMenu(
                controller != null ? controller.MenuContext : GameMenuContext.Lobby,
                controller);
        }

        /// <summary>Closes the menu; unapplied changes are discarded.</summary>
        public void CloseMenu()
        {
            if (!_menuOpen)
            {
                UiPopupStack.Remove(bindings != null ? bindings.MenuRoot : null);
                return;
            }

            _menuOpen = false;
            GameSettings.RevertPreview();
            SetConfirmOpen(false);
            if (bindings.LanguageDropdown != null)
            {
                bindings.LanguageDropdown.Hide();
            }

            bindings.MenuRoot.SetActive(false);
            UiPopupStack.Remove(bindings.MenuRoot);
            ClearSelection();
        }

        private void HandleEscape()
        {
            if (_menuOpen && UiPopupStack.IsTop(bindings.MenuRoot))
            {
                if (_dropdownExpandedLastFrame)
                {
                    // The dropdown list closes itself on Cancel.
                    return;
                }
            }

            if (UiPopupStack.TryCloseTop())
            {
                return;
            }

            // The hold-to-open emote wheel consumes Escape before the menu opens.
            if (_gameplayOverlayOpenLastFrame || HandEmoteWheelView.BlocksPointerInput)
            {
                return;
            }

            // A gameplay state or close button may have removed the final popup
            // earlier this frame. Do not replay that same Escape as menu-open.
            if (LocalInputGate.BlocksGameplayInput)
            {
                return;
            }

            OpenMenu();
        }

        private void Wire(bool subscribe)
        {
            if (subscribe == _wired)
            {
                return;
            }

            _wired = subscribe;
            if (subscribe)
            {
                bindings.GearButton.onClick.AddListener(OnGearClicked);
                bindings.CloseButton.onClick.AddListener(CloseMenu);
                bindings.MasterSlider.onValueChanged.AddListener(OnMasterChanged);
                bindings.SfxSlider.onValueChanged.AddListener(OnSfxChanged);
                bindings.BgmSlider.onValueChanged.AddListener(OnBgmChanged);
                bindings.LanguageDropdown.onValueChanged.AddListener(OnLanguageChanged);
                bindings.DisplayPreviousButton.onClick.AddListener(OnDisplayPrevious);
                bindings.DisplayNextButton.onClick.AddListener(OnDisplayNext);
                bindings.ResolutionPreviousButton.onClick.AddListener(OnResolutionPrevious);
                bindings.ResolutionNextButton.onClick.AddListener(OnResolutionNext);
                bindings.QualityPreviousButton.onClick.AddListener(OnQualityPrevious);
                bindings.QualityNextButton.onClick.AddListener(OnQualityNext);
                bindings.FrameRatePreviousButton.onClick.AddListener(OnFrameRatePrevious);
                bindings.FrameRateNextButton.onClick.AddListener(OnFrameRateNext);
                bindings.MouseSensitivitySlider.onValueChanged.AddListener(
                    OnMouseSensitivityChanged);
                bindings.InvertYToggle.onValueChanged.AddListener(OnInvertYChanged);
                bindings.ReduceScreenShakeToggle.onValueChanged.AddListener(
                    OnReduceScreenShakeChanged);
                bindings.ReduceFlashesToggle.onValueChanged.AddListener(
                    OnReduceFlashesChanged);
                bindings.PauseButton.onClick.AddListener(OnPauseClicked);
                bindings.ApplyButton.onClick.AddListener(OnApplyClicked);
                bindings.ExitButton.onClick.AddListener(OnExitClicked);
                bindings.ConfirmLeaveButton.onClick.AddListener(OnConfirmLeave);
                bindings.ConfirmCancelButton.onClick.AddListener(OnConfirmCancel);
                bindings.NoticeOkButton.onClick.AddListener(DismissNotice);
                bindings.PauseReleaseButton.onClick.AddListener(OnPauseReleaseClicked);
                GameText.LanguageChanged += OnGameLanguageChanged;
                return;
            }

            bindings.GearButton.onClick.RemoveListener(OnGearClicked);
            bindings.CloseButton.onClick.RemoveListener(CloseMenu);
            bindings.MasterSlider.onValueChanged.RemoveListener(OnMasterChanged);
            bindings.SfxSlider.onValueChanged.RemoveListener(OnSfxChanged);
            bindings.BgmSlider.onValueChanged.RemoveListener(OnBgmChanged);
            bindings.LanguageDropdown.onValueChanged.RemoveListener(OnLanguageChanged);
            bindings.DisplayPreviousButton.onClick.RemoveListener(OnDisplayPrevious);
            bindings.DisplayNextButton.onClick.RemoveListener(OnDisplayNext);
            bindings.ResolutionPreviousButton.onClick.RemoveListener(OnResolutionPrevious);
            bindings.ResolutionNextButton.onClick.RemoveListener(OnResolutionNext);
            bindings.QualityPreviousButton.onClick.RemoveListener(OnQualityPrevious);
            bindings.QualityNextButton.onClick.RemoveListener(OnQualityNext);
            bindings.FrameRatePreviousButton.onClick.RemoveListener(OnFrameRatePrevious);
            bindings.FrameRateNextButton.onClick.RemoveListener(OnFrameRateNext);
            bindings.MouseSensitivitySlider.onValueChanged.RemoveListener(
                OnMouseSensitivityChanged);
            bindings.InvertYToggle.onValueChanged.RemoveListener(OnInvertYChanged);
            bindings.ReduceScreenShakeToggle.onValueChanged.RemoveListener(
                OnReduceScreenShakeChanged);
            bindings.ReduceFlashesToggle.onValueChanged.RemoveListener(
                OnReduceFlashesChanged);
            bindings.PauseButton.onClick.RemoveListener(OnPauseClicked);
            bindings.ApplyButton.onClick.RemoveListener(OnApplyClicked);
            bindings.ExitButton.onClick.RemoveListener(OnExitClicked);
            bindings.ConfirmLeaveButton.onClick.RemoveListener(OnConfirmLeave);
            bindings.ConfirmCancelButton.onClick.RemoveListener(OnConfirmCancel);
            bindings.NoticeOkButton.onClick.RemoveListener(DismissNotice);
            bindings.PauseReleaseButton.onClick.RemoveListener(OnPauseReleaseClicked);
            GameText.LanguageChanged -= OnGameLanguageChanged;
        }

        private void OnGearClicked()
        {
            if (_menuOpen)
            {
                CloseMenu();
            }
            else
            {
                OpenMenu();
            }
        }

        private void OnMasterChanged(float value)
        {
            _draft.MasterVolume = value;
            PreviewDraftVolumes();
        }

        private void OnSfxChanged(float value)
        {
            _draft.SfxVolume = value;
            PreviewDraftVolumes();
        }

        private void OnBgmChanged(float value)
        {
            _draft.BgmVolume = value;
            PreviewDraftVolumes();
        }

        private void PreviewDraftVolumes()
        {
            if (_menuOpen)
            {
                GameSettings.PreviewVolumes(
                    _draft.MasterVolume,
                    _draft.SfxVolume,
                    _draft.BgmVolume);
            }
        }

        private void OnLanguageChanged(int index)
        {
            _draft.Language = GameLanguages.FromIndex(index);
        }

        private void OnDisplayPrevious()
        {
            _draft.DisplayMode = DisplayModeOptions.Step(_draft.DisplayMode, -1);
        }

        private void OnDisplayNext()
        {
            _draft.DisplayMode = DisplayModeOptions.Step(_draft.DisplayMode, 1);
        }

        private void OnResolutionPrevious()
        {
            _draft.Resolution = ResolutionOptions.Step(_draft.Resolution, -1);
        }

        private void OnResolutionNext()
        {
            _draft.Resolution = ResolutionOptions.Step(_draft.Resolution, 1);
        }

        private void OnQualityPrevious()
        {
            _draft.QualityPreset =
                QualityPresetOptions.Step(_draft.QualityPreset, -1);
        }

        private void OnQualityNext()
        {
            _draft.QualityPreset =
                QualityPresetOptions.Step(_draft.QualityPreset, 1);
        }

        private void OnFrameRatePrevious()
        {
            _draft.FrameRateCap =
                FrameRateCapOptions.Step(_draft.FrameRateCap, -1);
        }

        private void OnFrameRateNext()
        {
            _draft.FrameRateCap =
                FrameRateCapOptions.Step(_draft.FrameRateCap, 1);
        }

        private void OnMouseSensitivityChanged(float value)
        {
            _draft.MouseSensitivity = value;
        }

        private void OnInvertYChanged(bool value)
        {
            _draft.InvertY = value;
        }

        private void OnReduceScreenShakeChanged(bool value)
        {
            _draft.ReduceScreenShake = value;
        }

        private void OnReduceFlashesChanged(bool value)
        {
            _draft.ReduceFlashes = value;
        }

        private void OnApplyClicked()
        {
            if (!_menuOpen)
            {
                return;
            }

            GameSettings.Apply(_draft);
            _draft = GameSettings.Applied;
            PushDraftToControls();
        }

        private void OnPauseClicked()
        {
            var controller = OnlineSessionController.Instance;
            var match = NetworkMatchState.Instance;
            if (controller == null || match == null || !match.IsSpawned)
            {
                return;
            }

            if (IsLocalPauseRequester(match))
            {
                controller.RequestPlayerPauseRelease();
            }
            else if (match.CanRequestPlayerPause)
            {
                controller.RequestPlayerPause();
            }

            CloseMenu();
        }

        private void OnPauseReleaseClicked()
        {
            var match = NetworkMatchState.Instance;
            if (match != null && IsLocalPauseRequester(match))
            {
                OnlineSessionController.Instance?.RequestPlayerPauseRelease();
            }
        }

        private void OnExitClicked()
        {
            var controller = OnlineSessionController.Instance;
            if (controller == null)
            {
                return;
            }

            switch (GameMenuRules.GetExitAction(controller.MenuContext))
            {
                case GameMenuExitAction.QuitApplication:
                    CloseMenu();
                    controller.RequestQuitGame();
                    break;
                case GameMenuExitAction.LeaveWaitingRoom:
                    CloseMenu();
                    controller.RequestLeaveWaitingRoom();
                    break;
                case GameMenuExitAction.ConfirmMatchLeave:
                    SetConfirmOpen(true);
                    break;
            }
        }

        private void OnConfirmLeave()
        {
            SetConfirmOpen(false);
            CloseMenu();
            OnlineSessionController.Instance?.RequestVoluntaryMatchLeave();
        }

        private void OnConfirmCancel()
        {
            SetConfirmOpen(false);
        }

        private void SetConfirmOpen(bool open)
        {
            _confirmOpen = open;
            bindings.ConfirmRoot.SetActive(open);
            if (open)
            {
                UiPopupStack.Push(bindings.ConfirmRoot, OnConfirmCancel);
                GameSound.Play(SoundKeys.UiPopupOpen);
                bindings.ConfirmRoot.transform.SetAsLastSibling();
                bindings.NoticeRoot.transform.SetAsLastSibling();
                ClearSelection();
            }
            else
            {
                UiPopupStack.Remove(bindings.ConfirmRoot);
            }
        }

        private void ShowNotice(string message)
        {
            _noticeOpen = true;
            bindings.NoticeMessageText.text = message;
            bindings.NoticeRoot.SetActive(true);
            UiPopupStack.Push(bindings.NoticeRoot, DismissNotice);
            GameSound.Play(SoundKeys.UiNotice);
            bindings.NoticeRoot.transform.SetAsLastSibling();
            ClearSelection();
        }

        private void DismissNotice()
        {
            _noticeOpen = false;
            bindings.NoticeRoot.SetActive(false);
            UiPopupStack.Remove(bindings.NoticeRoot);
            ClearSelection();
        }

        private void OnGameLanguageChanged()
        {
            if (_menuOpen)
            {
                PushDraftToControls();
            }
        }

        private void PushDraftToControls()
        {
            _draft = _draft.Sanitized();
            bindings.MasterSlider.SetValueWithoutNotify(_draft.MasterVolume);
            bindings.SfxSlider.SetValueWithoutNotify(_draft.SfxVolume);
            bindings.BgmSlider.SetValueWithoutNotify(_draft.BgmVolume);
            bindings.LanguageDropdown.SetValueWithoutNotify(
                GameLanguages.ToIndex(_draft.Language));
            bindings.LanguageDropdown.RefreshShownValue();
            bindings.MouseSensitivitySlider.SetValueWithoutNotify(
                _draft.MouseSensitivity);
            bindings.InvertYToggle.SetIsOnWithoutNotify(_draft.InvertY);
            bindings.ReduceScreenShakeToggle.SetIsOnWithoutNotify(
                _draft.ReduceScreenShake);
            bindings.ReduceFlashesToggle.SetIsOnWithoutNotify(
                _draft.ReduceFlashes);
        }

        private void RefreshMenu(GameMenuContext context, OnlineSessionController controller)
        {
            bindings.MasterValueText.text = FormatPercent(_draft.MasterVolume);
            bindings.SfxValueText.text = FormatPercent(_draft.SfxVolume);
            bindings.BgmValueText.text = FormatPercent(_draft.BgmVolume);
            bindings.DisplayValueText.text =
                GameText.T(DisplayModeOptions.GetLabelSource(_draft.DisplayMode));
            bindings.ResolutionValueText.text =
                ResolutionOptions.GetLabel(_draft.Resolution);
            bindings.QualityValueText.text = GameText.T(
                QualityPresetOptions.GetLabelSource(_draft.QualityPreset));
            bindings.FrameRateValueText.text = GameText.T(
                FrameRateCapOptions.GetLabelSource(_draft.FrameRateCap));
            bindings.MouseSensitivityValueText.text =
                _draft.MouseSensitivity.ToString("0.00") + "x";
            bindings.ApplyButton.interactable = !_draft.Equals(GameSettings.Applied);

            var leaving = controller != null && controller.IsVoluntaryLeavePending;
            bindings.ExitButtonText.text = GameText.T(GameMenuRules.GetExitLabelSource(context));
            bindings.ExitButton.interactable = !leaving &&
                (context != GameMenuContext.WaitingRoom || controller == null || !controller.IsBusy);

            var showPause = GameMenuRules.ShowsPauseButton(context);
            if (bindings.PauseButton.gameObject.activeSelf != showPause)
            {
                bindings.PauseButton.gameObject.SetActive(showPause);
            }

            if (showPause)
            {
                var match = NetworkMatchState.Instance;
                var requester = match != null && match.IsSpawned && IsLocalPauseRequester(match);
                bindings.PauseButtonText.text = requester
                    ? GameText.T("Release Pause")
                    : GameText.T("Request Pause");
                bindings.PauseButton.interactable = !leaving && match != null &&
                    match.IsSpawned && (requester || match.CanRequestPlayerPause);
            }
        }

        private void RefreshGearButton(GameMenuContext context)
        {
            var show = GameMenuRules.ShowsGearButton(context) &&
                       !_menuOpen && !_confirmOpen && !_noticeOpen;
            if (bindings.GearButton.gameObject.activeSelf != show)
            {
                bindings.GearButton.gameObject.SetActive(show);
            }
        }

        private void RefreshPauseBanner()
        {
            var match = NetworkMatchState.Instance;
            var paused = match != null && match.IsSpawned && match.IsPlayerPaused;
            if (bindings.PauseBannerRoot.activeSelf != paused)
            {
                bindings.PauseBannerRoot.SetActive(paused);
            }

            if (!paused)
            {
                LocalInputGate.SetPausePointerRequested(false);
                return;
            }

            var requester = IsLocalPauseRequester(match);
            bindings.PauseBannerText.text = requester
                ? GameText.T("You paused the game.")
                : GameText.F("Paused by {0}.", GetPauseRequesterName(match));
            bindings.PauseTimerText.text = GameMenuRules.FormatPauseClock(
                match.PlayerPauseRemaining);
            if (bindings.PauseReleaseButton.gameObject.activeSelf != requester)
            {
                bindings.PauseReleaseButton.gameObject.SetActive(requester);
            }

            LocalInputGate.SetPausePointerRequested(requester);
        }

        private static bool IsLocalPauseRequester(NetworkMatchState match)
        {
            if (match == null || !match.IsPlayerPauseActive)
            {
                return false;
            }

            var controller = OnlineSessionController.Instance;
            var local = controller != null ? controller.GetLocalAvatar() : null;
            return local != null && local.AssignedSlot >= 0 &&
                   local.AssignedSlot == match.PlayerPauseSlot;
        }

        private static string GetPauseRequesterName(NetworkMatchState match)
        {
            var slot = match.PlayerPauseSlot;
            var avatar = match.GetAvatarForSlot(slot);
            if (avatar != null && !string.IsNullOrWhiteSpace(avatar.DisplayName))
            {
                return avatar.DisplayName;
            }

            return GameText.F("Player {0}", slot + 1);
        }

        private void PopulateLanguageOptions()
        {
            _languageOptions.Clear();
            for (var index = 0; index < GameLanguages.Count; index++)
            {
                _languageOptions.Add(new Dropdown.OptionData(
                    GameLanguages.GetNativeName(GameLanguages.FromIndex(index))));
            }

            bindings.LanguageDropdown.ClearOptions();
            bindings.LanguageDropdown.AddOptions(_languageOptions);
        }

        private bool IsLanguageDropdownExpanded()
        {
            // The legacy Dropdown instantiates its list as a child named
            // "Dropdown List" while it is shown.
            return _menuOpen &&
                   bindings.LanguageDropdown.transform.Find("Dropdown List") != null;
        }

        private static string FormatPercent(float volume)
        {
            return Mathf.RoundToInt(Mathf.Clamp01(volume) * 100f).ToString();
        }

        private static void ClearSelection()
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }
    }
}
