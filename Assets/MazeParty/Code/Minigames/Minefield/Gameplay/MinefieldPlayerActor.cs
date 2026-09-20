using System;
using UnityEngine;

namespace MazeParty.Gameplay.Minigames.Minefield
{
    public enum MinefieldHazardKind : byte
    {
        Mine,
        Crusher,
        RoundTimeout
    }

    public enum MinefieldEliminationCause : byte
    {
        None,
        SecondMineHit,
        Crusher,
        RoundTimeout
    }

    /// <summary>
    /// Netcode-neutral state payload for a spawned minigame avatar.
    /// </summary>
    public readonly struct MinefieldActorSnapshot
    {
        public MinefieldActorSnapshot(
            int revision,
            int playerSlot,
            int mineHitCount,
            MinefieldPlayerState state,
            MinefieldEliminationCause eliminationCause,
            bool hazardsEnabled)
        {
            Revision = Mathf.Max(0, revision);
            PlayerSlot = playerSlot;
            MineHitCount = Mathf.Clamp(
                mineHitCount,
                0,
                MinefieldRules.MineHitsToEliminate);
            State = state;
            EliminationCause = state == MinefieldPlayerState.Eliminated
                ? eliminationCause
                : MinefieldEliminationCause.None;
            HazardsEnabled = hazardsEnabled;
        }

        public int Revision { get; }
        public int PlayerSlot { get; }
        public int MineHitCount { get; }
        public MinefieldPlayerState State { get; }
        public MinefieldEliminationCause EliminationCause { get; }
        public bool HazardsEnabled { get; }
    }

