using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.ArenaCombat;
using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Gameplay.Minigames.WrongWay;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private void HandleLocalLook()
        {
            if (HandEmoteWheelView.BlocksPointerInput) return;
            var match = NetworkMatchState.Instance;
            var minigameFirstPerson =
                match != null &&
                match.IsTagChasePlaying &&
                match.CurrentMinigameUsesFirstPersonForSlot(
                    AssignedSlot);
            var canLook =
                minigameFirstPerson ||
                (match != null &&
                 ((match.CanAcceptActionInput &&
                   HasResolvedItemChoice &&
                   CurrentHealth > 0 &&
                   !BoardFlowView.IsItemShopOpen && !BoardUtilityItemView.IsTargetPickerOpen && !IsSwapping) ||
                  match.CanAvatarUseCombatInput(this)) &&
                 Cursor.lockState == CursorLockMode.Locked);
            var mouse = LocalMouse;
            if (!canLook || mouse == null)
            {
                return;
            }

            var delta = ResolveLookDelta(
                mouse.delta.ReadValue(),
                lookSensitivity,
                GameSettings.Applied);
            _localYaw += delta.x;
            _localPitch = Mathf.Clamp(_localPitch + delta.y, -85f, 85f);
            ApplyLocalEyeRotation();
        }

        internal static Vector2 ResolveLookDelta(
            Vector2 rawMouseDelta,
            float authoredSensitivity,
            GameSettingsData settings)
        {
            settings = settings.Sanitized();
            var delta = rawMouseDelta *
                        (Mathf.Max(0f, authoredSensitivity) *
                         settings.MouseSensitivity);
            delta.y *= settings.InvertY ? 1f : -1f;
            return delta;
        }

        private void HandleLocalActionButtons()
        {
            if (HandEmoteWheelView.BlocksPointerInput) return;
            var match = NetworkMatchState.Instance;
            if (match != null && match.IsCliffBarragePlaying)
            {
                return;
            }
            if (match != null && match.IsSequenceMemoryPlaying)
            {
                return;
            }

            if (match != null && match.IsBombPassingPlaying)
            {
                return;
            }

            if (match != null && match.IsSnowySpinPlaying)
            {
                return;
            }

            if (match != null && match.IsRacePlaying)
            {
                return;
            }

            if (match != null && match.IsRedLightGreenLightPlaying)
            {
                return;
            }

            if (match != null && match.IsStableFootingPlaying)
            {
                return;
            }

            if (match != null && match.IsBalloonBlowPlaying)
            {
                return;
            }

            if (match != null && match.IsGiftGrabPlaying)
            {
                return;
            }

            var mouse = LocalMouse;
            if (mouse == null)
            {
                return;
            }

            if (match != null && match.IsWrongWayPlaying)
            {
                return;
            }

            if (match != null && match.IsMinefieldPlaying)
            {
                if (match.CanCurrentMinigameAcceptInputForSlot(AssignedSlot) &&
                    mouse.rightButton.wasPressedThisFrame)
                {
                    RequestMinefieldSonarRpc(_lastSentMinefieldInput);
                }
                return;
            }

            if (IsPointerOverUi())
            {
                return;
            }

            var lobbyInput = CanUseLobbyInput();
            var combatInput = match != null && match.CanAvatarUseCombatInput(this);
            var repeatPrimary = ShouldRepeatPrimaryAction(
                mouse,
                combatInput
                    ? BoardCombatRules.PunchCooldownSeconds
                    : PlayerUnarmedRules.PunchCooldownSeconds);

            if (lobbyInput)
            {
                if (repeatPrimary)
                {
                    var origin = transform.position + Vector3.up * 0.75f;
                    RequestLobbyPunchRpc(origin, transform.forward);
                }
                return;
            }

            if (match == null)
            {
                return;
            }

            if (combatInput)
            {
                if (repeatPrimary)
                {
                    var origin = eyePivot != null
                        ? eyePivot.position
                        : transform.position + Vector3.up * 0.75f;
                    var direction = eyePivot != null ? eyePivot.forward : transform.forward;
                    RequestCombatPunchRpc(origin, direction);
                }
                return;
            }

            if (!match.CanAcceptActionInput || !HasResolvedItemChoice ||
                CurrentHealth <= 0 ||
                (BoardFlowView.IsItemShopOpen || BoardUtilityItemView.IsTargetPickerOpen || IsSwapping))
            {
                return;
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (TryGetAimedBoardShop(out var shopHit))
                {
                    var tombstone = shopHit.collider.GetComponentInParent<BoardTombstoneMarker>();
                    if (tombstone != null)
                    {
                        RequestTombstonePickupRpc(tombstone.Id);
                        return;
                    }

                    var keyTarget = shopHit.collider.GetComponentInParent<KeyShopWorldTarget>();
                    if (keyTarget != null)
                    {
                        RequestKeyShopPurchaseRpc(match.KeyShopRevision);
                        return;
                    }

                    var itemTarget = shopHit.collider.GetComponentInParent<ItemShopWorldTarget>();
                    if (itemTarget != null)
                    {
                        BoardFlowView.Instance?.OpenItemShop(itemTarget.ShopIndex);
                        return;
                    }
                }

                if (TryGetAimedWorldDie(out var aimedDie, out var aimedRay) &&
                    aimedDie.AssignedSlot == AssignedSlot && !HasRolled)
                {
                    aimedDie.RequestRollFromLocalRay(aimedRay);
                }
            }

            if (repeatPrimary || (_selectedItemSlot.Value >= 0 && mouse.leftButton.wasPressedThisFrame))
            {
                if (TryGetAimedWorldDie(out var aimedDie, out var aimedRay))
                {
                    if (aimedDie.AssignedSlot == AssignedSlot && !HasRolled)
                    {
                        aimedDie.RequestNudgeFromLocalRay(aimedRay);
                    }
                    return;
                }

                if (_selectedItemSlot.Value >= 0)
                {
                    // Item activation remains edge-triggered so holding LMB cannot
                    // consume several inventory slots as replication catches up.
                    if (mouse.leftButton.wasPressedThisFrame)
                    {
                        var origin = eyePivot != null
                            ? eyePivot.position
                            : transform.position +
                              Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
                        var direction = eyePivot != null
                            ? eyePivot.forward
                            : transform.forward;
                        if (LocalEquippedItem == PrototypeItemId.PositionSwapper)
                            BoardUtilityItemView.Instance?.Open(this);
                        else if (TryBeginLocalItemUseRequest(
                                     LocalEquippedItem,
                                     out var requestId))
                        {
                            UseSelectedItemRpc(
                                origin,
                                direction,
                                requestId);
                        }
                    }
                }
                else
                {
                    var origin = eyePivot != null
                        ? eyePivot.position
                        : transform.position + Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
                    var direction = eyePivot != null ? eyePivot.forward : transform.forward;
                    RequestBoardPunchRpc(origin, direction);
                }
            }
        }

        private bool ShouldRepeatPrimaryAction(Mouse mouse, double intervalSeconds)
        {
            if (mouse == null || !mouse.leftButton.isPressed)
            {
                _nextLocalPrimaryRepeatAt = 0d;
                return false;
            }

            var now = Time.unscaledTimeAsDouble;
            if (!mouse.leftButton.wasPressedThisFrame &&
                now < _nextLocalPrimaryRepeatAt)
            {
                return false;
            }

            _nextLocalPrimaryRepeatAt =
                now + Math.Max(0.01d, intervalSeconds);
            return true;
        }

        private void SubmitLocalMovement()
        {
            var input = Vector2.zero;
            var quietWalkHeld = false;
            var keyboard = LocalKeyboard;
            var match = NetworkMatchState.Instance;
            var lobbyInput = CanUseLobbyInput();
            var canMove = lobbyInput || (match != null &&
                          ((match.CanAcceptActionInput && HasResolvedItemChoice &&
                            CurrentHealth > 0 &&
                            !BoardFlowView.IsItemShopOpen && !BoardUtilityItemView.IsTargetPickerOpen && !IsSwapping) ||
                           match.CanAvatarUseCombatInput(this)));
            if (keyboard != null && canMove)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
                quietWalkHeld = !lobbyInput && keyboard.leftCtrlKey.isPressed;
            }

            if (lobbyInput && input.sqrMagnitude > 0.0001f)
            {
                _localYaw = Mathf.Atan2(input.x, input.y) * Mathf.Rad2Deg;
                _localPitch = 0f;
                ApplyLocalEyeRotation();
            }

            var predictedCrouch = quietWalkHeld || _isCrouching.Value;
            _avatarVisual?.SetCrouching(predictedCrouch);
            if (eyePivot != null)
            {
                var eye = eyePivot.localPosition;
                eye.y = predictedCrouch
                    ? PlayerAvatarVisual.CrouchingEyeHeight
                    : PlayerAvatarVisual.StandingEyeHeight;
                eyePivot.localPosition = eye;
            }

            if (input != _lastSentInput ||
                quietWalkHeld != _lastSentQuietWalkHeld ||
                Mathf.Abs(Mathf.DeltaAngle(_lastSentYaw, _localYaw)) >= 1f ||
                Mathf.Abs(_lastSentPitch - _localPitch) >= 1f ||
                Time.unscaledTime >= _nextInputRefresh)
            {
                _lastSentInput = input;
                _lastSentQuietWalkHeld = quietWalkHeld;
                _lastSentYaw = _localYaw;
                _lastSentPitch = _localPitch;
                _nextInputRefresh = Time.unscaledTime + 0.1f;
                SubmitMovementRpc(input, _localYaw, _localPitch, quietWalkHeld);
            }
        }

        private bool SubmitLocalMinefieldMovement()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsMinefieldPlaying)
            {
                _lastSentMinefieldInput = Vector2.zero;
                return false;
            }

            var input = Vector2.zero;
            var keyboard = LocalKeyboard;
            if (keyboard != null &&
                match.CanCurrentMinigameAcceptInputForSlot(AssignedSlot))
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentMinefieldInput ||
                Time.unscaledTime >= _nextMinefieldInputRefresh)
            {
                _lastSentMinefieldInput = input;
                _nextMinefieldInputRefresh = Time.unscaledTime + 0.1f;
                SubmitMinefieldInputRpc(input);
            }

            return true;
        }

        private bool SubmitLocalRedLightGreenLightMovement()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsRedLightGreenLightPlaying)
            {
                _lastSentRedLightGreenLightInput = Vector2.zero;
                return false;
            }

            var input = Vector2.zero;
            var keyboard = LocalKeyboard;
            if (keyboard != null &&
                match.CanCurrentMinigameAcceptInputForSlot(AssignedSlot))
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentRedLightGreenLightInput ||
                Time.unscaledTime >=
                _nextRedLightGreenLightInputRefresh)
            {
                _lastSentRedLightGreenLightInput = input;
                _nextRedLightGreenLightInputRefresh =
                    Time.unscaledTime + 0.1f;
                SubmitRedLightGreenLightInputRpc(input);
            }

            return true;
        }

        private bool SubmitLocalStableFootingMovement()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsStableFootingPlaying)
            {
                _lastSentStableFootingInput = Vector2.zero;
                return false;
            }

            var input = Vector2.zero;
            var canAccept = match.CanCurrentMinigameAcceptInputForSlot(AssignedSlot);
            var keyboard = LocalKeyboard;
            if (canAccept && keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentStableFootingInput ||
                Time.unscaledTime >= _nextStableFootingInputRefresh)
            {
                _lastSentStableFootingInput = input;
                _nextStableFootingInputRefresh = Time.unscaledTime + 0.1f;
                SubmitStableFootingInputRpc(input);
            }

            var mouse = LocalMouse;
            if (canAccept && mouse != null &&
                mouse.leftButton.wasPressedThisFrame)
            {
                RequestStableFootingPushRpc();
            }

            return true;
        }

        private bool SubmitLocalBalloonBlowInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsBalloonBlowPlaying)
            {
                _lastSentBalloonBlowHeld = false;
                _lastSentBalloonBlowRound = -1;
                _lastSentBalloonBlowInputEpoch = 0U;
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) || inputEpoch == 0U)
            {
                return true;
            }

            var mouse = LocalMouse;
            var isHeld = mouse != null &&
                         mouse.leftButton.isPressed &&
                         !IsPointerOverUi();
            if (isHeld != _lastSentBalloonBlowHeld ||
                roundNumber != _lastSentBalloonBlowRound ||
                inputEpoch != _lastSentBalloonBlowInputEpoch ||
                Time.unscaledTime >= _nextBalloonBlowInputRefresh)
            {
                _lastSentBalloonBlowHeld = isHeld;
                _lastSentBalloonBlowRound = roundNumber;
                _lastSentBalloonBlowInputEpoch = inputEpoch;
                _nextBalloonBlowInputRefresh =
                    Time.unscaledTime + 0.1f;
                SubmitBalloonBlowHeldRpc(
                    isHeld,
                    roundNumber,
                    inputEpoch);
            }

            return true;
        }

        private bool SubmitLocalGiftGrabInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsGiftGrabPlaying)
            {
                _lastSentGiftGrabInput = Vector2.zero;
                _lastSentGiftGrabRound = -1;
                _lastSentGiftGrabInputEpoch = 0U;
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) || inputEpoch == 0U)
            {
                return true;
            }

            var input = Vector2.zero;
            var canAccept = match.CanCurrentMinigameAcceptInputForSlot(AssignedSlot);
            var keyboard = LocalKeyboard;
            if (canAccept && keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentGiftGrabInput ||
                roundNumber != _lastSentGiftGrabRound ||
                inputEpoch != _lastSentGiftGrabInputEpoch ||
                Time.unscaledTime >= _nextGiftGrabInputRefresh)
            {
                _lastSentGiftGrabInput = input;
                _lastSentGiftGrabRound = roundNumber;
                _lastSentGiftGrabInputEpoch = inputEpoch;
                _nextGiftGrabInputRefresh = Time.unscaledTime + 0.1f;
                SubmitGiftGrabInputRpc(
                    input,
                    roundNumber,
                    inputEpoch);
            }

            var mouse = LocalMouse;
            if (canAccept && mouse != null &&
                mouse.leftButton.wasPressedThisFrame)
            {
                RequestGiftGrabActionRpc(
                    roundNumber,
                    inputEpoch);
            }

            return true;
        }

        private bool SubmitLocalTerritoryPaintInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsTerritoryPaintPlaying)
            {
                _lastSentTerritoryPaintInput = Vector2.zero;
                _lastSentTerritoryPaintRound = -1;
                _lastSentTerritoryPaintInputEpoch = 0U;
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) || inputEpoch == 0U)
            {
                return true;
            }

            var input = Vector2.zero;
            var keyboard = LocalKeyboard;
            if (keyboard != null &&
                match.CanCurrentMinigameAcceptInputForSlot(
                    AssignedSlot))
            {
                input.x =
                    (keyboard.dKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed ? 1f : 0f);
                input.y =
                    (keyboard.wKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentTerritoryPaintInput ||
                roundNumber != _lastSentTerritoryPaintRound ||
                inputEpoch != _lastSentTerritoryPaintInputEpoch ||
                Time.unscaledTime >=
                _nextTerritoryPaintInputRefresh)
            {
                _lastSentTerritoryPaintInput = input;
                _lastSentTerritoryPaintRound = roundNumber;
                _lastSentTerritoryPaintInputEpoch = inputEpoch;
                _nextTerritoryPaintInputRefresh =
                    Time.unscaledTime + 0.1f;
                SubmitTerritoryPaintInputRpc(
                    input,
                    roundNumber,
                    inputEpoch);
            }

            return true;
        }

        private bool SubmitLocalSequenceMemoryInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSequenceMemoryPlaying)
            {
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) ||
                inputEpoch == 0U ||
                !match.CanCurrentMinigameAcceptInputForSlot(AssignedSlot))
            {
                return true;
            }

            var keyboard = LocalKeyboard;
            if (keyboard == null)
            {
                return true;
            }

            var aPressed = keyboard.aKey.wasPressedThisFrame;
            var sPressed = keyboard.sKey.wasPressedThisFrame;
            var dPressed = keyboard.dKey.wasPressedThisFrame;
            var pressedCount =
                (aPressed ? 1 : 0) +
                (sPressed ? 1 : 0) +
                (dPressed ? 1 : 0);
            if (pressedCount != 1)
            {
                return true;
            }

            var input = aPressed
                ? SequenceMemoryInput.A
                : sPressed
                    ? SequenceMemoryInput.S
                    : SequenceMemoryInput.D;
            SubmitSequenceMemoryInputRpc(
                input,
                roundNumber,
                inputEpoch);
            return true;
        }

        private bool SubmitLocalCliffBarrageInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsCliffBarragePlaying)
            {
                _lastSentCliffBarrageInput = Vector2.zero;
                _lastSentCliffBarrageRound = -1;
                _lastSentCliffBarrageInputEpoch = 0U;
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) || inputEpoch == 0U)
            {
                return true;
            }

            var input = Vector2.zero;
            var canAccept = match.CanCurrentMinigameAcceptInputForSlot(
                AssignedSlot);
            var keyboard = LocalKeyboard;
            if (canAccept && keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentCliffBarrageInput ||
                roundNumber != _lastSentCliffBarrageRound ||
                inputEpoch != _lastSentCliffBarrageInputEpoch ||
                Time.unscaledTime >= _nextCliffBarrageInputRefresh)
            {
                _lastSentCliffBarrageInput = input;
                _lastSentCliffBarrageRound = roundNumber;
                _lastSentCliffBarrageInputEpoch = inputEpoch;
                _nextCliffBarrageInputRefresh = Time.unscaledTime + 0.1f;
                SubmitCliffBarrageInputRpc(
                    input, roundNumber, inputEpoch);
            }

            var mouse = LocalMouse;
            if (canAccept && mouse != null &&
                mouse.leftButton.wasPressedThisFrame)
            {
                RequestCliffBarragePushRpc(roundNumber, inputEpoch);
            }

            return true;
        }

        private bool SubmitLocalSnowySpinInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsSnowySpinPlaying)
            {
                _lastSentSnowySpinInput = Vector2.zero;
                _lastSentSnowySpinRound = -1;
                _lastSentSnowySpinInputEpoch = 0U;
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) || inputEpoch == 0U)
            {
                return true;
            }

            var input = Vector2.zero;
            var canAccept = match.CanCurrentMinigameAcceptInputForSlot(
                AssignedSlot);
            var keyboard = LocalKeyboard;
            if (canAccept && keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentSnowySpinInput ||
                roundNumber != _lastSentSnowySpinRound ||
                inputEpoch != _lastSentSnowySpinInputEpoch ||
                Time.unscaledTime >= _nextSnowySpinInputRefresh)
            {
                _lastSentSnowySpinInput = input;
                _lastSentSnowySpinRound = roundNumber;
                _lastSentSnowySpinInputEpoch = inputEpoch;
                _nextSnowySpinInputRefresh = Time.unscaledTime + 0.1f;
                SubmitSnowySpinInputRpc(
                    input, roundNumber, inputEpoch);
            }

            return true;
        }

        private bool SubmitLocalBombPassingInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsBombPassingPlaying)
            {
                _lastSentBombPassingInput = Vector2.zero;
                _lastSentBombPassingRound = -1;
                _lastSentBombPassingInputEpoch = 0U;
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) || inputEpoch == 0U)
            {
                return true;
            }

            var input = Vector2.zero;
            var canAccept = match.CanCurrentMinigameAcceptInputForSlot(
                AssignedSlot);
            var keyboard = LocalKeyboard;
            if (canAccept && keyboard != null)
            {
                input.x = (keyboard.dKey.isPressed ? 1f : 0f) -
                          (keyboard.aKey.isPressed ? 1f : 0f);
                input.y = (keyboard.wKey.isPressed ? 1f : 0f) -
                          (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentBombPassingInput ||
                roundNumber != _lastSentBombPassingRound ||
                inputEpoch != _lastSentBombPassingInputEpoch ||
                Time.unscaledTime >= _nextBombPassingInputRefresh)
            {
                _lastSentBombPassingInput = input;
                _lastSentBombPassingRound = roundNumber;
                _lastSentBombPassingInputEpoch = inputEpoch;
                _nextBombPassingInputRefresh = Time.unscaledTime + 0.1f;
                SubmitBombPassingInputRpc(
                    input, roundNumber, inputEpoch);
            }

            var mouse = LocalMouse;
            if (canAccept && mouse != null &&
                mouse.leftButton.wasPressedThisFrame)
            {
                RequestBombPassingActionRpc(roundNumber, inputEpoch);
            }

            return true;
        }

        private bool SubmitLocalBouncingShieldInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsBouncingBallsPlaying)
            {
                _lastSentBouncingShieldAxis = 0f;
                _lastSentBouncingShieldRound = -1;
                _lastSentBouncingShieldInputEpoch = 0U;
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) ||
                inputEpoch == 0U ||
                !match.CanCurrentMinigameAcceptInputForSlot(AssignedSlot))
            {
                return true;
            }

            var keyboard = LocalKeyboard;
            var axis = keyboard == null
                ? 0f
                : (keyboard.dKey.isPressed ? 1f : 0f) -
                  (keyboard.aKey.isPressed ? 1f : 0f);
            if (axis != _lastSentBouncingShieldAxis ||
                roundNumber != _lastSentBouncingShieldRound ||
                inputEpoch != _lastSentBouncingShieldInputEpoch ||
                Time.unscaledTime >= _nextBouncingShieldInputRefresh)
            {
                _lastSentBouncingShieldAxis = axis;
                _lastSentBouncingShieldRound = roundNumber;
                _lastSentBouncingShieldInputEpoch = inputEpoch;
                _nextBouncingShieldInputRefresh =
                    Time.unscaledTime + 0.1f;
                SubmitBouncingShieldAxisRpc(
                    axis,
                    roundNumber,
                    inputEpoch);
            }

            return true;
        }

        private bool SubmitLocalRaceInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsRacePlaying)
            {
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) ||
                inputEpoch == 0U ||
                !match.CanCurrentMinigameAcceptInputForSlot(AssignedSlot))
            {
                return true;
            }

            var keyboard = LocalKeyboard;
            if (keyboard == null)
            {
                return true;
            }

            var leftPressed = keyboard.aKey.wasPressedThisFrame;
            var rightPressed = keyboard.dKey.wasPressedThisFrame;
            if (leftPressed == rightPressed)
            {
                return true;
            }

            SubmitRaceStepRpc(
                leftPressed ? RaceStepInput.Left : RaceStepInput.Right,
                roundNumber,
                inputEpoch);
            return true;
        }

        private bool SubmitLocalTagChaseInput()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsTagChasePlaying)
            {
                _lastSentTagChaseInput = Vector2.zero;
                _lastSentTagChaseRound = -1;
                _lastSentTagChaseInputEpoch = 0U;
                return false;
            }

            if (!match.TryGetCurrentMinigameRoundAndInputEpoch(
                    out var roundNumber,
                    out var inputEpoch) ||
                inputEpoch == 0U)
            {
                return true;
            }

            var input = Vector2.zero;
            var canAccept =
                match.CanCurrentMinigameAcceptInputForSlot(
                    AssignedSlot);
            var keyboard = LocalKeyboard;
            if (canAccept && keyboard != null)
            {
                input.x =
                    (keyboard.dKey.isPressed ? 1f : 0f) -
                    (keyboard.aKey.isPressed ? 1f : 0f);
                input.y =
                    (keyboard.wKey.isPressed ? 1f : 0f) -
                    (keyboard.sKey.isPressed ? 1f : 0f);
                input = Vector2.ClampMagnitude(input, 1f);
            }

            if (input != _lastSentTagChaseInput ||
                Mathf.Abs(Mathf.DeltaAngle(
                    _lastSentTagChaseYaw,
                    _localYaw)) >= 1f ||
                roundNumber != _lastSentTagChaseRound ||
                inputEpoch != _lastSentTagChaseInputEpoch ||
                Time.unscaledTime >=
                _nextTagChaseInputRefresh)
            {
                _lastSentTagChaseInput = input;
                _lastSentTagChaseYaw = _localYaw;
                _lastSentTagChaseRound = roundNumber;
                _lastSentTagChaseInputEpoch = inputEpoch;
                _nextTagChaseInputRefresh =
                    Time.unscaledTime + 0.1f;
                SubmitTagChaseInputRpc(
                    input,
                    _localYaw,
                    roundNumber,
                    inputEpoch);
            }

            var mouse = LocalMouse;
            if (canAccept &&
                match.CurrentMinigameUsesFirstPersonForSlot(
                    AssignedSlot) &&
                mouse != null &&
                mouse.leftButton.wasPressedThisFrame &&
                !IsPointerOverUi())
            {
                RequestTagChaseCatchRpc(
                    roundNumber,
                    inputEpoch);
            }

            return true;
        }


        private bool SubmitLocalWrongWayDirection()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsWrongWayPlaying)
            {
                return false;
            }

            var keyboard = LocalKeyboard;
            if (keyboard == null ||
                !match.CanCurrentMinigameAcceptInputForSlot(AssignedSlot))
            {
                return true;
            }

            WrongWayDirection? direction = null;
            if (keyboard.wKey.wasPressedThisFrame)
            {
                direction = WrongWayDirection.Up;
            }
            else if (keyboard.sKey.wasPressedThisFrame)
            {
                direction = WrongWayDirection.Down;
            }
            else if (keyboard.aKey.wasPressedThisFrame)
            {
                direction = WrongWayDirection.Left;
            }
            else if (keyboard.dKey.wasPressedThisFrame)
            {
                direction = WrongWayDirection.Right;
            }

            if (direction.HasValue)
            {
                SubmitWrongWayDirectionRpc((byte)direction.Value);
            }

            return true;
        }
    }
}
