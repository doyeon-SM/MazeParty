using MazeParty.Gameplay;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkList<BoardKillGoldSnapshot> _deathGoldTransfers =
            new NetworkList<BoardKillGoldSnapshot>(default,
                NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
        private readonly NetworkVariable<double> _deathGoldPausedAt = new NetworkVariable<double>();
        private int _nextDeathGoldId;

        public NetworkList<BoardKillGoldSnapshot> DeathGoldTransfers => _deathGoldTransfers;
        public double DeathGoldPresentationNow =>
            _deathGoldPausedAt.Value > 0d ? _deathGoldPausedAt.Value : SynchronizedNow;

        // Called only from the victim's one-time board HP death transition.
        // Temporary combat eliminations do not enter this path.
        public void GrantBoardDeathGoldOnServer(NetworkPlayerAvatar victim, NetworkPlayerAvatar killer)
        {
            if (!IsServer || !GameplayEnabled || victim == null || !victim.IsSpawned ||
                victim.CurrentHealth > 0 || victim.AssignedSlot < 0 ||
                victim.AssignedSlot >= MultiplayerConstants.MaxPlayers)
                return;

            var killerSlot = killer != null && killer != victim && killer.IsSpawned &&
                killer.AssignedSlot >= 0 && killer.AssignedSlot < MultiplayerConstants.MaxPlayers
                ? killer.AssignedSlot : -1;
            var before = victim.Gold;
            victim.ApplyGoldDeltaOnServer(-BoardDeathRules.GoldLoss(before, killerSlot >= 0));
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var reward = BoardDeathRules.GoldReward(before, victim.AssignedSlot, killerSlot, slot);
                var recipient = GetAvatarForSlot(slot);
                if (reward <= 0 || recipient == null || !recipient.IsSpawned) continue;
                var credited = recipient.ApplyGoldDeltaOnServer(reward);
                if (credited <= 0) continue;
                _deathGoldTransfers.Add(new BoardKillGoldSnapshot
                {
                    Id = ++_nextDeathGoldId,
                    RecipientSlot = slot,
                    Amount = credited,
                    Origin = victim.transform.position,
                    StartedAt = ServerNow
                });
            }
        }

        public bool HasDeathGoldInFlight(int slot = -1)
        {
            var now = DeathGoldPresentationNow;
            for (var index = 0; index < _deathGoldTransfers.Count; index++)
            {
                var transfer = _deathGoldTransfers[index];
                if ((slot < 0 || transfer.RecipientSlot == slot) &&
                    now < transfer.StartedAt + BoardDeathGoldPresentationRules.Duration(transfer.Amount))
                    return true;
            }
            return false;
        }

        private void AdvanceDeathGoldPresentationOnServer(double now)
        {
            // Retain completed events briefly so a delayed snapshot can still be consumed.
            for (var index = _deathGoldTransfers.Count - 1; index >= 0; index--)
            {
                var transfer = _deathGoldTransfers[index];
                if (now >= transfer.StartedAt +
                    BoardDeathGoldPresentationRules.Duration(transfer.Amount) + 2d)
                    _deathGoldTransfers.RemoveAt(index);
            }
        }

        private void PauseDeathGoldPresentationOnServer(double now)
        {
            if (_deathGoldPausedAt.Value <= 0d) _deathGoldPausedAt.Value = now;
        }

        private void ResumeDeathGoldPresentationOnServer(double now)
        {
            if (_deathGoldPausedAt.Value <= 0d) return;
            var paused = System.Math.Max(0d, now - _deathGoldPausedAt.Value);
            for (var index = 0; index < _deathGoldTransfers.Count; index++)
            {
                var transfer = _deathGoldTransfers[index];
                transfer.StartedAt += paused;
                _deathGoldTransfers[index] = transfer;
            }
            _deathGoldPausedAt.Value = 0d;
        }

        private void ResetDeathGoldPresentationOnServer()
        {
            _deathGoldTransfers.Clear();
            _deathGoldPausedAt.Value = 0d;
            // Keep event IDs monotonic for clients that remain in the same scene.
        }
    }
}
