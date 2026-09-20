using System;
using System.Collections.Generic;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum BoardTopologyIssueCode
    {
        NullTile,
        DuplicateCoordinate,
        MissingStartTile,
        NullGate,
        MissingGateEndpoint,
        GateSelfLoop,
        GateEndpointNotRegistered,
        GateTilesNotAdjacent,
        DuplicateDirectedGate,
        GateDirectionMismatch,
        GatePlaneDoesNotSeparateTiles
    }

    public readonly struct BoardTopologyIssue
    {
        public BoardTopologyIssue(BoardTopologyIssueCode code, string message, UnityEngine.Object context)
        {
            Code = code;
            Message = message;
            Context = context;
        }

        public BoardTopologyIssueCode Code { get; }
        public string Message { get; }
        public UnityEngine.Object Context { get; }
    }

    public sealed class BoardTopologyValidationResult
    {
        internal BoardTopologyValidationResult(List<BoardTopologyIssue> issues)
        {
            Issues = issues.AsReadOnly();
        }

        public IReadOnlyList<BoardTopologyIssue> Issues { get; }
        public bool IsValid => Issues.Count == 0;
    }

    public static class BoardTopologyValidator
    {
        public static BoardTopologyValidationResult Validate(
            IReadOnlyList<BoardTile> tiles,
            IReadOnlyList<BoardGate> gates)
        {
            var issues = new List<BoardTopologyIssue>();
            var registeredTiles = new HashSet<BoardTile>();
            var coordinates = new Dictionary<Vector2Int, BoardTile>();
            var startCount = 0;

            if (tiles != null)
            {
                for (var i = 0; i < tiles.Count; i++)
                {
                    var tile = tiles[i];
                    if (tile == null)
                    {
                        issues.Add(new BoardTopologyIssue(
                            BoardTopologyIssueCode.NullTile,
                            "Topology contains a null tile.",
                            null));
                        continue;
                    }

                    registeredTiles.Add(tile);
                    if (tile.TileType == BoardTileType.Start)
                        startCount++;

                    if (coordinates.TryGetValue(tile.Coordinate, out var duplicate))
                    {
                        issues.Add(new BoardTopologyIssue(
                            BoardTopologyIssueCode.DuplicateCoordinate,
                            $"Tiles '{duplicate.name}' and '{tile.name}' use coordinate {tile.Coordinate}.",
                            tile));
                    }
                    else
                    {
                        coordinates.Add(tile.Coordinate, tile);
                    }
                }
            }

            if (startCount == 0)
            {
                issues.Add(new BoardTopologyIssue(
                    BoardTopologyIssueCode.MissingStartTile,
                    "Topology requires at least one Start tile.",
                    null));
            }

            var directedEdges = new HashSet<string>(StringComparer.Ordinal);
            if (gates != null)
            {
                for (var i = 0; i < gates.Count; i++)
                {
                    var gate = gates[i];
                    if (gate == null)
                    {
                        issues.Add(new BoardTopologyIssue(
                            BoardTopologyIssueCode.NullGate,
                            "Topology contains a null gate.",
                            null));
                        continue;
                    }

                    ValidateGate(gate, registeredTiles, directedEdges, issues);
                }
            }

            return new BoardTopologyValidationResult(issues);
        }

        private static void ValidateGate(
            BoardGate gate,
            HashSet<BoardTile> registeredTiles,
            HashSet<string> directedEdges,
            List<BoardTopologyIssue> issues)
        {
            var source = gate.Source;
            var destination = gate.Destination;
            if (source == null || destination == null)
            {
                issues.Add(new BoardTopologyIssue(
                    BoardTopologyIssueCode.MissingGateEndpoint,
                    $"Gate '{gate.name}' must reference both source and destination tiles.",
                    gate));
                return;
            }

            if (source == destination)
            {
                issues.Add(new BoardTopologyIssue(
                    BoardTopologyIssueCode.GateSelfLoop,
                    $"Gate '{gate.name}' cannot lead back to the same tile.",
                    gate));
                return;
            }

            if (!registeredTiles.Contains(source) || !registeredTiles.Contains(destination))
            {
                issues.Add(new BoardTopologyIssue(
                    BoardTopologyIssueCode.GateEndpointNotRegistered,
                    $"Gate '{gate.name}' references a tile outside this topology.",
                    gate));
            }

            var coordinateDelta = destination.Coordinate - source.Coordinate;
            if (Mathf.Abs(coordinateDelta.x) + Mathf.Abs(coordinateDelta.y) != 1)
            {
                issues.Add(new BoardTopologyIssue(
                    BoardTopologyIssueCode.GateTilesNotAdjacent,
                    $"Gate '{gate.name}' must connect adjacent grid coordinates.",
                    gate));
            }

            var edgeKey = source.Coordinate + ">" + destination.Coordinate;
            if (!directedEdges.Add(edgeKey))
            {
                issues.Add(new BoardTopologyIssue(
                    BoardTopologyIssueCode.DuplicateDirectedGate,
                    $"Duplicate directed gate from '{source.name}' to '{destination.name}'.",
                    gate));
            }

            var sourceUp = source.transform.up.normalized;
            var direction = destination.WorldCenter - source.WorldCenter;
            direction -= sourceUp * Vector3.Dot(direction, sourceUp);
            if (direction.sqrMagnitude <= 0.0001f ||
                Vector3.Dot(gate.ForwardNormal, direction.normalized) < 0.95f)
            {
                issues.Add(new BoardTopologyIssue(
                    BoardTopologyIssueCode.GateDirectionMismatch,
                    $"Gate '{gate.name}' forward must point from source to destination.",
                    gate));
            }

            if (gate.GetSignedDistance(source.WorldCenter) >= 0f ||
                gate.GetSignedDistance(destination.WorldCenter) <= 0f)
            {
                issues.Add(new BoardTopologyIssue(
                    BoardTopologyIssueCode.GatePlaneDoesNotSeparateTiles,
                    $"Gate '{gate.name}' plane must separate source and destination centers.",
                    gate));
            }
        }
    }
}
