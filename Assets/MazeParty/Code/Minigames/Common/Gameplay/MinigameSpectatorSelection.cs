using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay.Minigames
{
    public static class MinigameSpectatorSelection
    {
        public static int NextTarget(int currentTarget, IReadOnlyList<bool> watchable)
        {
            if (watchable == null) throw new ArgumentNullException(nameof(watchable));
            var current = currentTarget >= 0 && currentTarget < watchable.Count ? currentTarget : -1;
            for (var offset = 1; offset <= watchable.Count; offset++)
            {
                var slot = (current + offset) % watchable.Count;
                if (watchable[slot]) return slot;
            }
            return -1;
        }

        public static int ChooseTarget(
            int currentTarget,
            IReadOnlyList<bool> watchable,
            IReadOnlyList<float> progress)
        {
            if (watchable == null || progress == null ||
                watchable.Count != progress.Count)
            {
                throw new ArgumentException("Spectator standings are incomplete.");
            }

            if (currentTarget >= 0 && currentTarget < watchable.Count &&
                watchable[currentTarget])
            {
                return currentTarget;
            }

            var leader = -1;
            for (var slot = 0; slot < watchable.Count; slot++)
            {
                if (!watchable[slot])
                {
                    continue;
                }

                if (leader < 0 || progress[slot] > progress[leader])
                {
                    leader = slot;
                }
            }

            return leader;
        }
    }
}
