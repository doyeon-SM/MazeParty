using System;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    // Small policies shared by network runtimes. Game phases, cooldowns, input
    // sequence ordering and the identity of the active avatar stay with each game.
    internal static class MinigameServerRules
    {
        public static uint NextInputEpoch(uint current) =>
            current == uint.MaxValue ? 1U : current + 1U;

        public static bool IsCurrentInputEpoch(uint supplied, uint current) =>
            supplied != 0U && supplied == current;

        public static bool TryResolveSpawnedServerSlot(NetworkBehaviour runtime,
            NetworkPlayerAvatar avatar, out int slot)
        {
            slot = avatar != null ? avatar.AssignedSlot : -1;
            return runtime.IsSpawned && runtime.IsServer && avatar != null &&
                   avatar.IsSpawned && slot >= 0 && slot < MultiplayerConstants.MaxPlayers;
        }

        public static double Remaining(double deadline, double pausedRemaining,
            bool inactive, bool paused, double now) => inactive ? 0d :
            Math.Max(0d, paused ? pausedRemaining : deadline - now);

        public static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
        public static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);
        public static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
    }
}
