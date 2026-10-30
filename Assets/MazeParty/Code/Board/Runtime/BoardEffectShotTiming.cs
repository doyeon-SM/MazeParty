using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>One player shot: settle, play, hold. Shared by server and clients.</summary>
    public static class BoardEffectShotTiming
    {
        public const double Lead = 0.5d;
        public const double Motion = 1.5d;
        public const double Tail = 0.5d;
        public const double Duration = Lead + Motion + Tail;

        public static bool IsEffectVisible(double elapsed) =>
            elapsed >= Lead && elapsed < Lead + Motion;

        public static float MotionProgress(double elapsed) =>
            Mathf.Clamp01((float)((elapsed - Lead) / Motion));
    }
}
