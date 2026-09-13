using System;
using System.Collections.Generic;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    public static class MinigameRuntimeRegistry
    {
        private static readonly IMinigameRuntimeAdapter[] RegisteredAdapters =
        {
            new MinefieldRuntimeAdapter(),
            new WrongWayRuntimeAdapter(),
            new RedLightGreenLightRuntimeAdapter(),
            new StableFootingRuntimeAdapter(),
            new BalloonBlowRuntimeAdapter(),
            new GiftGrabRuntimeAdapter()
        };

        private static readonly IReadOnlyList<ScheduledMinigameId>
            RegisteredIdsValue = BuildRegisteredIds();

        public static IReadOnlyList<ScheduledMinigameId> RegisteredIds =>
            RegisteredIdsValue;

        internal static bool TryGet(
            ScheduledMinigameId minigameId,
            out IMinigameRuntimeAdapter adapter)
        {
            for (var index = 0; index < RegisteredAdapters.Length; index++)
            {
                var candidate = RegisteredAdapters[index];
                if (candidate != null && candidate.Id == minigameId)
                {
                    adapter = candidate;
                    return true;
                }
            }

            adapter = null;
            return false;
        }

        internal static void PauseAll(double now)
        {
            ForEach(adapter => adapter.PauseOnServer(now));
        }

        internal static void ResumeAll(double now)
        {
            ForEach(adapter => adapter.ResumeOnServer(now));
        }

        internal static void RestoreAll(NetworkPlayerAvatar avatar)
        {
            ForEach(
                adapter =>
                    adapter.RestoreAvatarForReconnectOnServer(avatar));
        }

        internal static void EndAll()
        {
            ForEach(adapter => adapter.EndMatchOnServer());
        }

        private static IReadOnlyList<ScheduledMinigameId> BuildRegisteredIds()
        {
            var ids = new ScheduledMinigameId[RegisteredAdapters.Length];
            for (var index = 0; index < RegisteredAdapters.Length; index++)
            {
                ids[index] = RegisteredAdapters[index].Id;
            }

            return Array.AsReadOnly(ids);
        }

        private static void ForEach(Action<IMinigameRuntimeAdapter> action)
        {
            for (var index = 0; index < RegisteredAdapters.Length; index++)
            {
                var adapter = RegisteredAdapters[index];
                if (adapter != null)
                {
                    action(adapter);
                }
            }
        }
    }
}
