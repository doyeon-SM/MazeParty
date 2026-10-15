using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardResolutionAuthorityTests
    {
        [Test]
        public void MissingPlayerLookup_CannotSkipCombatOrCaptureAnEmptyLandingPlan()
        {
            var root = new GameObject("Board resolution authority fixture", typeof(NetworkObject));
            try
            {
                var match = root.AddComponent<NetworkMatchState>();
                typeof(NetworkBehaviour).GetProperty(nameof(NetworkBehaviour.IsServer))
                    .SetValue(match, true);
                var flow = new BoardFlowStateMachine();
                flow.Start(0d);
                flow.Tick(6d);
                for (var slot = 0; slot < 4; slot++) flow.TryReportPlayerArrived(slot, 10d);
                flow.Tick(15d);
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(NetworkMatchState).GetField("_flow", flags).SetValue(match, flow);
                foreach (var method in new[] { "BeginCombatSequenceOnServer",
                    "BeginNextCombatOrFinishOnServer", "AdvanceCombatOnServer" })
                {
                    typeof(NetworkMatchState).GetMethod(method, flags)
                        .Invoke(match, new object[] { 100d });
                    Assert.That(flow.State, Is.EqualTo(BoardFlowState.CombatResolve), method);
                    Assert.That(typeof(NetworkMatchState).GetField("_landingEffectPlanReady", flags)
                        .GetValue(match), Is.EqualTo(false), method);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
    }
}
