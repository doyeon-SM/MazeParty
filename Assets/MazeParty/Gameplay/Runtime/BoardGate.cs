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
            return lateralDistance - support <= GateWidth * 0.5f;
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
