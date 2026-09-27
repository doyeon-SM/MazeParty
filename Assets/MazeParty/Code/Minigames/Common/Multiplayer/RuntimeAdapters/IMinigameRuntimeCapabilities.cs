using MazeParty.Gameplay.Minigames.Race;
using MazeParty.Gameplay.Minigames.SequenceMemory;
using MazeParty.Gameplay.Minigames.WrongWay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    internal interface IMinigameReconnectCapability
    {
        void RestoreAvatarForReconnectOnServer(NetworkPlayerAvatar avatar);
    }

    internal interface IMinigameFirstPersonCapability
    {
        bool UsesFirstPersonControlsForSlot(int slot);
    }

    internal interface IMinigameRoundEpochCapability
    {
        bool TryGetRoundAndInputEpoch(
            out byte roundNumber,
            out uint inputEpoch);
    }

    internal interface IMinigameMovementInputCapability
    {
        void ReceiveMovementInputOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input,
            byte roundNumber,
            uint inputEpoch);
    }

    internal interface IMinigameLookInputCapability
    {
        void ReceiveLookInputOnServer(
            NetworkPlayerAvatar avatar,
            float yaw,
            byte roundNumber,
            uint inputEpoch);
    }

    internal interface IMinigamePushInputCapability
    {
        void RequestPushOnServer(NetworkPlayerAvatar avatar);
    }

    internal interface IMinigameInflateInputCapability
    {
        void SetInflateHeldOnServer(
            NetworkPlayerAvatar avatar,
            bool isHeld,
            byte roundNumber,
            uint inputEpoch);
    }

    internal interface IMinigamePrimaryActionCapability
    {
        void RequestPrimaryActionOnServer(
            NetworkPlayerAvatar avatar,
            byte roundNumber,
            uint inputEpoch);
    }

    internal interface IMinigameSonarInputCapability
    {
        void TrySonarOnServer(
            NetworkPlayerAvatar avatar,
            Vector2 input);
    }

    internal interface IWrongWayDirectionInputCapability
    {
        void TrySubmitDirectionOnServer(
            NetworkPlayerAvatar avatar,
            WrongWayDirection direction);
    }

    internal interface IRaceStepInputCapability
    {
        void TrySubmitRaceStepOnServer(
            NetworkPlayerAvatar avatar,
            RaceStepInput input,
            byte roundNumber,
            uint inputEpoch);
    }

    internal interface ISequenceMemoryInputCapability
    {
        void TrySubmitSequenceMemoryInputOnServer(
            NetworkPlayerAvatar avatar,
            SequenceMemoryInput input,
            byte roundNumber,
            uint inputEpoch);
    }

    internal interface IBouncingShieldInputCapability
    {
        void SetBouncingShieldAxisOnServer(
            NetworkPlayerAvatar avatar,
            float axis,
            byte roundNumber,
            uint inputEpoch);
    }
}
