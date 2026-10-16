using System;
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
        public const double DefaultTransitionSeconds = 5d;
        public const double ShopRevealSeconds = 3d;
        public const double AcquisitionSeconds =
            BoardResourceTransferPresentationRules.DestinationDurationSeconds;

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
                acquisitionMotion = Mathf.Clamp01((float)(
                    Math.Max(0d, elapsed - BoardResourceTransferPresentationRules.CameraLeadSeconds) /
                    (AcquisitionSeconds - BoardResourceTransferPresentationRules.CameraLeadSeconds)));
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
