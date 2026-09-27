using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;

namespace MazeParty.Multiplayer
{
    public static class MatchRecoveryFingerprint
    {
        // Increment only when a board/item rule change makes a saved stable
        // checkpoint unsafe to replay. Visual and localization changes do not.
        public const int BoardRecoveryCompatibilityVersion = 1;

        public static string CreatePlayerKey(string authenticatedPlayerId)
        {
            if (string.IsNullOrWhiteSpace(authenticatedPlayerId))
            {
                return string.Empty;
            }

            return ComputeSha256Hex(authenticatedPlayerId.Trim());
        }

        /// <summary>
        /// Hashes only recovery-relevant board topology and the compatibility
        /// versions of minigames present in this match. Adding an unrelated new
        /// catalog entry therefore does not invalidate an existing snapshot.
        /// </summary>
        public static string CreateContentFingerprint(
            BoardTopology topology,
            HostMinigameSchedule schedule)
        {
            return CreateContentFingerprint(
                topology,
                schedule,
                MatchRecoverySnapshot.CurrentRecoveryVersion);
        }

        public static string CreateContentFingerprint(
            BoardTopology topology,
            HostMinigameSchedule schedule,
            int recoveryVersion)
        {
            if (topology == null)
            {
                throw new ArgumentNullException(nameof(topology));
            }
            if (schedule == null)
            {
                throw new ArgumentNullException(nameof(schedule));
            }
            if (recoveryVersion < 1 ||
                recoveryVersion > MatchRecoverySnapshot.CurrentRecoveryVersion)
            {
                throw new ArgumentOutOfRangeException(nameof(recoveryVersion));
            }

            var text = new StringBuilder(1024);
            text.Append("recovery=")
                .Append(recoveryVersion.ToString(
                    CultureInfo.InvariantCulture))
                .Append("|board=")
                .Append(BoardRecoveryCompatibilityVersion.ToString(
                    CultureInfo.InvariantCulture))
                .Append('|');

            var minigames = new SortedSet<byte>();
            for (var turn = 1; turn <= schedule.TurnCount; turn++)
            {
                var id = schedule.GetMinigameForTurn(turn);
                if (id != ScheduledMinigameId.Skip)
                {
                    minigames.Add((byte)id);
                }
            }

            foreach (var raw in minigames)
            {
                var id = (ScheduledMinigameId)raw;
                if (!MinigameCatalog.TryGetDefinition(id, out var definition))
                {
                    throw new InvalidOperationException(
                        "The saved schedule contains a deleted minigame id.");
                }

                text.Append("game=")
                    .Append(raw.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(definition.RecoveryCompatibilityVersion.ToString(
                        CultureInfo.InvariantCulture))
                    .Append('|');
            }

            var tiles = new List<BoardTile>();
            for (var index = 0; index < topology.Tiles.Count; index++)
            {
                if (topology.Tiles[index] != null)
                {
                    tiles.Add(topology.Tiles[index]);
                }
            }
            tiles.Sort((left, right) =>
            {
                var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
                return x != 0
                    ? x
                    : left.Coordinate.y.CompareTo(right.Coordinate.y);
            });
            for (var index = 0; index < tiles.Count; index++)
            {
                var tile = tiles[index];
                text.Append("tile=")
                    .Append(tile.Coordinate.x.ToString(CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(tile.Coordinate.y.ToString(CultureInfo.InvariantCulture))
                    .Append(':')
                    .Append(((int)tile.TileType).ToString(
                        CultureInfo.InvariantCulture))
                    .Append("@p=");
                AppendVector(text, tile.transform.position);
                text.Append("@r=");
                AppendQuaternion(text, tile.transform.rotation);
                text.Append("@s=");
                AppendVector(text, tile.transform.lossyScale);
                text
                    .Append('|');
            }

            for (var index = 0; index < topology.Gates.Count; index++)
            {
                var gate = topology.Gates[index];
                text.Append("gate=")
                    .Append(index.ToString(CultureInfo.InvariantCulture))
                    .Append(':');
                if (gate == null || gate.Source == null || gate.Destination == null)
                {
                    text.Append("invalid|");
                    continue;
                }

                text.Append(gate.Source.Coordinate.x.ToString(
                        CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(gate.Source.Coordinate.y.ToString(
                        CultureInfo.InvariantCulture))
                    .Append('>')
                    .Append(gate.Destination.Coordinate.x.ToString(
                        CultureInfo.InvariantCulture))
                    .Append(',')
                    .Append(gate.Destination.Coordinate.y.ToString(
                        CultureInfo.InvariantCulture))
                    .Append("@p=");
                AppendVector(text, gate.transform.position);
                text.Append("@r=");
                AppendQuaternion(text, gate.transform.rotation);
                text.Append("@w=")
                    .Append(gate.GateWidth.ToString(
                        "R",
                        CultureInfo.InvariantCulture))
                    .Append("@e=")
                    .Append(gate.CrossingEpsilon.ToString(
                        "R",
                        CultureInfo.InvariantCulture))
                    .Append('|');
            }

            return ComputeSha256Hex(text.ToString());
        }

        private static void AppendVector(
            StringBuilder text,
            UnityEngine.Vector3 value)
        {
            text.Append(value.x.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(value.y.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(value.z.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void AppendQuaternion(
            StringBuilder text,
            UnityEngine.Quaternion value)
        {
            text.Append(value.x.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(value.y.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(value.z.ToString("R", CultureInfo.InvariantCulture))
                .Append(',')
                .Append(value.w.ToString("R", CultureInfo.InvariantCulture));
        }

        private static string ComputeSha256Hex(string value)
        {
            byte[] hash;
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(value));
            }

            var text = new StringBuilder(hash.Length * 2);
            for (var index = 0; index < hash.Length; index++)
            {
                text.Append(hash[index].ToString(
                    "x2",
                    CultureInfo.InvariantCulture));
            }

            return text.ToString();
        }
    }
}
