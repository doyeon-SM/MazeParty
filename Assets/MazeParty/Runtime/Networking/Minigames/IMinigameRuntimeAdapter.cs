using MazeParty.Gameplay.Minigames;
using MazeParty.Gameplay.Minigames.WrongWay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    internal interface IMinigameRuntimeAdapter
    {
        ScheduledMinigameId Id { get; }
        NetworkBehaviour Instance { get; }
        bool IsSpawned { get; }

        bool CanAcceptInputForSlot(int slot);
        bool TryGetRoundAndInputEpoch(
            out byte roundNumber,
            out uint inputEpoch);
        void BeginMatchOnServer(ulong matchSeed);
        void PauseOnServer(double now);
        void ResumeOnServer(double now);
        void RestoreAvatarForReconnectOnServer(NetworkPlayerAvatar avatar);
        void EndMatchOnServer();
        void ReceiveMovementInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            byte roundNumber,
            uint inputEpoch);
        void RequestPushOnServer(NetworkPlayerAvatar avatar);
        void SetInflateHeldOnServer(
            NetworkPlayerAvatar avatar,
            bool isHeld,
            byte roundNumber,
            uint inputEpoch);
        void RequestPrimaryActionOnServer(
            NetworkPlayerAvatar avatar,
            byte roundNumber,
            uint inputEpoch);
        void TrySonarOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input);
        void TrySubmitDirectionOnServer(
            NetworkPlayerAvatar avatar,
            WrongWayDirection direction);
    }

    internal abstract class MinigameRuntimeAdapter<TState> :
        IMinigameRuntimeAdapter
        where TState : NetworkBehaviour
    {
        public abstract ScheduledMinigameId Id { get; }

        protected abstract TState CurrentState { get; }

        public NetworkBehaviour Instance => CurrentState;

        public bool IsSpawned
        {
            get
            {
                var state = CurrentState;
                return state != null && state.IsSpawned;
            }
        }

        public virtual bool CanAcceptInputForSlot(int slot)
        {
            return false;
        }

        public virtual bool TryGetRoundAndInputEpoch(
            out byte roundNumber,
            out uint inputEpoch)
        {
            roundNumber = 0;
            inputEpoch = 0U;
            return false;
        }

        public virtual void BeginMatchOnServer(ulong matchSeed)
        {
        }

        public virtual void PauseOnServer(double now)
        {
        }

        public virtual void ResumeOnServer(double now)
        {
        }

        public virtual void RestoreAvatarForReconnectOnServer(
            NetworkPlayerAvatar avatar)
        {
        }

        public virtual void EndMatchOnServer()
        {
        }

        public virtual void ReceiveMovementInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            byte roundNumber,
            uint inputEpoch)
        {
        }

        public virtual void RequestPushOnServer(NetworkPlayerAvatar avatar)
        {
        }

        public virtual void SetInflateHeldOnServer(
            NetworkPlayerAvatar avatar,
            bool isHeld,
            byte roundNumber,
            uint inputEpoch)
        {
        }

        public virtual void RequestPrimaryActionOnServer(
            NetworkPlayerAvatar avatar,
            byte roundNumber,
            uint inputEpoch)
        {
        }

        public virtual void TrySonarOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input)
        {
        }

        public virtual void TrySubmitDirectionOnServer(
            NetworkPlayerAvatar avatar,
            WrongWayDirection direction)
        {
        }
    }
}
