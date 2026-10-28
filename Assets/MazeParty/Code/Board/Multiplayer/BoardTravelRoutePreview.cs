using System;
using System.Collections.Generic;
using MazeParty.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    [Serializable]
    public struct BoardRouteChoice :
        INetworkSerializable, IEquatable<BoardRouteChoice>
    {
        public Vector2Int Source;
        public Vector2Int Destination;

        public BoardRouteChoice(Vector2Int source, Vector2Int destination)
        {
            Source = source;
            Destination = destination;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer)
            where T : IReaderWriter
        {
            serializer.SerializeValue(ref Source);
            serializer.SerializeValue(ref Destination);
        }

        public bool Equals(BoardRouteChoice other)
        {
            return Source == other.Source && Destination == other.Destination;
        }

        public override bool Equals(object obj)
        {
            return obj is BoardRouteChoice other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Source, Destination);
        }
    }

    public readonly struct BoardTravelRouteStep :
        IEquatable<BoardTravelRouteStep>
    {
        public BoardTravelRouteStep(
            Vector2Int coordinate,
            int step,
            bool isBranch)
        {
            Coordinate = coordinate;
            Step = step;
            IsBranch = isBranch;
        }

        public Vector2Int Coordinate { get; }
        public int Step { get; }
        public bool IsBranch { get; }

        public bool Equals(BoardTravelRouteStep other)
        {
            return Coordinate == other.Coordinate &&
                   Step == other.Step &&
                   IsBranch == other.IsBranch;
        }

        public override bool Equals(object obj)
        {
            return obj is BoardTravelRouteStep other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Coordinate, Step, IsBranch);
        }
    }

    /// <summary>
    /// Represents directed walks as shared (tile, step, matched-choice) states.
    /// Cycles and return trips remain valid without materializing exponentially
    /// many paths. Authored gate order still breaks ties for the preferred route.
    /// </summary>
    public sealed class BoardTravelRoutePreview
    {
        private readonly struct RouteState : IEquatable<RouteState>
        {
            public RouteState(BoardTile tile, int step, int choice)
            { Tile = tile; Step = step; Choice = choice; }
            public BoardTile Tile { get; }
            public int Step { get; }
            public int Choice { get; }
            public bool Equals(RouteState other) =>
                Tile == other.Tile && Step == other.Step && Choice == other.Choice;
            public override bool Equals(object obj) => obj is RouteState other && Equals(other);
            public override int GetHashCode() => HashCode.Combine(Tile, Step, Choice);
        }

        private readonly BoardTopology _topology;
        private readonly BoardTile _origin, _primaryDestination;
        private readonly BoardMapRoute.DestinationIndex _routesToDestination;
        private readonly Dictionary<BoardTile, BoardTile[]> _destinations =
            new Dictionary<BoardTile, BoardTile[]>();
        private readonly List<BoardTile> _primaryRoute = new List<BoardTile>();
        private readonly List<BoardTile> _choicePrimaryRoute = new List<BoardTile>();
        private readonly List<BoardTile> _preferredReturn = new List<BoardTile>();
        private readonly List<BoardTile> _selectedPrimaryRoute = new List<BoardTile>();
        private readonly List<BoardRouteChoice> _cachedChoices = new List<BoardRouteChoice>();
        private readonly Dictionary<RouteState, int> _counts = new Dictionary<RouteState, int>();
        private readonly Dictionary<RouteState, int> _preferenceScores = new Dictionary<RouteState, int>();
        private readonly List<RouteState> _reachable = new List<RouteState>();
        private readonly Dictionary<RouteState, bool> _branches = new Dictionary<RouteState, bool>();
        private readonly Dictionary<(Vector2Int Coordinate, int Step), bool> _visibleSteps =
            new Dictionary<(Vector2Int Coordinate, int Step), bool>();
        private readonly HashSet<BoardRouteChoice> _visibleEdges = new HashSet<BoardRouteChoice>();
        private bool _choiceCacheValid, _hasPrimaryRoute;
        private int _compatibleRouteCount;

        public BoardTravelRoutePreview(BoardTopology topology, BoardTile turnOrigin, int maximumStep)
            : this(topology, turnOrigin, 0, maximumStep, null) { }
        public BoardTravelRoutePreview(BoardTopology topology, BoardTile turnOrigin, int maximumStep,
            BoardTile primaryDestination)
            : this(topology, turnOrigin, 0, maximumStep, primaryDestination) { }
        public BoardTravelRoutePreview(BoardTopology topology, BoardTile turnOrigin, int startingStep,
            int maximumStep)
            : this(topology, turnOrigin, startingStep, maximumStep, null) { }
        public BoardTravelRoutePreview(BoardTopology topology, BoardTile turnOrigin, int startingStep,
            int maximumStep, BoardTile primaryDestination)
        {
            if (topology == null) throw new ArgumentNullException(nameof(topology));
            if (turnOrigin == null) throw new ArgumentNullException(nameof(turnOrigin));
            if (startingStep < 0) throw new ArgumentOutOfRangeException(nameof(startingStep));
            if (maximumStep < startingStep) throw new ArgumentOutOfRangeException(nameof(maximumStep));
            StartCoordinate = turnOrigin.Coordinate;
            StartingStep = startingStep;
            MaximumStep = maximumStep;
            _topology = topology;
            _origin = turnOrigin;
            _primaryDestination = primaryDestination;
            if (primaryDestination != null)
            {
                _routesToDestination = new BoardMapRoute.DestinationIndex(topology, primaryDestination);
                FindPreferredReturn();
                _routesToDestination.TryFind(turnOrigin, _primaryRoute);
                ExtendPreferredRoute(_primaryRoute);
            }
            EnsureChoiceCache(null);
            RouteCount = _compatibleRouteCount;
        }

        public Vector2Int StartCoordinate { get; }
        public int StartingStep { get; }
        public int MaximumStep { get; }
        // Counts saturate for huge walks; reachability and presentation remain exact.
        public int RouteCount { get; }
        private int RemainingSteps => MaximumStep - StartingStep;
        public static bool ShouldDisplayForTurn(bool isActionPhase, int turnRoll, bool hasValidOrigin) =>
            isActionPhase && turnRoll > 0 && hasValidOrigin;

        public int GetCompatibleRouteCount(IReadOnlyList<BoardRouteChoice> choices)
        { EnsureChoiceCache(choices); return _compatibleRouteCount; }

        public int GetVisibleSteps(IReadOnlyList<BoardRouteChoice> choices,
            List<BoardTravelRouteStep> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            EnsureChoiceCache(choices);
            _visibleSteps.Clear();
            foreach (var state in _reachable)
            {
                var key = (state.Tile.Coordinate, StartingStep + state.Step);
                var branch = _hasPrimaryRoute || _branches[state];
                if (_visibleSteps.TryGetValue(key, out var existing)) branch &= existing;
                _visibleSteps[key] = branch;
            }
            if (_hasPrimaryRoute)
                for (var step = 0; step < _selectedPrimaryRoute.Count; step++)
                    _visibleSteps[(_selectedPrimaryRoute[step].Coordinate, StartingStep + step)] = false;
            foreach (var pair in _visibleSteps)
                destination.Add(new BoardTravelRouteStep(pair.Key.Coordinate, pair.Key.Step, pair.Value));
            destination.Sort(CompareSteps);
            return destination.Count;
        }
        public IReadOnlyList<BoardTravelRouteStep> GetVisibleSteps(
            IReadOnlyList<BoardRouteChoice> choices = null)
        {
            var result = new List<BoardTravelRouteStep>();
            GetVisibleSteps(choices, result);
            return result;
        }

        public int GetVisibleEdges(IReadOnlyList<BoardRouteChoice> choices, int consumedStep,
            List<BoardRouteChoice> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            EnsureChoiceCache(choices);
            _visibleEdges.Clear();
            foreach (var state in _reachable)
            {
                if (state.Step >= RemainingSteps || StartingStep + state.Step < consumedStep) continue;
                foreach (var tile in GetDestinations(state.Tile))
                    if (TryNext(state, tile, out var next) && Count(next) > 0)
                    {
                        var edge = new BoardRouteChoice(state.Tile.Coordinate, tile.Coordinate);
                        if (_visibleEdges.Add(edge)) destination.Add(edge);
                    }
            }
            destination.Sort((a, b) =>
            {
                var value = a.Source.x.CompareTo(b.Source.x);
                if (value != 0) return value;
                value = a.Source.y.CompareTo(b.Source.y);
                if (value != 0) return value;
                value = a.Destination.x.CompareTo(b.Destination.x);
                return value != 0 ? value : a.Destination.y.CompareTo(b.Destination.y);
            });
            return destination.Count;
        }

        public int GetPreferredEdges(IReadOnlyList<BoardRouteChoice> choices, int consumedStep,
            List<BoardRouteChoice> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            EnsureChoiceCache(choices);
            if (!_hasPrimaryRoute) return 0;
            for (var edge = 0; edge + 1 < _selectedPrimaryRoute.Count; edge++)
                if (StartingStep + edge >= consumedStep)
                    destination.Add(new BoardRouteChoice(_selectedPrimaryRoute[edge].Coordinate,
                        _selectedPrimaryRoute[edge + 1].Coordinate));
            return destination.Count;
        }

        private void EnsureChoiceCache(IReadOnlyList<BoardRouteChoice> choices)
        {
            var count = choices != null ? choices.Count : 0;
            var unchanged = _choiceCacheValid && count == _cachedChoices.Count;
            for (var i = 0; unchanged && i < count; i++) unchanged = choices[i].Equals(_cachedChoices[i]);
            if (unchanged) return;
            _cachedChoices.Clear();
            for (var i = 0; i < count; i++) _cachedChoices.Add(choices[i]);
            _counts.Clear();
            _preferenceScores.Clear();
            _reachable.Clear();
            _branches.Clear();
            _selectedPrimaryRoute.Clear();
            _hasPrimaryRoute = false;
            var origin = new RouteState(_origin, 0, 0);
            _compatibleRouteCount = Count(origin);
            if (_compatibleRouteCount > 0)
            {
                BuildReachableStates(origin);
                BuildPreferredRoute(origin);
            }
            _choiceCacheValid = true;
        }

        private bool TryNext(RouteState current, BoardTile destination, out RouteState next)
        {
            var matched = current.Choice;
            if (matched < _cachedChoices.Count && current.Tile.Coordinate == _cachedChoices[matched].Source)
            {
                // The first encounter after the previous choice must use this exit.
                // Repeated visits consume repeated choices in traversal order.
                if (destination.Coordinate != _cachedChoices[matched].Destination)
                { next = default; return false; }
                matched++;
            }
            next = new RouteState(destination, current.Step + 1, matched);
            return true;
        }

        private int Count(RouteState state)
        {
            if (_counts.TryGetValue(state, out var cached)) return cached;
            var exits = GetDestinations(state.Tile);
            var total = 0;
            if (state.Step >= RemainingSteps || exits.Length == 0)
                total = state.Choice == _cachedChoices.Count ? 1 : 0;
            else
                foreach (var tile in exits)
                    if (TryNext(state, tile, out var next))
                        total = (int)Math.Min(int.MaxValue, (long)total + Count(next));
            _counts.Add(state, total);
            return total;
        }

        private void BuildReachableStates(RouteState origin)
        {
            _reachable.Add(origin);
            _branches.Add(origin, false);
            // Breadth first order ensures all incoming branch flags are merged
            // before a state's next layer is visited.
            for (var index = 0; index < _reachable.Count; index++)
            {
                var state = _reachable[index];
                if (state.Step >= RemainingSteps) continue;
                var exits = GetDestinations(state.Tile);
                var preferred = state.Step + 1 < _primaryRoute.Count && _primaryRoute[state.Step] == state.Tile
                    ? _primaryRoute[state.Step + 1] : null;
                for (var exit = 0; exit < exits.Length; exit++)
                {
                    if (!TryNext(state, exits[exit], out var next) || Count(next) == 0) continue;
                    var branch = _branches[state] || (preferred != null ? exits[exit] != preferred : exit > 0);
                    if (_branches.TryGetValue(next, out var existing)) _branches[next] = existing && branch;
                    else { _reachable.Add(next); _branches.Add(next, branch); }
                }
            }
        }

        private void BuildPreferredRoute(RouteState origin)
        {
            if (_primaryDestination == null) return;
            var preferred = _primaryRoute;
            if (_cachedChoices.Count > 0)
            {
                if (!_topology.TryGetTile(_cachedChoices[_cachedChoices.Count - 1].Destination, out var current) ||
                    !_routesToDestination.TryFind(current, _choicePrimaryRoute)) return;
                ExtendPreferredRoute(_choicePrimaryRoute);
                preferred = _choicePrimaryRoute;
            }
            _hasPrimaryRoute = true;
            var state = origin;
            var preferredIndex = 0;
            while (true)
            {
                _selectedPrimaryRoute.Add(state.Tile);
                if (state.Step >= RemainingSteps) break;
                var found = false;
                var selected = default(RouteState);
                var bestScore = -1;
                foreach (var tile in GetDestinations(state.Tile))
                {
                    if (!TryNext(state, tile, out var next) || Count(next) == 0) continue;
                    var score = state.Choice < _cachedChoices.Count
                        ? PreferenceScore(next, preferred)
                        : preferredIndex + 1 < preferred.Count && tile == preferred[preferredIndex + 1] ? 1 : 0;
                    if (score <= bestScore) continue;
                    found = true; selected = next; bestScore = score;
                }
                if (!found) break;
                if (state.Choice == _cachedChoices.Count)
                    preferredIndex = bestScore > 0 ? preferredIndex + 1 : preferred.Count;
                state = selected;
            }
        }

        // Maximize the preferred suffix after the final committed choice, then
        // choose the first authored walk on ties, matching the original preview.
        private int PreferenceScore(RouteState state, List<BoardTile> preferred)
        {
            if (_preferenceScores.TryGetValue(state, out var cached)) return cached;
            var score = 0;
            if (state.Choice == _cachedChoices.Count)
                score = preferred.Count > 0 && state.Tile == preferred[0]
                    ? Math.Min(preferred.Count, RemainingSteps - state.Step + 1) : 0;
            else if (state.Step < RemainingSteps)
                foreach (var tile in GetDestinations(state.Tile))
                    if (TryNext(state, tile, out var next) && Count(next) > 0)
                        score = Math.Max(score, PreferenceScore(next, preferred));
            _preferenceScores.Add(state, score);
            return score;
        }

        private void FindPreferredReturn()
        {
            var candidate = new List<BoardTile>();
            foreach (var next in GetDestinations(_primaryDestination))
            {
                if (!_routesToDestination.TryFind(next, candidate) || candidate.Count == 0) continue;
                if (_preferredReturn.Count > 0 && candidate.Count >= _preferredReturn.Count) continue;
                _preferredReturn.Clear(); _preferredReturn.AddRange(candidate);
            }
        }
        private void ExtendPreferredRoute(List<BoardTile> route)
        {
            if (route.Count == 0 || _preferredReturn.Count == 0) return;
            while (route.Count < RemainingSteps + 1 && route[route.Count - 1] == _primaryDestination)
                for (var i = 0; i < _preferredReturn.Count && route.Count < RemainingSteps + 1; i++)
                    route.Add(_preferredReturn[i]);
        }
        private BoardTile[] GetDestinations(BoardTile tile)
        {
            if (_destinations.TryGetValue(tile, out var cached)) return cached;
            var destinations = new List<BoardTile>();
            foreach (var gate in _topology.GetOutgoingGates(tile))
            {
                if (gate == null || gate.IsTemporarilyBlocked) continue;
                var next = gate.Destination;
                if (next != null && !destinations.Contains(next)) destinations.Add(next);
            }
            cached = destinations.ToArray();
            _destinations.Add(tile, cached);
            return cached;
        }
        private static int CompareSteps(BoardTravelRouteStep left, BoardTravelRouteStep right)
        {
            var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
            if (x != 0) return x;
            var y = left.Coordinate.y.CompareTo(right.Coordinate.y);
            return y != 0 ? y : left.Step.CompareTo(right.Step);
        }
    }


    internal readonly struct RestoredBoardTravelRouteState
    {
        public RestoredBoardTravelRouteState(
            bool active,
            Vector2Int origin,
            int startingStep,
            BoardRouteChoice[] choices)
        {
            Active = active;
            Origin = origin;
            StartingStep = startingStep;
            Choices = choices ?? Array.Empty<BoardRouteChoice>();
        }

        public bool Active { get; }
        public Vector2Int Origin { get; }
        public int StartingStep { get; }
        public BoardRouteChoice[] Choices { get; }
    }

    internal static class BoardTravelRouteStatePolicy
    {
        public static int GetStartingStepAfterRelocation(
            int turnRoll,
            int remainingMoves)
        {
            var safeRoll = Mathf.Clamp(
                turnRoll,
                0,
                WorldDieAuthorityModel.MaximumFace * 2);
            return Mathf.Clamp(safeRoll - remainingMoves, 0, safeRoll);
        }

        public static RestoredBoardTravelRouteState Restore(
            BoardTopology topology,
            int turnRoll,
            int remainingMoves,
            bool hasOrigin,
            Vector2Int origin,
            int startingStep,
            IReadOnlyList<BoardRouteChoice> choices)
        {
            var safeRoll = Mathf.Clamp(
                turnRoll,
                0,
                WorldDieAuthorityModel.MaximumFace * 2);
            if (topology == null || safeRoll <= 0 || !hasOrigin ||
                remainingMoves < 0 || remainingMoves > safeRoll ||
                !topology.TryGetTile(origin, out var originTile) ||
                originTile == null)
            {
                return new RestoredBoardTravelRouteState(
                    false,
                    default,
                    0,
                    Array.Empty<BoardRouteChoice>());
            }

            var maximumStartingStep = safeRoll - remainingMoves;
            if (startingStep < 0 || startingStep > maximumStartingStep)
            {
                return new RestoredBoardTravelRouteState(
                    false,
                    default,
                    0,
                    Array.Empty<BoardRouteChoice>());
            }

            var restoredChoices = new List<BoardRouteChoice>();
            if (choices != null)
            {
                for (var index = 0; index < choices.Count; index++)
                {
                    if (!IsValidForkChoice(topology, choices[index]))
                    {
                        return new RestoredBoardTravelRouteState(
                            false,
                            default,
                            0,
                            Array.Empty<BoardRouteChoice>());
                    }

                    restoredChoices.Add(choices[index]);
                }
            }

            var safeStartingStep = startingStep;
            var preview = new BoardTravelRoutePreview(
                topology,
                originTile,
                safeStartingStep,
                safeRoll);
            if (preview.GetCompatibleRouteCount(restoredChoices) == 0)
            {
                return new RestoredBoardTravelRouteState(
                    false,
                    default,
                    0,
                    Array.Empty<BoardRouteChoice>());
            }

            return new RestoredBoardTravelRouteState(
                true,
                origin,
                safeStartingStep,
                restoredChoices.ToArray());
        }

        public static bool IsValidForkChoice(
            BoardTopology topology,
            BoardRouteChoice choice)
        {
            if (topology == null ||
                !topology.TryGetTile(choice.Source, out var source) ||
                !topology.TryGetTile(choice.Destination, out var destination) ||
                source == null || destination == null)
            {
                return false;
            }

            var outgoing = topology.GetOutgoingGates(source);
            var destinations = new HashSet<BoardTile>();
            var hasSelectedExit = false;
            for (var index = 0; index < outgoing.Count; index++)
            {
                if (outgoing[index] == null || outgoing[index].IsTemporarilyBlocked) continue;
                var gateDestination = outgoing[index].Destination;
                if (gateDestination == null || !destinations.Add(gateDestination))
                    continue;

                if (gateDestination == destination)
                    hasSelectedExit = true;
            }

            return destinations.Count > 1 && hasSelectedExit;
        }
    }
}