    /// <summary>
    /// Runtime bridge between mine/crusher contacts and an authoritative round
    /// coordinator. Mutating methods are explicit authority hooks; contact
    /// detection itself only raises requests unless an offline hazard resolves it.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MinefieldPlayerActor : MonoBehaviour
    {
        [SerializeField] private int playerSlot;
        [SerializeField] private int stateRevision;
        [SerializeField, Range(0, MinefieldRules.MineHitsToEliminate)]
        private int mineHitCount;
        [SerializeField] private MinefieldPlayerState state;
        [SerializeField] private MinefieldEliminationCause eliminationCause;
        [SerializeField] private bool hazardsEnabled = true;

        public event Action<MinefieldPlayerActor, MinefieldHazardKind, GameObject>
            HazardContactRequested;
        public event Action<MinefieldPlayerActor, MinefieldActorSnapshot> StateChanged;
        public event Action<MinefieldPlayerActor, GameObject> Crippled;
        public event Action<MinefieldPlayerActor, MinefieldEliminationCause, GameObject>
            Eliminated;
        public event Action<MinefieldPlayerActor> Finished;

        public int PlayerSlot => playerSlot;
        public int StateRevision => stateRevision;
        public int MineHitCount => mineHitCount;
        public MinefieldPlayerState State => state;
        public MinefieldEliminationCause EliminationCause => eliminationCause;
        public bool HazardsEnabled => hazardsEnabled;
        public bool CanMove =>
            state == MinefieldPlayerState.Healthy ||
            state == MinefieldPlayerState.Crippled;
        public bool CanReceiveHazards => hazardsEnabled && CanMove;
        public bool ShouldHideTorso => mineHitCount > 0;

        public MinefieldActorSnapshot Snapshot => new MinefieldActorSnapshot(
            stateRevision,
            playerSlot,
            mineHitCount,
            state,
            eliminationCause,
            hazardsEnabled);

        private void OnValidate()
        {
            playerSlot = Mathf.Clamp(
                playerSlot,
                0,
                MinefieldRules.PlayerCount - 1);
            mineHitCount = Mathf.Clamp(
                mineHitCount,
                0,
                MinefieldRules.MineHitsToEliminate);
        }

        public void ConfigurePlayerSlot(int slot)
        {
            if (!MinefieldRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            playerSlot = slot;
        }

        public bool RequestHazardContact(MinefieldHazardKind kind, GameObject source)
        {
            if (!CanReceiveHazards)
            {
                return false;
            }

            HazardContactRequested?.Invoke(this, kind, source);
            return true;
        }

        public MinefieldHitResolution ApplyAuthoritativeMineHit(GameObject source)
        {
            var previousState = state;
            if (!CanReceiveHazards)
            {
                return new MinefieldHitResolution(
                    false,
                    previousState,
                    state,
                    mineHitCount);
            }

            mineHitCount = Mathf.Min(
                mineHitCount + 1,
                MinefieldRules.MineHitsToEliminate);
            state = mineHitCount >= MinefieldRules.MineHitsToEliminate
                ? MinefieldPlayerState.Eliminated
                : MinefieldPlayerState.Crippled;
            eliminationCause = state == MinefieldPlayerState.Eliminated
                ? MinefieldEliminationCause.SecondMineHit
                : MinefieldEliminationCause.None;
            if (state == MinefieldPlayerState.Eliminated)
            {
                hazardsEnabled = false;
            }

            stateRevision++;
            PublishStateTransition(previousState, source);
            return new MinefieldHitResolution(
                true,
                previousState,
                state,
                mineHitCount);
        }

        public bool ApplyAuthoritativeElimination(
            MinefieldEliminationCause cause,
            GameObject source)
        {
            if (!CanMove)
            {
                return false;
            }

            if (cause == MinefieldEliminationCause.None)
            {
                cause = MinefieldEliminationCause.RoundTimeout;
            }

            var previousState = state;
            state = MinefieldPlayerState.Eliminated;
            eliminationCause = cause;
            hazardsEnabled = false;
            stateRevision++;
            PublishStateTransition(previousState, source);
            return true;
        }

        public bool ApplyAuthoritativeFinish()
        {
            if (!CanMove)
            {
                return false;
            }

            var previousState = state;
            state = MinefieldPlayerState.Finished;
            eliminationCause = MinefieldEliminationCause.None;
            hazardsEnabled = false;
            stateRevision++;
            PublishStateTransition(previousState, null);
            return true;
        }

        public void ResetForRoundAuthoritatively(
            int slot,
            bool enableHazards = true)
        {
            if (!MinefieldRules.IsValidPlayerSlot(slot))
            {
                throw new ArgumentOutOfRangeException(nameof(slot));
            }

            var previousState = state;
            playerSlot = slot;
            mineHitCount = 0;
            state = MinefieldPlayerState.Healthy;
            eliminationCause = MinefieldEliminationCause.None;
            hazardsEnabled = enableHazards;
            stateRevision++;
            PublishStateTransition(previousState, null);
        }

        public bool SetHazardsEnabledAuthoritatively(bool enabled)
        {
            if (hazardsEnabled == enabled || !CanMove && enabled)
            {
                return false;
            }

            hazardsEnabled = enabled;
            stateRevision++;
            StateChanged?.Invoke(this, Snapshot);
            return true;
        }

        public bool ApplyAuthoritativeSnapshot(MinefieldActorSnapshot snapshot)
        {
            if (!MinefieldRules.IsValidPlayerSlot(snapshot.PlayerSlot) ||
                snapshot.Revision < stateRevision ||
                IsCurrent(snapshot))
            {
                return false;
            }

            var previousState = state;
            playerSlot = snapshot.PlayerSlot;
            stateRevision = snapshot.Revision;
            mineHitCount = snapshot.MineHitCount;
            state = snapshot.State;
            eliminationCause = snapshot.EliminationCause;
            hazardsEnabled = snapshot.HazardsEnabled;
            PublishStateTransition(previousState, null);
            return true;
        }

        public void PublishCurrentState()
        {
            StateChanged?.Invoke(this, Snapshot);
        }

        private bool IsCurrent(MinefieldActorSnapshot snapshot)
        {
            return snapshot.Revision == stateRevision &&
                   snapshot.PlayerSlot == playerSlot &&
                   snapshot.MineHitCount == mineHitCount &&
                   snapshot.State == state &&
                   snapshot.EliminationCause == eliminationCause &&
                   snapshot.HazardsEnabled == hazardsEnabled;
        }

        private void PublishStateTransition(
            MinefieldPlayerState previousState,
            GameObject source)
        {
            StateChanged?.Invoke(this, Snapshot);

            if (previousState != MinefieldPlayerState.Crippled &&
                state == MinefieldPlayerState.Crippled)
            {
                Crippled?.Invoke(this, source);
            }

            if (previousState != MinefieldPlayerState.Eliminated &&
                state == MinefieldPlayerState.Eliminated)
            {
                Eliminated?.Invoke(this, eliminationCause, source);
            }

            if (previousState != MinefieldPlayerState.Finished &&
                state == MinefieldPlayerState.Finished)
            {
                Finished?.Invoke(this);
            }
        }
    }
}
