using MazeParty.Gameplay;
using Unity.Netcode;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkMatchState
    {
        private readonly NetworkVariable<BoardCombatRetreatSnapshot> _combatRetreat = new();
        public BoardCombatRetreatSnapshot CombatRetreat => _combatRetreat.Value;
        public bool IsCombatRetreatActive => FlowState == BoardFlowState.CombatResolve && _combatRetreat.Value.Mask != 0;
        public double CombatRetreatElapsed => _combatRetreat.Value.Elapsed(SynchronizedNow);
        public bool IsCombatRetreatKnockedDown(int slot) => IsCombatRetreatActive &&
            _combatRetreat.Value.Contains(slot) && !_combatRetreat.Value.HasReached(SynchronizedNow, BoardCombatRetreatTiming.StandUpAt);

        private void AdvanceCombatRetreatOnServer(double now)
        {
            if (!IsServer || _combatRetreat.Value.Mask == 0 || IsGlobalSimulationPaused) return;
            var snapshot = _combatRetreat.Value;
            var elapsed = snapshot.Elapsed(now);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                if (snapshot.Contains(slot))
                    GetAvatarForSlot(slot)?.ApplyCombatRetreatPose(snapshot.GetPath(slot), elapsed);
            if (!snapshot.HasReached(now, BoardCombatRetreatTiming.Duration)) return;

            _combatRetreat.Value = default;
            BeginNextCombatOrFinishOnServer(now);
        }

        private void PauseCombatRetreatOnServer(double now)
        {
            var snapshot = _combatRetreat.Value;
            if (snapshot.Mask == 0 || snapshot.Paused) return;
            snapshot.Paused = true;
            snapshot.PausedAt = now;
            _combatRetreat.Value = snapshot;
        }

        private void ResumeCombatRetreatOnServer(double now)
        {
            var snapshot = _combatRetreat.Value;
            if (!snapshot.Paused) return;
            snapshot.StartedAt += System.Math.Max(0d, now - snapshot.PausedAt);
            snapshot.Paused = false;
            snapshot.PausedAt = 0d;
            _combatRetreat.Value = snapshot;
        }
    }
}
