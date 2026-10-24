using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class AwardCeremonyMovementAuthorityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        [Test]
        public void CeremonyPlacementAndMovement_RespectAuthorityLockPauseAndReturnedSeats()
        {
            var previousMatch = NetworkMatchState.Instance;
            var previousStage = AwardCeremonyStageBindings.Instance;
            var warnings = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            var previousWarning = (bool)warnings.GetValue(null);
            var root = new GameObject("Ceremony movement fixture");
            try
            {
                warnings.SetValue(null, true);
                var stageObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Board/World/AwardCeremonyStage.prefab"));
                stageObject.transform.SetParent(root.transform);
                stageObject.transform.position = new Vector3(6000, 0, 6000);
                var stage = stageObject.GetComponent<AwardCeremonyStageBindings>();
                typeof(AwardCeremonyStageBindings).GetProperty("Instance").SetValue(null, stage);
                stage.PresentationRoot.SetActive(true);
                var matchObject = new GameObject("Match", typeof(NetworkObject));
                matchObject.transform.SetParent(root.transform);
                var match = matchObject.AddComponent<NetworkMatchState>();
                Authority(match, true);
                typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, match);
                Set(match, "_gameplayEnabled", true);
                Set(match, "_flowState", (byte)BoardFlowState.MatchComplete);
                Set(match, "_awardCeremonyPhase", (byte)AwardCeremonyPhase.FinalPodiumLocked);
                Set(match, "_awardCeremonyRemainingMask", (byte)15);
                var playerObject = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Multiplayer/NetworkPlayer.prefab"));
                playerObject.transform.SetParent(root.transform);
                var player = playerObject.GetComponent<NetworkPlayerAvatar>();
                // EditMode does not run the normal Awake lifecycle for this fixture.
                typeof(NetworkPlayerAvatar).GetField("_characterController", Private).SetValue(player,
                    playerObject.GetComponent<CharacterController>());
                Authority(player, false);
                Set(player, "_slot", 0);
                Set(player, "_gold", 37);
                Set(player, "_keyCount", 6);
                Set(player, "_minigameWins", 3);
                typeof(NetworkMatchState).GetField("_avatarLookupCache", Private).SetValue(match,
                    new[] {player, null, null, null});
                var target = stage.GetPlayerPosition(4, 0, 1);
                player.PrepareForAwardCeremonyOnServer(target, Quaternion.identity);
                Assert.That(player.transform.position, Is.Not.EqualTo(target), "Client cannot place an avatar.");
                Authority(player, true);
                player.PrepareForAwardCeremonyOnServer(target, Quaternion.identity);
                Assert.That(player.transform.position, Is.EqualTo(target));
                Assert.That(new[] {player.KeyCount, player.Gold, player.MinigameWins}, Is.EqualTo(new[] {6,37,3}));

                foreach (var state in new[] {
                    (AwardCeremonyPhase.FinalPodiumLocked, false, (byte)15, (byte)0, false),
                    (AwardCeremonyPhase.AwaitingReturn, true, (byte)15, (byte)0, false),
                    (AwardCeremonyPhase.AwaitingReturn, false, (byte)15, (byte)1, false),
                    (AwardCeremonyPhase.AwaitingReturn, false, (byte)14, (byte)0, false),
                    (AwardCeremonyPhase.AwaitingReturn, false, (byte)15, (byte)0, true) })
                {
                    Set(match, "_awardCeremonyPhase", (byte)state.Item1);
                    Set(match, "_playerPauseActive", state.Item2);
                    Set(match, "_awardCeremonyRemainingMask", state.Item3);
                    Set(match, "_awardCeremonyReturnReadyMask", state.Item4);
                    typeof(NetworkPlayerAvatar).GetField("_serverInput", Private).SetValue(player, Vector2.right);
                    var before = player.transform.position;
                    typeof(NetworkPlayerAvatar).GetMethod("SimulateAwardCeremonyOnServer", Private).Invoke(player, null);
                    Assert.That(player.transform.position.x > before.x, Is.EqualTo(state.Item5));
                }
                // Exercise the real server Update -> avatar FixedUpdate sequence.
                // Rotation could work even while Update cleared every movement RPC.
                var managerObject = new GameObject("Ceremony server fixture");
                managerObject.transform.SetParent(root.transform);
                var manager = managerObject.AddComponent<NetworkManager>();
                var spawnManager = (NetworkSpawnManager)System.Activator.CreateInstance(
                    typeof(NetworkSpawnManager), Private, null, new object[] { manager }, null);
                typeof(NetworkManager).GetProperty("SpawnManager").SetValue(manager, spawnManager);
                var connections = typeof(NetworkManager).GetField("ConnectionManager", Private).GetValue(manager);
                ((System.Collections.Generic.List<ulong>)connections.GetType()
                    .GetField("ConnectedClientIds", Private).GetValue(connections)).Add(0);
                ((System.Collections.Generic.Dictionary<ulong, System.Collections.Generic.List<NetworkObject>>)
                    typeof(NetworkSpawnManager).GetField("m_PlayerObjectsTable", Private).GetValue(spawnManager))
                    .Add(0, new System.Collections.Generic.List<NetworkObject> { playerObject.GetComponent<NetworkObject>() });
                typeof(NetworkBehaviour).GetField("m_NetworkManager", Private).SetValue(match, manager);
                var flow = new BoardFlowStateMachine();
                flow.RestoreCheckpoint(BoardFlowState.MatchComplete, flow.TotalTurns, 0);
                typeof(NetworkMatchState).GetField("_flow", Private).SetValue(match, flow);
                Set(match, "_awardCeremonyPhase", (byte)AwardCeremonyPhase.AwaitingReturn);
                Set(match, "_awardCeremonyRemainingMask", (byte)15);
                Set(match, "_awardCeremonyReturnReadyMask", (byte)0);
                Set(match, "_awardCeremonyAutoReturnEndsAt", double.MaxValue);
                var podiumStart = stage.GetPlayerPosition(1, 0, 1);
                player.PrepareForAwardCeremonyOnServer(podiumStart, Quaternion.identity);
                Physics.SyncTransforms();
                // This fixture has no transport: reset audio cadence while testing movement.
                var cadence = typeof(NetworkPlayerAvatar).GetField("_footstepCadence", Private).GetValue(player);
                for (var frame = 0; frame < 100; frame++)
                {
                    cadence.GetType().GetMethod("Reset").Invoke(cadence, null);
                    typeof(NetworkPlayerAvatar).GetField("_serverInput", Private).SetValue(player, Vector2.down);
                    typeof(NetworkMatchState).GetMethod("TickBoardFlowOnServer", Private)
                        .Invoke(match, new object[] { frame * .02d });
                    typeof(NetworkPlayerAvatar).GetMethod("SimulateAwardCeremonyOnServer", Private).Invoke(player, null);
                }
                Assert.That(player.transform.position.z, Is.LessThan(podiumStart.z - 2f),
                    "Server frame updates must preserve ceremony walking input.");
                Assert.That(player.transform.position.y, Is.LessThan(podiumStart.y - 1f),
                    "The authored player must be able to leave the podium and land on the stage.");
                Assert.That(player.transform.position.y, Is.GreaterThan(.5f),
                    "Walking off the podium must retain floor collision.");

                var clamped = stage.ClampPosition(target + new Vector3(100,0,100), .5f);
                Assert.That(stage.StageFloor.bounds.Contains(new Vector3(clamped.x,
                    stage.StageFloor.bounds.center.y, clamped.z)), Is.True);
                for (var count = 1; count <= 4; count++)
                {
                    var previousX = float.NegativeInfinity;
                    for (var index = 0; index < count; index++)
                    {
                        var position = stage.GetPlayerPosition(1, index, count);
                        Assert.That(position.x, Is.GreaterThan(previousX));
                        Assert.That(stage.PodiumRoots[0].GetComponent<BoxCollider>().bounds.Contains(
                            new Vector3(position.x, stage.PodiumRoots[0].position.y, position.z)), Is.True);
                        previousX = position.x;
                    }
                }
            }
            finally
            {
                typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, previousMatch);
                foreach (var b in root.GetComponentsInChildren<NetworkBehaviour>(true))
                    typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(b, false);
                Object.DestroyImmediate(root);
                typeof(AwardCeremonyStageBindings).GetProperty("Instance").SetValue(null, previousStage);
                warnings.SetValue(null, previousWarning);
            }
        }

        private static void Authority(NetworkBehaviour b, bool server)
        {
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(b, server);
            typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(b, true);
        }
        private static void Set<T>(object target, string field, T value) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(field, Private).GetValue(target)).Value = value;
    }
}
