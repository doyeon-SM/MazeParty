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
    /// Precomputes every directed walk that can be taken from the tile
    /// where a turn's dice result was committed. The origin is normally step
    /// zero; after an authoritative relocation it can resume at the number of
    /// already-consumed moves. No route extends beyond the committed result. A
    /// route becomes a branch when it leaves the optional preferred route (or
    /// takes any exit after the first authored outgoing gate when no preferred
    /// route is available). After a fork is committed, the compatible route
    /// whose remaining prefix follows the shortest path to the destination is
    /// promoted to primary.
    /// </summary>
    public sealed class BoardTravelRoutePreview
    {
        private sealed class CandidateRoute
        {
            public CandidateRoute(RouteNode[] nodes)
            {
                Nodes = nodes;
            }

            public RouteNode[] Nodes { get; }
        }

        private readonly struct RouteNode
        {
            public RouteNode(BoardTile tile, bool isBranch)
            {
                Tile = tile;
                IsBranch = isBranch;
            }

            public BoardTile Tile { get; }
            public bool IsBranch { get; }
        }

        private readonly struct StepKey : IEquatable<StepKey>
        {
            public StepKey(Vector2Int coordinate, int step)
            {
                Coordinate = coordinate;
                Step = step;
            }

            public Vector2Int Coordinate { get; }
            public int Step { get; }

            public bool Equals(StepKey other)
            {
                return Coordinate == other.Coordinate && Step == other.Step;
            }

            public override bool Equals(object obj)
            {
                return obj is StepKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(Coordinate, Step);
            }
        }

        private readonly List<CandidateRoute> _routes =
            new List<CandidateRoute>();
        private readonly Dictionary<StepKey, bool> _visibleSteps =
            new Dictionary<StepKey, bool>();
        private readonly List<BoardTile> _primaryRoute =
            new List<BoardTile>();
        private readonly List<BoardTile> _choicePrimaryRoute =
            new List<BoardTile>();
        private readonly BoardTopology _topology;
        private readonly BoardTile _primaryDestination;

        public BoardTravelRoutePreview(
            BoardTopology topology,
            BoardTile turnOrigin,
            int maximumStep)
            : this(topology, turnOrigin, 0, maximumStep, null)
        {
        }

        public BoardTravelRoutePreview(
            BoardTopology topology,
            BoardTile turnOrigin,
            int maximumStep,
            BoardTile primaryDestination)
            : this(
                topology,
                turnOrigin,
                0,
                maximumStep,
                primaryDestination)
        {
        }

        public BoardTravelRoutePreview(
            BoardTopology topology,
            BoardTile turnOrigin,
            int startingStep,
            int maximumStep)
            : this(
                topology,
                turnOrigin,
                startingStep,
                maximumStep,
                null)
        {
        }

        public BoardTravelRoutePreview(
            BoardTopology topology,
            BoardTile turnOrigin,
            int startingStep,
            int maximumStep,
            BoardTile primaryDestination)
        {
            if (topology == null)
                throw new ArgumentNullException(nameof(topology));
            if (turnOrigin == null)
                throw new ArgumentNullException(nameof(turnOrigin));
            if (startingStep < 0)
                throw new ArgumentOutOfRangeException(nameof(startingStep));
            if (maximumStep < startingStep)
                throw new ArgumentOutOfRangeException(nameof(maximumStep));

            StartCoordinate = turnOrigin.Coordinate;
            StartingStep = startingStep;
            MaximumStep = maximumStep;
            _topology = topology;
            _primaryDestination = primaryDestination;
            if (primaryDestination != null)
            {
                BoardMapRoute.TryFind(
                    topology,
                    turnOrigin,
                    primaryDestination,
                    _primaryRoute);
            }
            var path = new List<RouteNode>
            {
                new RouteNode(turnOrigin, false)
            };
            Enumerate(topology, turnOrigin, false, path);
        }

        public Vector2Int StartCoordinate { get; }
        public int StartingStep { get; }
        public int MaximumStep { get; }
        public int RouteCount => _routes.Count;

        public static bool ShouldDisplayForTurn(
            bool isActionPhase,
            int turnRoll,
            bool hasValidOrigin)
        {
            return isActionPhase && turnRoll > 0 && hasValidOrigin;
        }

        public int GetCompatibleRouteCount(
            IReadOnlyList<BoardRouteChoice> choices)
        {
            var count = 0;
            for (var index = 0; index < _routes.Count; index++)
            {
                if (IsCompatible(_routes[index], choices))
                    count++;
            }

            return count;
        }

        /// <summary>
        /// Writes unique visible route numbers ordered by coordinate (x, then y)
        /// and step. If the same coordinate and number are reachable as both a
        /// primary and branch step, primary wins so the number is emitted once.
        /// </summary>
        public int GetVisibleSteps(
            IReadOnlyList<BoardRouteChoice> choices,
            List<BoardTravelRouteStep> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();
            _visibleSteps.Clear();
            var choicePrimaryRoute = ResolveChoicePrimaryRoute(choices);
            for (var routeIndex = 0; routeIndex < _routes.Count; routeIndex++)
            {
                var route = _routes[routeIndex];
                if (!IsCompatible(route, choices))
                    continue;

                for (var step = 0; step < route.Nodes.Length; step++)
                {
                    var node = route.Nodes[step];
                    var key = new StepKey(
                        node.Tile.Coordinate,
                        StartingStep + step);
                    var isBranch = choicePrimaryRoute != null
                        ? !ReferenceEquals(route, choicePrimaryRoute)
                        : node.IsBranch;
                    if (_visibleSteps.TryGetValue(key, out var existingBranch))
                    {
                        _visibleSteps[key] = existingBranch && isBranch;
                    }
                    else
                    {
                        _visibleSteps.Add(key, isBranch);
                    }
                }
            }

            foreach (var pair in _visibleSteps)
            {
                destination.Add(new BoardTravelRouteStep(
                    pair.Key.Coordinate,
                    pair.Key.Step,
                    pair.Value));
            }

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

        private CandidateRoute ResolveChoicePrimaryRoute(
            IReadOnlyList<BoardRouteChoice> choices)
        {
            if (choices == null || choices.Count == 0 ||
                _primaryDestination == null ||
                !_topology.TryGetTile(
                    choices[choices.Count - 1].Destination,
                    out var current) ||
                current == null ||
                !BoardMapRoute.TryFind(
                    _topology,
                    current,
                    _primaryDestination,
                    _choicePrimaryRoute))
            {
                return null;
            }

            CandidateRoute best = null;
            var bestPrefixLength = -1;
            for (var routeIndex = 0; routeIndex < _routes.Count; routeIndex++)
            {
                var route = _routes[routeIndex];
                if (!TryGetChoiceEndNodeIndex(
                        route,
                        choices,
                        out var choiceEndNode))
                {
                    continue;
                }

                var prefixLength = 0;
                while (choiceEndNode + prefixLength < route.Nodes.Length &&
                       prefixLength < _choicePrimaryRoute.Count &&
                       route.Nodes[choiceEndNode + prefixLength].Tile ==
                       _choicePrimaryRoute[prefixLength])
                {
                    prefixLength++;
                }

                if (prefixLength <= bestPrefixLength)
                    continue;

                best = route;
                bestPrefixLength = prefixLength;
            }

            return best;
        }

        private void Enumerate(
            BoardTopology topology,
            BoardTile current,
            bool isBranch,
            List<RouteNode> path)
        {
            if (path.Count - 1 >= MaximumStep - StartingStep)
            {
                _routes.Add(new CandidateRoute(path.ToArray()));
                return;
            }

            var outgoing = topology.GetOutgoingGates(current);
            var destinations = new HashSet<BoardTile>();
            var extended = false;
            var routeStep = path.Count - 1;
            var preferredDestination = routeStep < _primaryRoute.Count - 1 &&
                                       _primaryRoute[routeStep] == current
                ? _primaryRoute[routeStep + 1]
                : null;
            for (var gateIndex = 0; gateIndex < outgoing.Count; gateIndex++)
            {
                var destination = outgoing[gateIndex]?.Destination;
                if (destination == null ||
                    !destinations.Add(destination))
                {
                    continue;
                }

                extended = true;
                var destinationIsBranch = isBranch ||
                    (preferredDestination != null
                        ? destination != preferredDestination
                        : destinations.Count > 1);
                path.Add(new RouteNode(destination, destinationIsBranch));
                Enumerate(
                    topology,
                    destination,
                    destinationIsBranch,
                    path);
                path.RemoveAt(path.Count - 1);
            }

            if (!extended)
                _routes.Add(new CandidateRoute(path.ToArray()));
        }

        private static bool IsCompatible(
            CandidateRoute route,
            IReadOnlyList<BoardRouteChoice> choices)
        {
            return TryGetChoiceEndNodeIndex(route, choices, out _);
        }

        private static bool TryGetChoiceEndNodeIndex(
            CandidateRoute route,
            IReadOnlyList<BoardRouteChoice> choices,
            out int choiceEndNode)
        {
            choiceEndNode = 0;
            if (choices == null || choices.Count == 0)
                return true;

            var nextEdge = 0;
            for (var choiceIndex = 0; choiceIndex < choices.Count; choiceIndex++)
            {
                var choice = choices[choiceIndex];
                var matched = false;
                for (var edge = nextEdge; edge < route.Nodes.Length - 1; edge++)
                {
                    if (route.Nodes[edge].Tile.Coordinate != choice.Source)
                        continue;

                    if (route.Nodes[edge + 1].Tile.Coordinate == choice.Destination)
                    {
                        matched = true;
                        nextEdge = edge + 1;
                        choiceEndNode = edge + 1;
                    }

                    break;
                }

                if (!matched)
                    return false;
            }

            return true;
        }

        private static int CompareSteps(
            BoardTravelRouteStep left,
            BoardTravelRouteStep right)
        {
            var x = left.Coordinate.x.CompareTo(right.Coordinate.x);
            if (x != 0)
                return x;
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
                var gateDestination = outgoing[index]?.Destination;
                if (gateDestination == null || !destinations.Add(gateDestination))
                    continue;

                if (gateDestination == destination)
                    hasSelectedExit = true;
            }

            return destinations.Count > 1 && hasSelectedExit;
        }
    }
}
