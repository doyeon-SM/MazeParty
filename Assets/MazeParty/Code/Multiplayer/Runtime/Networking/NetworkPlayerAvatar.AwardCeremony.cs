using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private bool _ceremonyPositionInitialized;

        private bool CanUseCeremonyInput() => NetworkMatchState.Instance != null &&
            NetworkMatchState.Instance.CanAcceptCeremonyMovement(AssignedSlot);

        public void PrepareForAwardCeremonyOnServer(Vector3 position, Quaternion rotation)
        {
            var match = NetworkMatchState.Instance;
            if (!IsServer || match == null || !match.IsFinalRankingLocked ||
                match.GetAvatarForSlot(AssignedSlot) != this) return;
            StopServerInputOnServer();
            CancelHandGestureOnServer();
            _combatKnockbackVelocity = Vector3.zero;
            _combatState.Value = (byte)NetworkCombatState.None;
            _boardDeathEndsAt = _pausedBoardDeathRemaining = 0d;
            ClearBoardKnockdownOnServer();
            _currentHealth.Value = _maxHealth.Value;
            ApplyCombatColliderState(NetworkCombatState.None);
            HideBoundaryWalls();
            TeleportController(position, rotation);
            _serverYaw = rotation.eulerAngles.y;
            _serverPitch = 0f;
            _ceremonyPositionInitialized = true;
        }

        private bool SimulateAwardCeremonyOnServer()
        {
            var match = NetworkMatchState.Instance;
            if (match == null || !match.IsAwardCeremonyActive)
            {
                _ceremonyPositionInitialized = false;
                return false;
            }
            HideBoundaryWalls();
            var stage = AwardCeremonyStageBindings.Instance;
            if (!_ceremonyPositionInitialized || !CanUseCeremonyInput() ||
                ActiveHandGesture != 0 || stage == null || !stage.HasRequiredReferences)
            {
                StopServerInputOnServer();
                return true;
            }
            transform.rotation = Quaternion.Euler(0f, _serverYaw, 0f);
            var direction = stage.transform.TransformDirection(new Vector3(_serverInput.x, 0f, _serverInput.y));
            var previous = transform.position;
            MoveControllerWithGrounding(direction * moveSpeed, Time.fixedDeltaTime);
            var radius = _characterController.radius * Mathf.Max(
                Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) + 0.02f;
            var clamped = stage.ClampPosition(transform.position, radius);
            if ((clamped - transform.position).sqrMagnitude > 0.000001f)
                TeleportController(clamped, transform.rotation, false);
            RecordNetworkFootsteps(previous, transform.position, direction.sqrMagnitude > 0.0001f, false);
            return true;
        }
    }
}
