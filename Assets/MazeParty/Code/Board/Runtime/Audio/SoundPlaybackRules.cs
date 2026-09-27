using System;
using System.Collections.Generic;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Per-cue clip selection state. Pure logic so the variation rules can be
    /// tested with a seeded random source.
    /// </summary>
    public sealed class SoundVariationPicker
    {
        private int _last = -1;
        private int[] _bag;
        private int _bagCursor;

        public int Last => _last;

        /// <summary>Index of the next clip, or -1 when there is none.</summary>
        public int Next(int count, SoundVariationMode mode, Random random)
        {
            if (count <= 0)
            {
                _last = -1;
                return -1;
            }

            if (_last >= count)
            {
                _last = -1;
            }

            if (count == 1)
            {
                _last = 0;
                return 0;
            }

            switch (mode)
            {
                case SoundVariationMode.Sequential:
                    _last = (_last + 1) % count;
                    return _last;
                case SoundVariationMode.Random:
                    _last = random.Next(count);
                    return _last;
                case SoundVariationMode.Shuffle:
                    return NextFromBag(count, random);
                default:
                    if (_last < 0)
                    {
                        _last = random.Next(count);
                        return _last;
                    }

                    var candidate = random.Next(count - 1);
                    _last = candidate >= _last ? candidate + 1 : candidate;
                    return _last;
            }
        }

        private int NextFromBag(int count, Random random)
        {
            if (_bag == null || _bag.Length != count || _bagCursor >= _bag.Length)
            {
                if (_bag == null || _bag.Length != count)
                {
                    _bag = new int[count];
                }

                for (var index = 0; index < count; index++)
                {
                    _bag[index] = index;
                }

                for (var index = count - 1; index > 0; index--)
                {
                    var swap = random.Next(index + 1);
                    (_bag[index], _bag[swap]) = (_bag[swap], _bag[index]);
                }

                // A new round never starts with the clip that ended the last one.
                if (_bag[0] == _last)
                {
                    var swap = 1 + random.Next(count - 1);
                    (_bag[0], _bag[swap]) = (_bag[swap], _bag[0]);
                }

                _bagCursor = 0;
            }

            _last = _bag[_bagCursor++];
            return _last;
        }
    }

    /// <summary>Snapshot of one pooled voice for <see cref="SoundVoiceRules"/>.</summary>
    public readonly struct SoundVoiceState
    {
        public SoundVoiceState(bool busy, int cueId, int priority, double startedAt, bool loop)
        {
            Busy = busy;
            CueId = cueId;
            Priority = priority;
            StartedAt = startedAt;
            Loop = loop;
        }

        public bool Busy { get; }
        public int CueId { get; }
        public int Priority { get; }
        public double StartedAt { get; }
        public bool Loop { get; }
    }

    /// <summary>Voice limiting for the pooled sound player.</summary>
    public static class SoundVoiceRules
    {
        private const double IntervalTolerance = 0.0001d;

        /// <summary>False when the same cue started less than minInterval ago.</summary>
        public static bool PassesInterval(double now, double lastStartedAt, float minInterval)
        {
            return minInterval <= 0f ||
                   lastStartedAt < 0d ||
                   now - lastStartedAt + IntervalTolerance >= minInterval;
        }

        /// <summary>
        /// Voice for a new sound, or -1 to skip it.
        /// 1. The cue is at its instance limit: replace its oldest instance.
        /// 2. Otherwise a free voice.
        /// 3. Otherwise steal the lowest-priority, then oldest, one-shot whose
        ///    priority does not exceed the new sound's. Loops are never stolen.
        /// </summary>
        public static int ChooseVoice(
            IReadOnlyList<SoundVoiceState> voices,
            int cueId,
            int maxInstances,
            int priority)
        {
            var sameCueCount = 0;
            var oldestSameCue = -1;
            var free = -1;
            var steal = -1;
            for (var index = 0; index < voices.Count; index++)
            {
                var voice = voices[index];
                if (!voice.Busy)
                {
                    if (free < 0)
                    {
                        free = index;
                    }

                    continue;
                }

                if (voice.CueId == cueId)
                {
                    sameCueCount++;
                    if (oldestSameCue < 0 ||
                        voice.StartedAt < voices[oldestSameCue].StartedAt)
                    {
                        oldestSameCue = index;
                    }
                }

                if (voice.Loop || voice.Priority > priority)
                {
                    continue;
                }

                if (steal < 0 ||
                    voice.Priority < voices[steal].Priority ||
                    (voice.Priority == voices[steal].Priority &&
                     voice.StartedAt < voices[steal].StartedAt))
                {
                    steal = index;
                }
            }

            if (maxInstances > 0 && sameCueCount >= maxInstances)
            {
                return oldestSameCue;
            }

            return free >= 0 ? free : steal;
        }
    }
}
