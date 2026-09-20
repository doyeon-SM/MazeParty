using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace MazeParty.Gameplay.Testbed
{
    public sealed class GameplayTestbedController : MonoBehaviour
    {
        private static readonly GameplayItemDefinition PulseBlaster = new GameplayItemDefinition(
            "pulse_blaster",
            "Pulse Blaster",
            "LMB: ray attack. Deals 20 item damage and push. Seven shots.",
            7);

        private static readonly GameplayItemDefinition PushMine = new GameplayItemDefinition(
            "push_mine",
            "Push Mine",
            "LMB: area attack around the player. Damage and strong push.",
            1);

        private static readonly GameplayItemDefinition MedKit = new GameplayItemDefinition(
            "med_kit",
            "Med Kit",
            "LMB: restore 35 HP. Consumed immediately.",
            1);

        [Header("Scene references")]
        [SerializeField] private GameplayInputSource inputSource;
        [SerializeField] private GameplayPlayerMotor playerMotor;
        [SerializeField] private GameplayHealth playerHealth;
        [SerializeField] private GameplayCameraDirector cameraDirector;
        [SerializeField] private Transform firstPersonSpawn;
        [SerializeField] private Transform boardSpawn;
        [SerializeField] private Transform minigameSpawn;
        [SerializeField] private GameplayTestbedUiBindings uiBindings;

        private readonly GameplayPhaseClock _phaseClock = new GameplayPhaseClock();
        private readonly GameplayInventory _inventory = new GameplayInventory();

        private GameObject _selectionPanel;
        private Text _actionTimerText;
        private Text _shieldTimerText;
        private Text _choiceTimerText;
        private Text _healthText;
        private Text _ammoText;
        private Text _statusText;
        private Text _tooltipText;
        private Image[] _slotBackgrounds;
        private Text[] _slotLabels;
        private Button[] _choiceButtons;
        private Text[] _choiceButtonLabels;
        private TestbedItemChoiceButton[] _choicePresenters;

        private ItemChoiceResolution _lastChoiceResolution;
        private bool _manualPointerRelease;
        private bool _actionEnded;
        private double _nextPrimaryRepeatAt;

        public GameplayInventory Inventory => _inventory;
        public GameplayPhaseClock PhaseClock => _phaseClock;

        public void Configure(
            GameplayInputSource input,
            GameplayPlayerMotor motor,
            GameplayHealth health,
            GameplayCameraDirector cameras,
            Transform firstPerson,
            Transform board,
            Transform minigame,
            GameplayTestbedUiBindings bindings)
        {
            inputSource = input;
            playerMotor = motor;
            playerHealth = health;
            cameraDirector = cameras;
            firstPersonSpawn = firstPerson;
            boardSpawn = board;
            minigameSpawn = minigame;
            uiBindings = bindings;
        }

        private void Awake()
        {
            if (uiBindings == null || !uiBindings.HasRequiredReferences)
            {
                Debug.LogError(
                    "GameplayTestbedController requires a complete " +
                    "GameplayTestbedUiBindings component from " +
                    "GameplayTestbedCanvas.prefab.",
                    this);
                enabled = false;
                return;
            }

            ApplyUiBindings();
            WireButtons();
            _inventory.Changed += RefreshInventoryUi;
        }

        private void Start()
        {
            playerHealth.ConfigureProtection(_phaseClock, () => Time.timeAsDouble);
            playerHealth.HealthChanged += OnHealthChanged;
            playerHealth.DamageBlocked += OnDamageBlocked;

            ResetInventory();
            SwitchMode(GameplayMode.BoardTopDown);
            _selectionPanel.SetActive(false);
            _lastChoiceResolution = _phaseClock.ChoiceResolution;
            SetStatus("TOP VIEW PREVIEW - click START ACTION to begin the shared 3:00 clock.");
            RefreshAllUi();
        }

        private void OnDestroy()
        {
            _inventory.Changed -= RefreshInventoryUi;
            UnwireButtons();
            if (_choicePresenters != null)
            {
                for (var index = 0; index < _choicePresenters.Length; index++)
                {
                    _choicePresenters[index]?.Unbind(this);
                }
            }
            if (playerHealth != null)
            {
                playerHealth.HealthChanged -= OnHealthChanged;
                playerHealth.DamageBlocked -= OnDamageBlocked;
            }
        }

        private void Update()
        {
            var now = Time.timeAsDouble;
            var previousResolution = _phaseClock.ChoiceResolution;
            _phaseClock.Tick(now);

            if (previousResolution == ItemChoiceResolution.Pending
                && _phaseClock.ChoiceResolution == ItemChoiceResolution.TimedOut)
            {
                _inventory.ClearSelection();
                SetStatus("Item choice timed out: DO NOT USE was selected automatically.");
            }

            if (_phaseClock.IsRunning && _phaseClock.IsActionExpired(now) && !_actionEnded)
                EndActionPhase();

            HandleDebugShortcuts();
            HandleGameplayInput();
            RefreshAllUi();

            if (_lastChoiceResolution != _phaseClock.ChoiceResolution)
            {
                _lastChoiceResolution = _phaseClock.ChoiceResolution;
                ApplySelectionVisibility();
            }
        }

        public void StartActionPhase()
        {
            _phaseClock.Start(Time.timeAsDouble);
            _inventory.ClearSelection();
            _actionEnded = false;
            _manualPointerRelease = false;
            playerHealth.ResetHealth();
            SwitchMode(GameplayMode.FirstPerson);
            SetStatus("ACTION STARTED - choose an item within 30 seconds. HP shield is active for 5 seconds.");
            ApplySelectionVisibility();
            RefreshAllUi();
        }

        public void EndActionPhase()
        {
            if (_actionEnded)
                return;

            _actionEnded = true;
            var removedActiveItem = _inventory.EndSelectedItemUse();
            _phaseClock.Stop();
            _selectionPanel.SetActive(false);
            _manualPointerRelease = true;
            cameraDirector.SetUiPointerVisible(true);
            SetStatus(removedActiveItem
                ? "ACTION ENDED - the active item and its remaining charges were removed."
                : "ACTION ENDED.");
            RefreshAllUi();
        }

        public void ResetTestbed()
        {
            _phaseClock.Stop();
            _actionEnded = false;
            _manualPointerRelease = false;
            playerHealth.ResetHealth();
            ResetInventory();

            var targets = FindObjectsByType<TestbedTarget>();
            for (var i = 0; i < targets.Length; i++)
                targets[i].ResetState();

            SwitchMode(GameplayMode.BoardTopDown);
            _selectionPanel.SetActive(false);
            SetStatus("TESTBED RESET - top view preview is ready.");
            RefreshAllUi();
        }

        public void SelectItem(int slotIndex)
        {
            if (!_phaseClock.IsChoicePending)
                return;

            var slot = slotIndex >= 0 && slotIndex < GameplayInventory.Capacity
                ? _inventory.Slots[slotIndex]
                : null;
            if (slot == null)
            {
                SetStatus("That item slot is empty.");
                return;
            }

            var now = Time.timeAsDouble;
            if (!_phaseClock.TrySelectItem(slotIndex, now) || !_inventory.TrySelect(slotIndex))
                return;

            SetStatus(slot.Definition.DisplayName + " is ACTIVE. The shared action timer keeps running.");
            HideItemTooltip();
            ApplySelectionVisibility();
        }

        public void ChooseNoItem()
        {
            if (!_phaseClock.TryChooseNoItem(Time.timeAsDouble))
                return;

            _inventory.ClearSelection();
            SetStatus("DO NOT USE selected. The shared action timer keeps running.");
            HideItemTooltip();
            ApplySelectionVisibility();
        }

        public void ShowItemTooltip(int slotIndex)
        {
            if (_tooltipText == null)
                return;

            var slot = slotIndex >= 0 && slotIndex < GameplayInventory.Capacity
                ? _inventory.Slots[slotIndex]
                : null;
            _tooltipText.text = slot == null
                ? "Empty slot"
                : slot.Definition.DisplayName + "\n" + slot.Definition.Description;
            _tooltipText.gameObject.SetActive(true);
        }

        public void HideItemTooltip()
        {
            if (_tooltipText != null)
                _tooltipText.gameObject.SetActive(false);
        }

        public void SimulateIncomingItemHit()
        {
            var impulse = -playerMotor.transform.forward * 5f + Vector3.up * 1.5f;
            var report = GameplayHitResolver.Resolve(
                playerMotor.gameObject,
                new DamageRequest(20, DamageKind.Item, gameObject),
                impulse);

            SetStatus("REMOTE ITEM HIT: damage " + report.DamageResult
                + ", push " + (report.PushApplied ? "APPLIED" : "MISSED")
                + ". Selection UI remains unchanged.");
        }

        public void SimulateIncomingPush()
        {
            playerMotor.ApplyPush(-playerMotor.transform.forward * 6f + Vector3.up * 1.5f);
            SetStatus("REMOTE PUSH: APPLIED. Damage protection never blocks displacement.");
        }

        public void TryAddReward()
        {
            var added = _inventory.TryAdd(new GameplayItemDefinition(
                "bonus_orb",
                "Bonus Orb",
                "Test reward item. LMB consumes it.",
                1));

            SetStatus(added
                ? "Reward added to the first empty slot."
                : "Inventory full - reward acquisition was rejected (no discard prompt).");
        }

        public void SwitchToFirstPerson()
        {
            SwitchMode(GameplayMode.FirstPerson);
        }

        public void SwitchToBoard()
        {
            SwitchMode(GameplayMode.BoardTopDown);
        }

        public void SwitchToMinigame()
        {
            SwitchMode(GameplayMode.Minigame);
        }

        private void HandleDebugShortcuts()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;

            if (keyboard.f1Key.wasPressedThisFrame)
                SwitchToFirstPerson();
            if (keyboard.f2Key.wasPressedThisFrame)
                SwitchToBoard();
            if (keyboard.f3Key.wasPressedThisFrame)
                SwitchToMinigame();
            if (keyboard.rKey.wasPressedThisFrame)
                ResetTestbed();
        }

        private void HandleGameplayInput()
        {
            if (inputSource == null || playerMotor == null)
                return;

            if (inputSource.CancelPressed && cameraDirector.ActiveMode == GameplayMode.FirstPerson
                && !_phaseClock.IsChoicePending)
            {
                _manualPointerRelease = !_manualPointerRelease;
                cameraDirector.SetUiPointerVisible(_manualPointerRelease);
            }

            var selectionOpen = _phaseClock.IsChoicePending;
            var directInputAllowed = !selectionOpen && !_manualPointerRelease;
            var allowMouseLook = cameraDirector.ActiveMode == GameplayMode.FirstPerson;
            playerMotor.Tick(
                inputSource.Move,
                inputSource.Look,
                inputSource.WalkHeld,
                directInputAllowed,
                allowMouseLook);
            playerMotor.AvatarVisual?.SetOwnerFirstPerson(
                cameraDirector.ActiveMode == GameplayMode.FirstPerson);

            if (!directInputAllowed || IsPointerOverUi())
                return;

            var repeatPrimary = ShouldRepeatPrimaryAction();
            if (_inventory.SelectedSlot != null)
            {
                if (inputSource.PrimaryPressed)
                    UseActiveItem();
            }
            else if (repeatPrimary)
            {
                UseActiveItem();
            }
            if (inputSource.SecondaryPressed)
                TryInteract();
        }

        private bool ShouldRepeatPrimaryAction()
        {
            if (!inputSource.PrimaryHeld)
            {
                _nextPrimaryRepeatAt = 0d;
                return false;
            }

            var now = Time.unscaledTimeAsDouble;
            if (!inputSource.PrimaryPressed && now < _nextPrimaryRepeatAt)
            {
                return false;
            }

            _nextPrimaryRepeatAt =
                now + PlayerUnarmedRules.PunchCooldownSeconds;
            return true;
        }

        private void UseActiveItem()
        {
            var selected = _inventory.SelectedSlot;
            if (selected == null)
            {
                playerMotor.AvatarVisual?.TriggerPunch();
                SetStatus("PRIMARY: alternating unarmed punch. No item or HP damage applied.");
                return;
            }

            playerMotor.AvatarVisual?.TriggerItemUse(ToPrototypeItemId(selected.Definition.Id));
            switch (selected.Definition.Id)
            {
                case "pulse_blaster":
                    FirePulseBlaster();
                    _inventory.TryConsumeSelectedCharge();
                    break;
                case "push_mine":
                    TriggerPushMine();
                    _inventory.TryConsumeSelectedCharge();
                    break;
                case "med_kit":
                    playerHealth.Heal(35);
                    _inventory.TryConsumeSelectedCharge();
                    SetStatus("MED KIT used: restored 35 HP.");
                    break;
                default:
                    _inventory.TryConsumeSelectedCharge();
                    SetStatus(selected.Definition.DisplayName + " consumed.");
                    break;
            }

            if (_inventory.SelectedSlot == null)
                SetStatus(_statusText.text + " Active slot cleared.");
        }

        private static PrototypeItemId ToPrototypeItemId(string itemId)
        {
            switch (itemId)
            {
                case "pulse_blaster": return PrototypeItemId.PulseBlaster;
                case "push_mine": return PrototypeItemId.PushMine;
                case "med_kit": return PrototypeItemId.MedKit;
                default: return PrototypeItemId.None;
            }
        }

        private void FirePulseBlaster()
        {
            var ray = BuildPointerRay();
            var report = FirearmHitResolver.Raycast(
                playerMotor.gameObject,
                ray.origin,
                ray.direction,
                FirearmDamageRules.PulseBlasterRange,
                FirearmDamageRules.PulseBlasterBaseDamage,
                ray.direction * FirearmDamageRules.PulseBlasterPush + Vector3.up * 0.5f);
            if (!report.Hit)
            {
                SetStatus("PULSE BLASTER fired: no target. Ammo consumed.");
                return;
            }

            SetStatus("PULSE BLASTER hit " + report.Target.name + " [" + report.Region + "]"
                + ": damage " + report.Damage + " / " + report.DamageResult
                + ", push " + (report.PushApplied ? "APPLIED" : "NONE") + ".");
        }

        private void TriggerPushMine()
        {
            var colliders = Physics.OverlapSphere(
                playerMotor.transform.position,
                5f,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            var resolvedRoots = new HashSet<GameObject>();
            var hitCount = 0;

            for (var i = 0; i < colliders.Length; i++)
            {
                var candidate = colliders[i].transform.root.gameObject;
                if (candidate == playerMotor.transform.root.gameObject || !resolvedRoots.Add(candidate))
                    continue;

                var direction = candidate.transform.position - playerMotor.transform.position;
                direction.y = 0.25f;
                if (direction.sqrMagnitude < 0.01f)
                    direction = playerMotor.transform.forward;

                var report = GameplayHitResolver.Resolve(
                    colliders[i].gameObject,
                    new DamageRequest(35, DamageKind.Item, playerMotor.gameObject),
                    direction.normalized * 10f);
                if (report.DamageResult != DamageResult.Ignored || report.PushApplied)
                    hitCount++;
            }

            SetStatus("PUSH MINE triggered: affected " + hitCount + " target(s).");
        }

        private void TryInteract()
        {
            var ray = BuildPointerRay();
            if (!Physics.Raycast(ray, out var hit, 5f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Collide))
            {
                SetStatus("SECONDARY: no interactable in range.");
                return;
            }

            var interactable = FindInterfaceInParents<IInteractable>(hit.collider.gameObject);
            if (interactable == null)
            {
                SetStatus("SECONDARY: " + hit.collider.name + " is not interactable.");
                return;
            }

            interactable.Interact(playerMotor.gameObject);
            if (interactable is TestbedTarget target && !string.IsNullOrEmpty(target.LastInteractionMessage))
                SetStatus(target.LastInteractionMessage);
            else
                SetStatus(interactable.InteractionPrompt + " completed.");
        }

        private Ray BuildPointerRay()
        {
            var camera = cameraDirector.OutputCamera;
            var screenPoint = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            if (cameraDirector.ActiveMode != GameplayMode.FirstPerson && Mouse.current != null)
                screenPoint = Mouse.current.position.ReadValue();
            return camera.ScreenPointToRay(screenPoint);
        }

        private void SwitchMode(GameplayMode mode)
        {
            cameraDirector.SwitchTo(mode);
            var spawn = mode == GameplayMode.FirstPerson
                ? firstPersonSpawn
                : mode == GameplayMode.BoardTopDown ? boardSpawn : minigameSpawn;

            if (spawn != null)
                playerMotor.Teleport(spawn.position, spawn.rotation);

            if (mode != GameplayMode.FirstPerson)
                _manualPointerRelease = false;

            cameraDirector.SetUiPointerVisible(_phaseClock.IsChoicePending || _manualPointerRelease);
            SetStatus("CAMERA MODE: " + ModeLabel(mode) +
                      ". WASD move; hold LCTRL for quiet walk (6m sound radius).");
        }

        private void ApplySelectionVisibility()
        {
            var visible = _phaseClock.IsChoicePending;
            if (_selectionPanel != null)
                _selectionPanel.SetActive(visible);

            cameraDirector.SetUiPointerVisible(visible || _manualPointerRelease);
            if (!visible)
                HideItemTooltip();
        }

        private void ResetInventory()
        {
            _inventory.Reset();
            _inventory.TryAdd(PulseBlaster);
            _inventory.TryAdd(PushMine);
            _inventory.TryAdd(MedKit);
        }

        private void RefreshAllUi()
        {
            var now = Time.timeAsDouble;

            if (_actionTimerText != null)
                _actionTimerText.text = _phaseClock.IsRunning
                    ? "ACTION  " + FormatClock(_phaseClock.GetActionRemaining(now))
                    : "TOP VIEW / IDLE";

            if (_shieldTimerText != null)
            {
                var remaining = _phaseClock.GetOpeningProtectionRemaining(now);
                _shieldTimerText.text = remaining > 0d
                    ? "HP SHIELD  " + remaining.ToString("0.0") + "s"
                    : _phaseClock.IsRunning ? "HP SHIELD  OFF" : "HP SHIELD  --";
                _shieldTimerText.color = remaining > 0d
                    ? uiBindings.ShieldActiveColor
                    : uiBindings.ShieldInactiveColor;
            }

            if (_choiceTimerText != null)
                _choiceTimerText.text = _phaseClock.IsChoicePending
                    ? "CHOOSE  " + _phaseClock.GetChoiceRemaining(now).ToString("0.0") + "s"
                    : "CHOICE  " + ChoiceLabel(_phaseClock.ChoiceResolution);

            if (_healthText != null)
                _healthText.text = "HP  " + playerHealth.CurrentHealth + " / " + playerHealth.MaxHealth;

            if (_selectionPanel != null && _selectionPanel.activeSelf != _phaseClock.IsChoicePending)
                _selectionPanel.SetActive(_phaseClock.IsChoicePending);

            RefreshInventoryUi();
        }

        private void RefreshInventoryUi()
        {
            for (var i = 0; i < GameplayInventory.Capacity; i++)
            {
                var slot = _inventory.Slots[i];
                if (_slotLabels[i] != null)
                    _slotLabels[i].text = slot == null
                        ? "EMPTY"
                        : slot.Definition.DisplayName + (slot.Definition.InitialCharges > 1
                            ? "\n" + slot.Charges
                            : string.Empty);

                if (_slotBackgrounds[i] != null)
                {
                    _slotBackgrounds[i].color = i == _inventory.SelectedSlotIndex
                        ? uiBindings.InventorySelectedColor
                        : slot == null
                            ? uiBindings.InventoryEmptyColor
                            : uiBindings.InventoryOccupiedColor;
                }

                if (_choiceButtons[i] != null)
                {
                    _choiceButtons[i].interactable = slot != null;
                    _choiceButtonLabels[i].text = slot == null
                        ? "EMPTY"
                        : slot.Definition.DisplayName
                            + (slot.Definition.InitialCharges > 1 ? "  x" + slot.Charges : string.Empty);
                }
            }

            var selected = _inventory.SelectedSlot;
            if (_ammoText != null)
                _ammoText.text = selected != null && selected.Definition.InitialCharges > 1
                    ? "AMMO  " + selected.Charges
                    : "AMMO  --";
        }

        private void ApplyUiBindings()
        {
            _selectionPanel = uiBindings.SelectionPanel;
            _actionTimerText = uiBindings.ActionTimerText;
            _shieldTimerText = uiBindings.ShieldTimerText;
            _choiceTimerText = uiBindings.ChoiceTimerText;
            _healthText = uiBindings.HealthText;
            _ammoText = uiBindings.AmmoText;
            _statusText = uiBindings.StatusText;
            _tooltipText = uiBindings.TooltipText;
            _slotBackgrounds = uiBindings.SlotBackgrounds;
            _slotLabels = uiBindings.SlotLabels;
            _choiceButtons = uiBindings.ChoiceButtons;
            _choiceButtonLabels = uiBindings.ChoiceButtonLabels;
            _choicePresenters = uiBindings.ChoicePresenters;
            for (var index = 0; index < _choicePresenters.Length; index++)
            {
                _choicePresenters[index].Bind(this);
            }
        }

        private void WireButtons()
        {
            uiBindings.StartActionButton.onClick.AddListener(StartActionPhase);
            uiBindings.NoItemButton.onClick.AddListener(ChooseNoItem);
            uiBindings.IncomingHitButton.onClick.AddListener(SimulateIncomingItemHit);
            uiBindings.IncomingPushButton.onClick.AddListener(SimulateIncomingPush);
            uiBindings.AddRewardButton.onClick.AddListener(TryAddReward);
            uiBindings.EndActionButton.onClick.AddListener(EndActionPhase);
            uiBindings.ResetButton.onClick.AddListener(ResetTestbed);
            uiBindings.FirstPersonButton.onClick.AddListener(SwitchToFirstPerson);
            uiBindings.BoardButton.onClick.AddListener(SwitchToBoard);
            uiBindings.MinigameButton.onClick.AddListener(SwitchToMinigame);
        }

        private void UnwireButtons()
        {
            if (uiBindings == null)
            {
                return;
            }

            uiBindings.StartActionButton?.onClick.RemoveListener(StartActionPhase);
            uiBindings.NoItemButton?.onClick.RemoveListener(ChooseNoItem);
            uiBindings.IncomingHitButton?.onClick.RemoveListener(SimulateIncomingItemHit);
            uiBindings.IncomingPushButton?.onClick.RemoveListener(SimulateIncomingPush);
            uiBindings.AddRewardButton?.onClick.RemoveListener(TryAddReward);
            uiBindings.EndActionButton?.onClick.RemoveListener(EndActionPhase);
            uiBindings.ResetButton?.onClick.RemoveListener(ResetTestbed);
            uiBindings.FirstPersonButton?.onClick.RemoveListener(SwitchToFirstPerson);
            uiBindings.BoardButton?.onClick.RemoveListener(SwitchToBoard);
            uiBindings.MinigameButton?.onClick.RemoveListener(SwitchToMinigame);
        }

        private void OnHealthChanged(int current, int maximum)
        {
            if (_healthText != null)
                _healthText.text = "HP  " + current + " / " + maximum;
        }

        private void OnDamageBlocked(DamageRequest request)
        {
            SetStatus("HP DAMAGE BLOCKED by the opening shield. Hit and push still resolved.");
        }

        private void SetStatus(string message)
        {
            if (_statusText != null)
                _statusText.text = message;
            Debug.Log("[Gameplay Testbed] " + message, this);
        }

        private static bool IsPointerOverUi()
        {
            return EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
        }

        private static T FindInterfaceInParents<T>(GameObject target) where T : class
        {
            var current = target.transform;
            while (current != null)
            {
                var behaviours = current.GetComponents<MonoBehaviour>();
                for (var i = 0; i < behaviours.Length; i++)
                {
                    if (behaviours[i] is T match)
                        return match;
                }
                current = current.parent;
            }
            return null;
        }

        private static string FormatClock(double seconds)
        {
            var wholeSeconds = Mathf.CeilToInt((float)seconds);
            return (wholeSeconds / 60).ToString("00") + ":" + (wholeSeconds % 60).ToString("00");
        }

        private static string ChoiceLabel(ItemChoiceResolution resolution)
        {
            switch (resolution)
            {
                case ItemChoiceResolution.ItemSelected:
                    return "ITEM ACTIVE";
                case ItemChoiceResolution.DoNotUse:
                    return "DO NOT USE";
                case ItemChoiceResolution.TimedOut:
                    return "TIMEOUT / NO ITEM";
                default:
                    return "--";
            }
        }

        private static string ModeLabel(GameplayMode mode)
        {
            switch (mode)
            {
                case GameplayMode.FirstPerson:
                    return "FIRST PERSON";
                case GameplayMode.BoardTopDown:
                    return "BOARD TOP VIEW";
                default:
                    return "MINIGAME";
            }
        }

        // Online/Steam integration seam:
        // only the locally-owned player reads GameplayInputSource. In online play the
        // host validates hit/use requests, broadcasts the shared StartedAt timestamp,
        // and stores each player's personal choice resolution independently.
    }
}
