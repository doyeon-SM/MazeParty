using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum BoardGateCapsuleRelation
    {
        SourceSide,
        Straddling,
        DestinationSide
    }

    public enum BoardGateTraversalOutcome
    {
        InvalidConfiguration,
        NotFromSource,
        OutsideGateSpan,
        SourceSide,
        PartialCrossing,
        BlockedNoMoves,
        Committed
    }

    [DisallowMultipleComponent]
    public sealed class BoardGate : MonoBehaviour
    {
        public const float MinimumTraversalRadius = 0.5f;

        private const float CorridorEpsilon = 0.001f;

        [SerializeField] private BoardTile source;
        [SerializeField] private BoardTile destination;
        [SerializeField, Min(0.1f)] private float gateWidth = 2.5f;
        [SerializeField, Min(0f)] private float crossingEpsilon = 0.001f;
        [SerializeField] private bool drawDebugBoundary = true;

        public BoardTile Source => source;
        public BoardTile Destination => destination;
        public float CrossingEpsilon => crossingEpsilon;
        public Vector3 PlanePoint => transform.position;
        public Vector3 ForwardNormal => transform.forward.normalized;
        public float GateWidth => Mathf.Max(0.1f, gateWidth);

        public void Configure(
            BoardTile sourceTile,
            BoardTile destinationTile,
            float width = 2.5f)
        {
            source = sourceTile;
            destination = destinationTile;
            gateWidth = Mathf.Max(0.1f, width);
        }

        public float GetSignedDistance(Vector3 worldPoint)
        {
            return Vector3.Dot(worldPoint - PlanePoint, ForwardNormal);
        }

        public BoardGateCapsuleRelation GetCapsuleRelation(CharacterController controller)
        {
            if (controller == null)
                return BoardGateCapsuleRelation.SourceSide;

            var center = controller.transform.TransformPoint(controller.center);
            var support = GetCapsuleSupport(controller, ForwardNormal);
            var distance = GetSignedDistance(center);
            var safeEpsilon = Mathf.Max(0f, crossingEpsilon);

            if (distance - support >= safeEpsilon)
                return BoardGateCapsuleRelation.DestinationSide;
            if (distance + support <= -safeEpsilon)
                return BoardGateCapsuleRelation.SourceSide;
            return BoardGateCapsuleRelation.Straddling;
        }

        public bool IsCapsuleWithinGateSpan(CharacterController controller)
        {
            if (controller == null)
                return false;

            var center = controller.transform.TransformPoint(controller.center);
            var right = transform.right.normalized;
            var lateralDistance = Mathf.Abs(Vector3.Dot(center - PlanePoint, right));
            var support = GetCapsuleSupport(controller, right);
            return lateralDistance + support <=
                   GateWidth * 0.5f + CorridorEpsilon;
        }

        public bool IsCapsuleWithinEndpointCorridor(
            BoardTile endpoint,
            CharacterController controller)
        {
            if (endpoint == null || controller == null)
            {
                return false;
            }

            var right = transform.right.normalized;
            var lateralSupport = GetCapsuleSupport(controller, right);
            var footprintSupport = GetMaximumPlanarCapsuleSupport(
                controller,
                endpoint.transform.up);
            var center = controller.transform.TransformPoint(controller.center);
            return IsPointWithinEndpointCorridor(
                endpoint,
                center,
                lateralSupport,
                footprintSupport);
        }

        /// <summary>
        /// Returns whether a point lies in the authored approach corridor between
        /// one endpoint footprint and this gate plane. This keeps separated
        /// freeform tiles traversable without treating the whole area between tile
        /// centers as playable space.
        /// </summary>
        public bool IsPointWithinEndpointCorridor(
            BoardTile endpoint,
            Vector3 worldPoint)
        {
            return IsPointWithinEndpointCorridor(endpoint, worldPoint, 0f, 0f);
        }

        /// <summary>
        /// Resolves the signed start of the guaranteed corridor from an endpoint's
        /// safe interior center to this gate plane. Using an interior anchor keeps
        /// slanted triangle and pentagon edges continuously traversable.
        /// </summary>
        public bool TryGetEndpointCorridorLimit(
            BoardTile endpoint,
            out float signedDistance,
            float lateralInset = 0f)
        {
            return TryGetEndpointCorridorLimit(
                endpoint,
                out signedDistance,
                lateralInset,
                lateralInset,
                out _);
        }

        private bool TryGetEndpointCorridorLimit(
            BoardTile endpoint,
            out float signedDistance,
            float lateralInset,
            float endpointInset,
            out Vector3 endpointAnchor)
        {
            signedDistance = 0f;
            endpointAnchor = default;
            var isSource = endpoint != null && endpoint == source;
            var isDestination = endpoint != null && endpoint == destination;
            if (!isSource && !isDestination)
            {
                return false;
            }

            var usableHalfWidth =
                GateWidth * 0.5f - Mathf.Max(0f, lateralInset);
            if (usableHalfWidth < -CorridorEpsilon)
            {
                return false;
            }

            if (!endpoint.CanContainHorizontalInset(endpointInset))
            {
                return false;
            }

            endpointAnchor = endpoint.GetRecoveryCenter(
                horizontalInset: endpointInset);
            signedDistance = GetSignedDistance(endpointAnchor);
            return float.IsFinite(signedDistance) &&
                   (isSource
                       ? signedDistance <= CorridorEpsilon
                       : signedDistance >= -CorridorEpsilon);
        }

        private bool IsPointWithinEndpointCorridor(
            BoardTile endpoint,
            Vector3 worldPoint,
            float lateralInset,
            float endpointInset)
        {
            if (!TryGetEndpointCorridorLimit(
                    endpoint,
                    out var endpointDistance,
                    lateralInset,
                    endpointInset,
                    out var endpointAnchor))
            {
                return false;
            }

            var distance = GetSignedDistance(worldPoint);
            var betweenEndpointAndPlane = endpoint == source
                ? distance >= endpointDistance - CorridorEpsilon &&
                  distance <= CorridorEpsilon
                : distance <= endpointDistance + CorridorEpsilon &&
                  distance >= -CorridorEpsilon;
            if (!betweenEndpointAndPlane)
            {
                return false;
            }

            var right = transform.right.normalized;
            var forward = ForwardNormal;
            var endpointOffset = endpointAnchor - PlanePoint;
            var pointOffset = worldPoint - PlanePoint;
            var endpointOnPlane = new Vector2(
                Vector3.Dot(endpointOffset, right),
                Vector3.Dot(endpointOffset, forward));
            var pointOnPlane = new Vector2(
                Vector3.Dot(pointOffset, right),
                Vector3.Dot(pointOffset, forward));
            var denominator = endpointOnPlane.sqrMagnitude;
            var progress = denominator > CorridorEpsilon * CorridorEpsilon
                ? Mathf.Clamp01(Vector2.Dot(pointOnPlane, endpointOnPlane) /
                                denominator)
                : 0f;
            var closest = endpointOnPlane * progress;
            var usableHalfWidth = Mathf.Max(
                0f,
                GateWidth * 0.5f - Mathf.Max(0f, lateralInset));
            return (pointOnPlane - closest).sqrMagnitude <=
                   usableHalfWidth * usableHalfWidth + CorridorEpsilon;
        }

        public BoardGateTraversalOutcome TryTraverse(
            BoardTraversalState traversal,
            CharacterController controller)
        {
            if (source == null || destination == null || source == destination || traversal == null || controller == null)
                return BoardGateTraversalOutcome.InvalidConfiguration;
            if (traversal.CurrentTile != source)
                return BoardGateTraversalOutcome.NotFromSource;
            if (!IsCapsuleWithinGateSpan(controller))
                return BoardGateTraversalOutcome.OutsideGateSpan;

            var relation = GetCapsuleRelation(controller);
            if (relation == BoardGateCapsuleRelation.SourceSide)
                return BoardGateTraversalOutcome.SourceSide;
            if (relation == BoardGateCapsuleRelation.Straddling)
                return BoardGateTraversalOutcome.PartialCrossing;
            if (!traversal.HasRemainingMoves)
                return BoardGateTraversalOutcome.BlockedNoMoves;

            return traversal.TryCommit(this)
                ? BoardGateTraversalOutcome.Committed
                : BoardGateTraversalOutcome.InvalidConfiguration;
        }

        private static float GetCapsuleSupport(CharacterController controller, Vector3 direction)
        {
            var controllerTransform = controller.transform;
            var scale = controllerTransform.lossyScale;
            var radialScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var verticalScale = Mathf.Abs(scale.y);
            var radius = controller.radius * radialScale;
            var height = Mathf.Max(controller.height * verticalScale, radius * 2f);
            var segmentHalfLength = Mathf.Max(0f, height * 0.5f - radius);
            var axis = controllerTransform.up.normalized;
            return radius + segmentHalfLength * Mathf.Abs(Vector3.Dot(axis, direction.normalized));
        }

        public static float GetMaximumPlanarCapsuleSupport(
            CharacterController controller,
            Vector3 planeNormal)
        {
            if (controller == null)
            {
                return 0f;
            }

            var controllerTransform = controller.transform;
            var scale = controllerTransform.lossyScale;
            var radialScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            var verticalScale = Mathf.Abs(scale.y);
            var radius = controller.radius * radialScale;
            var height = Mathf.Max(controller.height * verticalScale, radius * 2f);
            var segmentHalfLength = Mathf.Max(0f, height * 0.5f - radius);
            var safePlaneNormal = planeNormal.sqrMagnitude > 0.000001f
                ? planeNormal.normalized
                : Vector3.up;
            var projectedAxis = Vector3.ProjectOnPlane(
                controllerTransform.up.normalized,
                safePlaneNormal);
            return radius + segmentHalfLength * projectedAxis.magnitude;
        }

        private void OnDrawGizmos()
        {
            if (!drawDebugBoundary)
                return;

            var previousColor = Gizmos.color;
            Gizmos.color = Color.magenta;
            var right = transform.right.normalized * (GateWidth * 0.5f);
            Gizmos.DrawLine(PlanePoint - right, PlanePoint + right);
            Gizmos.DrawRay(PlanePoint, ForwardNormal * 1.5f);
            Gizmos.color = previousColor;
        }
    }
}
