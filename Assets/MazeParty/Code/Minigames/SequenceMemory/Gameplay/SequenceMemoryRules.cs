using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace MazeParty.Gameplay.Minigames.SequenceMemory
{
    /// <summary>
    /// Shared constants for the single-match A/S/D sequence memory game.
    /// The ten problem turns do not award points of their own.
    /// </summary>
    public static class SequenceMemoryRules
    {
        private static readonly int[] ProblemLengths =
        {
            5, 5, 5,
            6, 6, 6,
            7, 7, 7,
            8
        };

        public const int PlayerCount = 4;
        public const int RoundCount = 10;
        public const int ProblemTurnCount = RoundCount;
        public const int MistakesToEliminate = 2;
        public const double CountdownSeconds = 3d;
        public const double ProblemSymbolIntervalSeconds = 0.7d;
        public const double ProblemFinalHoldSeconds = 0.5d;
        public const double InputWindowSeconds = 10d;
        public const double AnswerRevealSeconds = 2d;
        public const double ResultSeconds = 4d;

        public static bool IsValidPlayerSlot(int playerSlot)
        {
            return playerSlot >= 0 && playerSlot < PlayerCount;
        }

        public static bool IsValidInput(SequenceMemoryInput input)
        {
            return input >= SequenceMemoryInput.A &&
                input <= SequenceMemoryInput.D;
        }

        public static int GetProblemLength(int roundNumber)
        {
            ValidateRoundNumber(roundNumber);
            return ProblemLengths[roundNumber - 1];
        }

        public static double GetProblemPresentationSeconds(
            int roundNumber)
        {
            var length = GetProblemLength(roundNumber);
            return (length * ProblemSymbolIntervalSeconds) +
                ProblemFinalHoldSeconds;
        }

        internal static void ValidateRoundNumber(int roundNumber)
        {
            if (roundNumber < 1 || roundNumber > RoundCount)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(roundNumber),
                    roundNumber,
                    "Round number must be between 1 and 10.");
            }
        }

        internal static void ValidateInput(
            SequenceMemoryInput input,
            string parameterName)
        {
            if (!IsValidInput(input))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    input,
                    "Input must be A, S or D.");
            }
        }

        internal static void ValidateInputElapsedSeconds(
            double inputElapsedSeconds)
        {
            if (double.IsNaN(inputElapsedSeconds) ||
                double.IsInfinity(inputElapsedSeconds) ||
                inputElapsedSeconds < 0d)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputElapsedSeconds),
                    inputElapsedSeconds,
                    "Input elapsed time must be finite and non-negative.");
            }
        }
    }

    /// <summary>
    /// The three accepted inputs. Presentation maps A to the high tone, S to
    /// the middle tone and D to the low tone.
    /// </summary>
    public enum SequenceMemoryInput : byte
    {
        A,
        S,
        D
    }

    /// <summary>
    /// Immutable problem data safe to share with network and presentation
    /// adapters.
    /// </summary>
    public sealed class SequenceMemoryProblem
    {
        private readonly SequenceMemoryInput[] _inputs;
        private readonly ReadOnlyCollection<SequenceMemoryInput>
            _readOnlyInputs;

        public SequenceMemoryProblem(
            IReadOnlyList<SequenceMemoryInput> inputs)
        {
            if (inputs == null)
            {
                throw new ArgumentNullException(nameof(inputs));
            }

            if (inputs.Count < 1 ||
                inputs.Count >
                SequenceMemoryRules.GetProblemLength(
                    SequenceMemoryRules.RoundCount))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(inputs),
                    inputs.Count,
                    "A problem must contain between one and eight inputs.");
            }

            _inputs = new SequenceMemoryInput[inputs.Count];
            for (var index = 0; index < inputs.Count; index++)
            {
                var input = inputs[index];
                SequenceMemoryRules.ValidateInput(input, nameof(inputs));
                if (index >= 2 &&
                    input == _inputs[index - 1] &&
                    input == _inputs[index - 2])
                {
                    throw new ArgumentException(
                        "A problem cannot contain three identical " +
                        "consecutive inputs.",
                        nameof(inputs));
                }

                _inputs[index] = input;
            }

            _readOnlyInputs = Array.AsReadOnly(_inputs);
        }

        public int Length => _inputs.Length;
        public IReadOnlyList<SequenceMemoryInput> Inputs =>
            _readOnlyInputs;

        public SequenceMemoryInput this[int index] => _inputs[index];

        public override string ToString()
        {
            var characters = new char[_inputs.Length];
            for (var index = 0; index < _inputs.Length; index++)
            {
                characters[index] = ToCharacter(_inputs[index]);
            }

            return new string(characters);
        }

        private static char ToCharacter(SequenceMemoryInput input)
        {
            switch (input)
            {
                case SequenceMemoryInput.A:
                    return 'A';
                case SequenceMemoryInput.S:
                    return 'S';
                case SequenceMemoryInput.D:
                    return 'D';
                default:
                    throw new ArgumentOutOfRangeException(nameof(input));
            }
        }
    }

    /// <summary>
    /// Generates all ten canonical problems from one authoritative match seed.
    /// SplitMix64 keeps results independent of System.Random implementations.
    /// </summary>
    public static class SequenceMemoryProblemGenerator
    {
        private const int MaximumDuplicateRetries = 1024;
        private const ulong SeedSalt = 0xD6E8FEB86659FD93UL;

        public static SequenceMemoryProblem[] GenerateMatch(
            ulong serverSeed)
        {
            var random = new SplitMix64(serverSeed ^ SeedSalt);
            var problems =
                new SequenceMemoryProblem[SequenceMemoryRules.RoundCount];
            var signatures = new HashSet<string>();

            for (var roundIndex = 0;
                 roundIndex < problems.Length;
                 roundIndex++)
            {
                var length = SequenceMemoryRules.GetProblemLength(
                    roundIndex + 1);
                var accepted = false;
                for (var attempt = 0;
                     attempt < MaximumDuplicateRetries;
                     attempt++)
                {
                    var candidate = GenerateProblem(length, ref random);
                    if (!signatures.Add(candidate.ToString()))
                    {
                        continue;
                    }

                    problems[roundIndex] = candidate;
                    accepted = true;
                    break;
                }

                if (!accepted)
                {
                    throw new InvalidOperationException(
                        "Could not generate a unique sequence memory problem.");
                }
            }

            return problems;
        }

        private static SequenceMemoryProblem GenerateProblem(
            int length,
            ref SplitMix64 random)
        {
            var inputs = new SequenceMemoryInput[length];
            for (var index = 0; index < inputs.Length; index++)
            {
                if (index >= 2 && inputs[index - 1] == inputs[index - 2])
                {
                    var forbidden = (int)inputs[index - 1];
                    var choice = random.NextInt(2);
                    inputs[index] = (SequenceMemoryInput)(
                        choice >= forbidden ? choice + 1 : choice);
                }
                else
                {
                    inputs[index] =
                        (SequenceMemoryInput)random.NextInt(3);
                }
            }

            return new SequenceMemoryProblem(inputs);
        }

        private struct SplitMix64
        {
            private ulong _state;

            public SplitMix64(ulong seed)
            {
                _state = seed;
            }

            public ulong NextUInt64()
            {
                _state = unchecked(_state + 0x9E3779B97F4A7C15UL);
                var value = _state;
                value = unchecked(
                    (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
                value = unchecked(
                    (value ^ (value >> 27)) * 0x94D049BB133111EBUL);
                return value ^ (value >> 31);
            }

            public int NextInt(int exclusiveMaximum)
            {
                if (exclusiveMaximum <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(exclusiveMaximum));
                }

                var range = (ulong)exclusiveMaximum;
                var threshold = unchecked(0UL - range) % range;
                ulong value;
                do
                {
                    value = NextUInt64();
                }
                while (value < threshold);

                return (int)(value % range);
            }
        }
    }
}
