using System;
using System.Diagnostics;
using System.Text;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    /// <summary>
    /// Numeric event identifiers for host-authoritative decisions. Records do
    /// not accept strings so display names, account IDs, join codes and other
    /// sensitive session data cannot accidentally enter the trace.
    /// </summary>
    public enum ServerEventCode : ushort
    {
        None = 0,
        SessionTransition = 1,
        MatchTransition = 2,
        PermissionRejected = 3,
        PauseChanged = 4,
        ReconnectChanged = 5,
        MinigameTransition = 6,
        PersistenceRead = 7,
        PersistenceWrite = 8,
        PersistenceRejected = 9
    }

    public readonly struct ServerEventRecord
    {
        internal ServerEventRecord(
            long sequence,
            double recordedAt,
            ServerEventCode code,
            int turn,
            int state,
            int slot,
            long value0,
            long value1)
        {
            Sequence = sequence;
            RecordedAt = recordedAt;
            Code = code;
            Turn = turn;
            State = state;
            Slot = slot;
            Value0 = value0;
            Value1 = value1;
        }

        public long Sequence { get; }
        public double RecordedAt { get; }
        public ServerEventCode Code { get; }
        public int Turn { get; }
        public int State { get; }
        public int Slot { get; }
        public long Value0 { get; }
        public long Value1 { get; }
    }

    /// <summary>
    /// Fixed-size, allocation-free-on-write trace for development hosts. Calls
    /// to Record and DumpToLog are removed from non-development player builds.
    /// </summary>
    public static class ServerEventTrace
    {
        public const int Capacity = 512;

#if UNITY_EDITOR || UNITY_INCLUDE_INSTRUMENTATION
        private static readonly object Gate = new object();
        private static readonly ServerEventRecord[] Records =
            new ServerEventRecord[Capacity];
        private static int _count;
        private static int _nextIndex;
        private static long _nextSequence = 1;
#endif

        public static bool IsEnabled
        {
            get
            {
#if UNITY_EDITOR || UNITY_INCLUDE_INSTRUMENTATION
                return true;
#else
                return false;
#endif
            }
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("UNITY_INCLUDE_INSTRUMENTATION")]
        public static void Record(
            ServerEventCode code,
            int turn = -1,
            int state = -1,
            int slot = -1,
            long value0 = 0,
            long value1 = 0)
        {
#if UNITY_EDITOR || UNITY_INCLUDE_INSTRUMENTATION
            lock (Gate)
            {
                Records[_nextIndex] = new ServerEventRecord(
                    _nextSequence++,
                    Time.realtimeSinceStartupAsDouble,
                    code,
                    turn,
                    state,
                    slot,
                    value0,
                    value1);
                _nextIndex = (_nextIndex + 1) % Capacity;
                if (_count < Capacity)
                {
                    _count++;
                }
            }
#endif
        }

        public static ServerEventRecord[] Snapshot()
        {
#if UNITY_EDITOR || UNITY_INCLUDE_INSTRUMENTATION
            lock (Gate)
            {
                var result = new ServerEventRecord[_count];
                var first = _count == Capacity ? _nextIndex : 0;
                for (var index = 0; index < _count; index++)
                {
                    result[index] = Records[(first + index) % Capacity];
                }

                return result;
            }
#else
            return Array.Empty<ServerEventRecord>();
#endif
        }

        public static void Clear()
        {
#if UNITY_EDITOR || UNITY_INCLUDE_INSTRUMENTATION
            lock (Gate)
            {
                Array.Clear(Records, 0, Records.Length);
                _count = 0;
                _nextIndex = 0;
                _nextSequence = 1;
            }
#endif
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional("UNITY_INCLUDE_INSTRUMENTATION")]
        public static void DumpToLog()
        {
#if UNITY_EDITOR || UNITY_INCLUDE_INSTRUMENTATION
            var snapshot = Snapshot();
            var text = new StringBuilder(
                Math.Max(64, snapshot.Length * 64));
            text.AppendLine("[ServerTrace] Recent authoritative events:");
            for (var index = 0; index < snapshot.Length; index++)
            {
                var entry = snapshot[index];
                text.Append('#').Append(entry.Sequence)
                    .Append(" t=").Append(entry.RecordedAt.ToString("F3"))
                    .Append(" code=").Append((ushort)entry.Code)
                    .Append(" turn=").Append(entry.Turn)
                    .Append(" state=").Append(entry.State)
                    .Append(" slot=").Append(entry.Slot)
                    .Append(" a=").Append(entry.Value0)
                    .Append(" b=").Append(entry.Value1)
                    .AppendLine();
            }

            UnityEngine.Debug.Log(text.ToString());
#endif
        }
    }
}
