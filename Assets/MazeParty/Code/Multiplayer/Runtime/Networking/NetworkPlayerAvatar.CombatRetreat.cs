using MazeParty.Gameplay;
using UnityEngine;

namespace MazeParty.Multiplayer
{
    public sealed partial class NetworkPlayerAvatar
    {
        // The server owns the route. Sampling that same route after NetworkTransform
        // interpolation keeps every viewer and the delayed cameras on one clock.
        private void TickCombatRetreatPresentation()
        {
            var match = NetworkMatchState.Instance;
            if (!IsSpawned || match == null || !match.IsCombatRetreatActive ||
                !match.CombatRetreat.Contains(AssignedSlot)) return;
            ApplyCombatRetreatPose(match.CombatRetreat.GetPath(AssignedSlot), match.CombatRetreatElapsed);
        }

        internal void ApplyCombatRetreatPose(BoardCombatRetreatPath path, double elapsed)
        {
            if (path.Points.Length == 0) return;
            TeleportController(path.Evaluate(elapsed), path.Facing);
            var match = NetworkMatchState.Instance;
            _avatarVisual?.SetKnockedDown(match != null && match.IsCombatRetreatKnockedDown(AssignedSlot));
        }

        private void AppendCombatRetreatStep(ref BoardCombatRetreatPath path, BoardTile from, BoardTile to)
        {
            // Retrace the actual corridor, including its authored gate on freeform maps.
            if (_topology != null)
            {
                var incoming = _topology.GetIncomingGates(from);
                for (var i = 0; i < incoming.Count; i++)
                {
                    if (incoming[i] == null || incoming[i].Source != to) continue;
                    path.Add(incoming[i].PlanePoint + Vector3.up);
                    break;
                }
            }
            path.Add(to.GetRecoveryCenter(1f));
        }
    }
}
