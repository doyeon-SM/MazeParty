#if UNITY_EDITOR || DEBUG
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Thin entry points for the standalone development-build E2E driver. These
    /// deliberately route through the same session operations as the lobby UI.
    /// </summary>
    public sealed partial class OnlineSessionController
    {
        public const string DevelopmentForestMapId = "forest-graybox";

        public async Task DevelopmentCreateSessionAsync(
            string displayName,
            PlayerAppearanceState appearance)
        {
            if (_destroyed || _sessions == null || IsInSession)
            {
                throw new InvalidOperationException(
                    "A development session cannot be created in the current state.");
            }

            SaveLocalProfile(displayName, appearance);
            await CreateAndPublishAsync(displayName);
        }

        public async Task DevelopmentJoinSessionAsync(
            string code,
            string displayName,
            PlayerAppearanceState appearance)
        {
            if (_destroyed || _sessions == null || IsInSession ||
                string.IsNullOrWhiteSpace(code))
            {
                throw new InvalidOperationException(
                    "A development session cannot be joined in the current state.");
            }

            SaveLocalProfile(displayName, appearance);
            await JoinAndPublishAsync(code, displayName);
        }

        public async Task DevelopmentSetReadyAsync(bool ready)
        {
            if (_destroyed || _sessions == null || !_sessions.IsInSession)
            {
                throw new InvalidOperationException(
                    "A development ready state requires an active session.");
            }

            if (_sessions.Current.LocalReady == ready)
            {
                return;
            }

            await _sessions.SetReadyAsync(ready);
        }

        public Task DevelopmentStartGameAsync()
        {
            if (_destroyed || _sessions == null || !_sessions.IsInSession ||
                !_sessions.Current.IsHost || !_sessions.Current.CanStart)
            {
                throw new InvalidOperationException(
                    "A development match requires a ready four-player host session.");
            }

            return StartGameAsync();
        }

        public bool DevelopmentHasFourAssignedPlayers()
        {
            var manager = _networkManager != null
                ? _networkManager
                : Unity.Netcode.NetworkManager.Singleton;
            return manager != null &&
                   HasExactlyFourAssignedNetworkPlayers(manager);
        }

        public bool DevelopmentRequestForestBoardMap()
        {
            if (_destroyed || _sessions == null || !_sessions.IsInSession)
            {
                return false;
            }

            var catalog = LoadBoardMapCatalog();
            if (catalog == null ||
                !catalog.TryGetMap(DevelopmentForestMapId, out var definition) ||
                definition == null || !definition.HasValidIdentity)
            {
                return false;
            }

            var selection = BoardMapSelection.FromDefinition(definition);
            if (_sessions.Current.BoardMapSelection == selection)
            {
                return true;
            }

            if (IsBusy || !CanChangeBoardMapSelection())
            {
                return false;
            }

            RunAsync(
                () => _sessions.SetBoardMapAsync(selection),
                GameText.N("Saving board map selection..."),
                SessionLifecycleState.Lobby);
            return true;
        }

    }

    public sealed partial class NetworkPlayerAvatar
    {
        /// <summary>
        /// Moves the authoritative avatar to a registered point on a board tile
        /// and faces it toward a world-space target. Intended only to arrange a
        /// deterministic item/combat encounter; it does not apply damage.
        /// </summary>
        public bool DevelopmentRelocateToTile(
            BoardTile tile,
            Vector3 worldOffset)
        {
            if (tile == null || !IsFiniteDevelopmentVector(worldOffset))
            {
                return false;
            }

            return DevelopmentRelocateFacingOnServer(
                tile,
                tile.GetRecoveryCenter(1f) + worldOffset,
                tile.GetRecoveryCenter(1f) + worldOffset + transform.forward);
        }

        internal bool DevelopmentRelocateFacingOnServer(
            BoardTile tile,
            Vector3 position,
            Vector3 lookAtPosition)
        {
            if (!IsServer || !IsSpawned || tile == null ||
                !IsFiniteDevelopmentVector(position) ||
                !IsFiniteDevelopmentVector(lookAtPosition) ||
                _characterController == null)
            {
                return false;
            }

            ResolveTopology();
            if (_topology == null ||
                !_topology.TryGetTile(tile.Coordinate, out var registeredTile) ||
                registeredTile != tile)
            {
                return false;
            }

            var footprintSupport =
                BoardGate.GetMaximumPlanarCapsuleSupport(
                    _characterController,
                    tile.transform.up) +
                ControllerFootprintPadding;
            if (!tile.ContainsHorizontalDisc(position, footprintSupport))
            {
                return false;
            }

            var forward = Vector3.ProjectOnPlane(
                lookAtPosition - position,
                Vector3.up);
            if (forward.sqrMagnitude < 0.0001f)
            {
                return false;
            }

            var rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            StopServerInputOnServer();
            _combatKnockbackVelocity = Vector3.zero;
            _traversal.Relocate(tile, _remainingMoves.Value);
            TeleportController(position, rotation);
            _serverYaw = rotation.eulerAngles.y;
            _serverPitch = 0f;
            if (eyePivot != null)
            {
                eyePivot.localRotation = Quaternion.identity;
            }

            SyncLogicalTileOnServer();
            RefreshBoundaryWallsOnServer();
            return true;
        }

        /// <summary>
        /// Uses the owner RPC after aligning the owner's aim with the requested
        /// target. A true result means that the request was submitted; the host
        /// still performs every ordinary gameplay validation.
        /// </summary>
        public bool DevelopmentPrepareItemShot(
            Vector3 targetWorldPosition,
            out Vector3 origin,
            out Vector3 direction)
        {
            return TryPrepareDevelopmentOwnerAim(
                targetWorldPosition,
                out origin,
                out direction);
        }

        /// <summary>
        /// Submits a normal selected-item use from this avatar's owner after a
        /// successful DevelopmentPrepareItemShot call.
        /// </summary>
        public bool DevelopmentRequestUseSelectedItem()
        {
            if (!TryGetDevelopmentOwnerAim(out var origin, out var direction))
            {
                return false;
            }

            UseSelectedItemRpc(origin, direction);
            return true;
        }

        public bool DevelopmentPrepareCombatPunch(
            Vector3 targetWorldPosition,
            out Vector3 origin,
            out Vector3 direction)
        {
            return TryPrepareDevelopmentOwnerAim(
                targetWorldPosition,
                out origin,
                out direction);
        }

        /// <summary>
        /// Requests a normal board-combat punch from this avatar's owner after
        /// a successful DevelopmentPrepareCombatPunch call.
        /// </summary>
        public bool DevelopmentRequestCombatPunch()
        {
            if (!TryGetDevelopmentOwnerAim(out var origin, out var direction))
            {
                return false;
            }

            RequestCombatPunchRpc(origin, direction);
            return true;
        }

        private bool TryPrepareDevelopmentOwnerAim(
            Vector3 targetWorldPosition,
            out Vector3 origin,
            out Vector3 direction)
        {
            origin = eyePivot != null
                ? eyePivot.position
                : transform.position +
                  Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            direction = targetWorldPosition - origin;
            if (!IsOwner || !IsSpawned ||
                !IsFiniteDevelopmentVector(targetWorldPosition) ||
                !IsFiniteDevelopmentVector(origin) ||
                !IsFiniteDevelopmentVector(direction) ||
                direction.sqrMagnitude < 0.0001f)
            {
                direction = Vector3.zero;
                return false;
            }

            direction.Normalize();
            _localYaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            _localPitch = -Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) *
                          Mathf.Rad2Deg;
            ApplyLocalEyeRotation();

            // Keep the server-authoritative facing direction synchronized before
            // the following item/punch request is validated.
            SubmitMovementRpc(Vector2.zero, _localYaw, _localPitch, false);
            return true;
        }

        private bool TryGetDevelopmentOwnerAim(
            out Vector3 origin,
            out Vector3 direction)
        {
            origin = eyePivot != null
                ? eyePivot.position
                : transform.position +
                  Vector3.up * PlayerAvatarVisual.StandingEyeHeight;
            direction = eyePivot != null ? eyePivot.forward : transform.forward;
            return IsOwner && IsSpawned &&
                   IsFiniteDevelopmentVector(origin) &&
                   IsFiniteDevelopmentVector(direction) &&
                   direction.sqrMagnitude > 0.0001f;
        }

        private static bool IsFiniteDevelopmentVector(Vector3 value)
        {
            return float.IsFinite(value.x) &&
                   float.IsFinite(value.y) &&
                   float.IsFinite(value.z);
        }
    }

    public sealed partial class NetworkMatchState
    {
        /// <summary>
        /// Settles this avatar's remaining route using the same active key-shop
        /// avoidance input as the action-timeout path, then reports arrival.
        /// </summary>
        public bool DevelopmentSettleAndReportAllPlayers(
            bool coLocateSlotsZeroAndOne)
        {
            if (!IsServer || FlowState != BoardFlowState.Action)
            {
                return false;
            }

            var avatars = new NetworkPlayerAvatar[MultiplayerConstants.MaxPlayers];
            for (var slot = 0; slot < avatars.Length; slot++)
            {
                var avatar = GetAvatarForSlot(slot);
                if (avatar == null || !avatar.IsSpawned || !HasRolled(slot) ||
                    !CanProcessActionRequest(avatar))
                {
                    return false;
                }

                avatars[slot] = avatar;
            }

            BoardTile keyShopTile = null;
            if (_keyShopRuntime != null && _keyShopRuntime.IsActive &&
                _boardTopology != null)
            {
                _boardTopology.TryGetTile(
                    _keyShopRuntime.Location,
                    out keyShopTile);
            }

            for (var slot = 0; slot < avatars.Length; slot++)
            {
                if (!HasArrived(slot))
                {
                    avatars[slot].ForceSettleRemainingMovesOnServer(keyShopTile);
                }
            }

            if (coLocateSlotsZeroAndOne)
            {
                var tile = avatars[0].CurrentBoardTileOnServer;
                if (tile == null)
                {
                    return false;
                }

                var center = tile.GetRecoveryCenter(1f);
                var separation = tile.transform.right.normalized * 0.65f;
                if (!avatars[0].DevelopmentRelocateFacingOnServer(
                        tile,
                        center - separation,
                        center + separation) ||
                    !avatars[1].DevelopmentRelocateFacingOnServer(
                        tile,
                        center + separation,
                        center - separation))
                {
                    return false;
                }
            }

            for (var slot = 0; slot < avatars.Length; slot++)
            {
                if (!HasArrived(slot) &&
                    !TryReportPlayerArrivedOnServer(avatars[slot]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Reduces (never extends) the active combat deadline. If the simulation
        /// is paused, the held combat time is reduced instead.
        /// </summary>
        public bool DevelopmentExpireCurrentCombat()
        {
            if (!IsServer || !_combatActive.Value)
            {
                return false;
            }

            if (IsGlobalSimulationPaused)
            {
                if (_pausedCombatRemaining.Value <= 0d)
                {
                    return false;
                }

                // ResumeCombatAndPersonalProtectionOnServer restores only a
                // positive held duration. Keep one millisecond so the normal
                // resume/update path resolves the fight immediately.
                _pausedCombatRemaining.Value = 0.001d;
                return true;
            }

            if (_combatEndsAt.Value <= 0d)
            {
                return false;
            }

            _combatEndsAt.Value = ServerNow;
            return true;
        }

        /// <summary>
        /// Completes the active minigame through the ordinary validated reward
        /// settlement path. Ranks are indexed by authoritative player slot.
        /// </summary>
        public bool DevelopmentCompleteCurrentMinigame(
            IReadOnlyList<PlayerPlacement> placements)
        {
            return IsServer &&
                   TryCompleteMinigameOnServer(
                       CurrentMinigame,
                       placements);
        }
    }
}
#endif
