using System;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        private readonly NetworkVariable<double> _knockdownEndsAt = new(0d);
        private readonly NetworkVariable<double> _pausedKnockdownRemaining = new(0d);
        public double KnockdownRemaining => _pausedKnockdownRemaining.Value > 0d
            ? _pausedKnockdownRemaining.Value
            : Math.Max(0d, _knockdownEndsAt.Value -
                (NetworkMatchState.Instance != null ? NetworkMatchState.Instance.SynchronizedNow : Time.unscaledTimeAsDouble));
        public bool IsKnockedDown => KnockdownRemaining > 0d;

        private void BeginBoardKnockdownOnServer(int damage, bool alive)
        {
            var match = NetworkMatchState.Instance;
            if (!IsServer || !alive || damage < BoardDamageReactionRules.KnockdownDamage ||
                match == null || !match.GameplayEnabled || match.IsArenaCombatPlaying) return;
            _knockdownEndsAt.Value = match.SynchronizedNow + BoardDamageReactionRules.KnockdownSeconds;
            _pausedKnockdownRemaining.Value = 0d;
            CancelHandGestureOnServer();
            StopServerInputOnServer();
            _combatKnockbackVelocity = Vector3.zero;
        }

        private void ClearBoardKnockdownOnServer()
        {
            if (!IsServer) return;
            _knockdownEndsAt.Value = _pausedKnockdownRemaining.Value = 0d;
        }
        private void PauseBoardKnockdownOnServer(double now)
        {
            if (_pausedKnockdownRemaining.Value > 0d) return;
            _pausedKnockdownRemaining.Value = Math.Max(0d, _knockdownEndsAt.Value - now);
            _knockdownEndsAt.Value = 0d;
        }
        private void ResumeBoardKnockdownOnServer(double now)
        {
            _knockdownEndsAt.Value = _pausedKnockdownRemaining.Value > 0d
                ? now + _pausedKnockdownRemaining.Value : 0d;
            _pausedKnockdownRemaining.Value = 0d;
        }
    }
}
