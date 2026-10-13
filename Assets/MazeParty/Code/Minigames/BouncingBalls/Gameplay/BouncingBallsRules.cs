using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames.BouncingBalls
{
    /// <summary>
    /// Authoritative two-dimensional arena dimensions and match timing.
    /// Slots are clockwise from the bottom: bottom, right, top, left.
    /// </summary>
    public static class BouncingBallsRules
    {
        public const int PlayerCount = 4;
        public const int BallCount = 3;
        public const int RoundCount = 2;
        public const double RoundSeconds = 60d;
        public const double CountdownSeconds = 3d;
        public const double ResultSeconds = 4d;
        public const int SimulationHz = 60;
        public const double SimulationStepSeconds = 1d / SimulationHz;

        public const double ArenaHalfExtent = 8d;
        public const double GoalHalfWidth = 3.1d;
        public const double ShieldRailDistance = 7.35d;
        public const double ShieldHalfWidth = 1.1d;
        public const double ShieldMaximumOffset = GoalHalfWidth;
        public const double ShieldSpeed = 5.4d;
        public const double BallRadius = 0.24d;
        public const double BallSpeed = 7.5d;
        public const double RespawnVariationRadians = 0.35d;
        public const int NoOwnerSlot = -1;

        public static bool IsValidPlayerSlot(int slot)
        {
            return slot >= 0 && slot < PlayerCount;
        }

        public static bool IsValidBallId(int ballId)
        {
            return ballId >= 0 && ballId < BallCount;
        }

        public static int CalculateScoreAfterGoal(
            int currentScore,
            int scorerSlot,
            int defenderSlot)
        {
            if (currentScore < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(currentScore));
            }

            if (!IsValidPlayerSlot(defenderSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(defenderSlot));
            }

            if (!IsValidPlayerSlot(scorerSlot))
            {
                return currentScore;
            }

            return scorerSlot == defenderSlot
                ? Math.Max(0, currentScore - 1)
                : currentScore + 1;
        }

        public static bool TryResolveBallCollision(
            BouncingBallsBallSnapshot first,
            BouncingBallsBallSnapshot second,
            out BouncingBallsBallSnapshot resolvedFirst,
            out BouncingBallsBallSnapshot resolvedSecond)
        {
            var deltaX = second.X - first.X;
            var deltaY = second.Y - first.Y;
            var distanceSquared = (deltaX * deltaX) + (deltaY * deltaY);
            var minimumDistance = BallRadius * 2d;
            if (distanceSquared >= minimumDistance * minimumDistance)
            {
                resolvedFirst = first;
                resolvedSecond = second;
                return false;
            }

            double distance;
            double normalX;
            double normalY;
            if (distanceSquared > double.Epsilon)
            {
                distance = Math.Sqrt(distanceSquared);
                normalX = deltaX / distance;
                normalY = deltaY / distance;
            }
            else
            {
                distance = 0d;
                normalX = first.Id <= second.Id ? 1d : -1d;
                normalY = 0d;
            }

            var separation = (minimumDistance - distance) * 0.5d;
            var firstX = first.X - (normalX * separation);
            var firstY = first.Y - (normalY * separation);
            var secondX = second.X + (normalX * separation);
            var secondY = second.Y + (normalY * separation);
            var firstVelocityX = first.VelocityX;
            var firstVelocityY = first.VelocityY;
            var secondVelocityX = second.VelocityX;
            var secondVelocityY = second.VelocityY;

            var relativeNormalVelocity =
                ((secondVelocityX - firstVelocityX) * normalX) +
                ((secondVelocityY - firstVelocityY) * normalY);
            if (relativeNormalVelocity < 0d)
            {
                firstVelocityX += relativeNormalVelocity * normalX;
                firstVelocityY += relativeNormalVelocity * normalY;
                secondVelocityX -= relativeNormalVelocity * normalX;
                secondVelocityY -= relativeNormalVelocity * normalY;
            }

            resolvedFirst = new BouncingBallsBallSnapshot(
                first.Id,
                firstX,
                firstY,
                firstVelocityX,
                firstVelocityY,
                first.OwnerSlot);
            resolvedSecond = new BouncingBallsBallSnapshot(
                second.Id,
                secondX,
                secondY,
                secondVelocityX,
                secondVelocityY,
                second.OwnerSlot);
            return true;
        }

        /// <summary>
        /// Projects the local player's horizontal screen input onto that
        /// slot's authoritative shield rail. Slots two and three view the
        /// arena through a 180/270 degree camera rotation, so their screen
        /// right points toward the negative rail coordinate.
        /// </summary>
        public static int ProjectScreenAxisToShieldRail(
            int playerSlot,
            int screenAxis)
        {
            if (!IsValidPlayerSlot(playerSlot))
            {
                throw new ArgumentOutOfRangeException(nameof(playerSlot));
            }

            if (screenAxis < -1 || screenAxis > 1)
            {
                throw new ArgumentOutOfRangeException(nameof(screenAxis));
            }

            return playerSlot < 2 ? screenAxis : -screenAxis;
        }

        internal static void ValidateRoundElapsed(double seconds)
        {
            if (double.IsNaN(seconds) ||
                double.IsInfinity(seconds) ||
                seconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(seconds),
                    seconds,
                    "Active round time must be finite and non-negative.");
            }
        }
    }

    /// <summary>
    /// The match awards no per-round placement points. Cumulative goals
    /// decide rank, then fewer conceded goals and finally stable server slot.
    /// </summary>
    public static class BouncingBallsRanking
    {
        public static int[] BuildRanksBySlot(
            IReadOnlyList<int> scores,
            IReadOnlyList<int> conceded)
        {
            if (scores == null)
            {
                throw new ArgumentNullException(nameof(scores));
            }

            if (conceded == null)
            {
                throw new ArgumentNullException(nameof(conceded));
            }

            if (scores.Count != BouncingBallsRules.PlayerCount ||
                conceded.Count != BouncingBallsRules.PlayerCount)
            {
                throw new ArgumentException(
                    "Scores and conceded goals require four player slots.");
            }

            var slots = new int[BouncingBallsRules.PlayerCount];
            for (var slot = 0; slot < slots.Length; slot++)
            {
                if (scores[slot] < 0 || conceded[slot] < 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(scores),
                        "Goal counts cannot be negative.");
                }

                slots[slot] = slot;
            }

            Array.Sort(
                slots,
                (left, right) =>
                {
                    var scoreOrder = scores[right].CompareTo(scores[left]);
                    if (scoreOrder != 0)
                    {
                        return scoreOrder;
                    }

                    var concededOrder =
                        conceded[left].CompareTo(conceded[right]);
                    return concededOrder != 0
                        ? concededOrder
                        : left.CompareTo(right);
                });

            var ranks = new int[BouncingBallsRules.PlayerCount];
            for (var index = 0; index < slots.Length; index++)
            {
                ranks[slots[index]] = index + 1;
            }

            return ranks;
        }
    }
}
