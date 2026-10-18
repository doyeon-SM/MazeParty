using System;
using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public enum KeyPurchasePresentationPhase : byte
    {
        None,
        ToTopView,
        AcquireKey,
        RevealShop,
        ReturnToFirstPerson,
        Complete
    }

    public static class KeyPurchasePresentationRules
    {
        public const double DefaultTransitionSeconds = BoardCameraCloudTiming.Duration;
        public const double ShopRevealSeconds = 3d;
        public const double AcquisitionSeconds =
            BoardEffectShotTiming.Duration;

        public static double TotalDuration(double transitionSeconds) =>
            transitionSeconds * 2d + AcquisitionSeconds + ShopRevealSeconds;

        public static KeyPurchasePresentationPhase GetPhase(
            double elapsed, double transitionSeconds, out float acquisitionMotion)
        {
            acquisitionMotion = 0f;
            if (elapsed < transitionSeconds) return KeyPurchasePresentationPhase.ToTopView;
            elapsed -= transitionSeconds;
            if (elapsed < AcquisitionSeconds)
            {
                acquisitionMotion = BoardEffectShotTiming.MotionProgress(elapsed);
                return KeyPurchasePresentationPhase.AcquireKey;
            }
            elapsed -= AcquisitionSeconds;
            if (elapsed < ShopRevealSeconds) return KeyPurchasePresentationPhase.RevealShop;
            elapsed -= ShopRevealSeconds;
            return elapsed < transitionSeconds
                ? KeyPurchasePresentationPhase.ReturnToFirstPerson
                : KeyPurchasePresentationPhase.Complete;
        }
    }
}
