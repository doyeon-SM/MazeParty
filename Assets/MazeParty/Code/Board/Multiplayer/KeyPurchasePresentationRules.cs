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
        public const double ShopHoldSeconds = 1d;
        public const double ShopZoomOutSeconds = 1d;
        public const double ShopRevealSeconds = ShopHoldSeconds + ShopZoomOutSeconds;
        public const double AcquisitionSeconds =
            BoardEffectShotTiming.Duration;

        public static double RevealStartsAt(double transitionSeconds, bool hasBuyer = true) =>
            hasBuyer ? transitionSeconds + AcquisitionSeconds : 0d;

        public static double TotalDuration(double transitionSeconds, bool hasBuyer = true) =>
            RevealStartsAt(transitionSeconds, hasBuyer) + ShopRevealSeconds + transitionSeconds;

        public static float ZoomOutProgress(double revealElapsed) =>
            Mathf.Clamp01((float)((revealElapsed - ShopHoldSeconds) / ShopZoomOutSeconds));

        public static KeyPurchasePresentationPhase GetPhase(
            double elapsed, double transitionSeconds, out float acquisitionMotion, bool hasBuyer = true)
        {
            acquisitionMotion = 0f;
            if (hasBuyer)
            {
                if (elapsed < transitionSeconds) return KeyPurchasePresentationPhase.ToTopView;
                elapsed -= transitionSeconds;
                if (elapsed < AcquisitionSeconds)
                {
                    acquisitionMotion = BoardEffectShotTiming.MotionProgress(elapsed);
                    return KeyPurchasePresentationPhase.AcquireKey;
                }
                elapsed -= AcquisitionSeconds;
            }
            if (elapsed < ShopRevealSeconds) return KeyPurchasePresentationPhase.RevealShop;
            elapsed -= ShopRevealSeconds;
            return elapsed < transitionSeconds
                ? KeyPurchasePresentationPhase.ReturnToFirstPerson
                : KeyPurchasePresentationPhase.Complete;
        }
    }
}
