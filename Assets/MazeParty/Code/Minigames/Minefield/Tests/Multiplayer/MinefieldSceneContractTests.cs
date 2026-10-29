using System.Linq;
using System.Reflection;
using MazeParty.Gameplay.Minigames.Minefield;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinefieldSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/Minefield/Minefield.unity";
        [TestCase("PlayHazardVfxRpc", 3)]
        [TestCase("PlaySonarPresentationRpc", 2)]
        public void PresentationRpc_IsReliablePerEvent(
            string methodName,
            int expectedParameterCount)
        {
            var rpc = typeof(NetworkMinefieldState).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rpc, Is.Not.Null);
            Assert.That(rpc.GetCustomAttribute<RpcAttribute>()?.Delivery,
                Is.EqualTo(RpcDelivery.Reliable));
            var parameters = rpc.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(expectedParameterCount));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(byte)));
            Assert.That(parameters[expectedParameterCount - 1].ParameterType,
                Is.EqualTo(typeof(Vector3)));
        }

        [Test]
        public void MinefieldScene_PreservesAuthoritativeLayoutAndAdditiveContract()
        {
            AssertAuthoritativeLayoutContract();

            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var state = roots
                    .SelectMany(root => root.GetComponentsInChildren<
                        NetworkMinefieldState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);

                var view = state.GetComponent<MinefieldNetworkView>();
                Assert.That(view, Is.Not.Null);
                Assert.That(
                    state.GetComponentsInChildren<Canvas>(true),
                    Is.Empty,
                    "Minefield communicates state through its world " +
                    "presentation and owns no dedicated Canvas HUD.");

                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));
                Assert.That(
                    state.GetComponentsInChildren<Component>(true).Any(
                        component => component != null &&
                                     component.GetType().FullName ==
                                     "Unity.Cinemachine.CinemachineCamera"),
                    Is.True);
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive scene must reuse Board's output Camera.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);
                Assert.That(
                    FindDescendant(state.transform, "Arena Presentation"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "Crusher Placeholder"),
                    Is.Not.Null);
                var floor = FindDescendant(state.transform, "Arena Floor");
                var west = FindDescendant(state.transform, "West Wall");
                var east = FindDescendant(state.transform, "East Wall");
                var environment = FindDescendant(state.transform,
                    "Minefield Environment");
                Assert.That(floor, Is.Not.Null);
                Assert.That(west, Is.Not.Null);
                Assert.That(east, Is.Not.Null);
                Assert.That(environment, Is.Not.Null);
                var width = NetworkMinefieldState.ArenaMaxX -
                            NetworkMinefieldState.ArenaMinX;
                Assert.That(floor.localScale.x,
                    Is.EqualTo(width).Within(0.001f));
                Assert.That(west.position.x,
                    Is.EqualTo(NetworkMinefieldState.ArenaMinX - 0.25f)
                        .Within(0.001f));
                Assert.That(east.position.x,
                    Is.EqualTo(NetworkMinefieldState.ArenaMaxX + 0.25f)
                        .Within(0.001f));
                Assert.That(FindDescendant(environment, "Grid X 1"),
                    Is.Not.Null);
                Assert.That(FindDescendant(environment, "Grid X 2"),
                    Is.Null);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void Sonar_StaysActiveUntilItsExactExtendedDeadline()
        {
            var root = new GameObject("Sonar deadline contract");
            root.AddComponent<NetworkObject>();
            var state = root.AddComponent<NetworkMinefieldState>();
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                var mask = (NetworkVariable<byte>)typeof(NetworkMinefieldState)
                    .GetField("_sonarMask", flags).GetValue(state);
                var ends = (double[])typeof(NetworkMinefieldState)
                    .GetField("_sonarEndsAt", flags).GetValue(state);
                mask.Value = 1;
                ends[0] = 10d + NetworkMinefieldState.SonarDetectionSeconds;
                var update = typeof(NetworkMinefieldState).GetMethod("UpdateSonarOnServer", flags);
                foreach (var elapsed in new[] { 0.75d, 1.249999d, 1.25d, 2d })
                {
                    update.Invoke(state, new object[] { 10d + elapsed });
                    Assert.That(state.IsSonarActive(0), Is.EqualTo(elapsed < 1.25d),
                        "Elapsed seconds: " + elapsed);
                }
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void Sonar_ReplicatesMinesOnlyAfterWaveArrivalAndPreservesProgressAcrossPause()
        {
            var root = new GameObject("Sonar wave authority contract");
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning",
                BindingFlags.Static | BindingFlags.NonPublic);
            var previous = warning.GetValue(null);
            warning.SetValue(null, true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                var state = root.AddComponent<NetworkMinefieldState>();
                NetworkCountdownTestAccess.SetNetworkValue(state, "_matchActive", true);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_phase", (byte)NetworkMinefieldPhase.Running);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_roundNumber", 1);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_phaseEndsAt", 50d);
                NetworkCountdownTestAccess.SetPrivateField(state, "_serverSeed", 123UL);
                var mines = NetworkMinefieldState.GenerateMineWorldPositions(123UL, 1);
                var origin = mines[0] - Vector3.right * NetworkMinefieldState.SonarRadius;
                typeof(NetworkMinefieldState).GetMethod("SetSonarWave", flags).Invoke(state, new object[]
                {
                    0, new MinefieldSonarWaveSnapshot { Origin = origin, StartedAt = 10d }
                });
                NetworkCountdownTestAccess.SetNetworkValue(state, "_sonarMask", (byte)1);
                var ends = (double[])typeof(NetworkMinefieldState).GetField("_sonarEndsAt", flags).GetValue(state);
                ends[0] = 11.25d;
                var refresh = typeof(NetworkMinefieldState).GetMethod("RefreshSensorReplicationOnServer", flags);
                refresh.Invoke(state, new object[] { 11d });
                Assert.That(state.GetMineWorldPositions(), Is.Empty, "Clients cannot reveal the hidden layout.");
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state, true);
                foreach (var time in new[] { 10d, 10.375d, 10.74999d })
                {
                    refresh.Invoke(state, new object[] { time });
                    Assert.That(state.GetMineWorldPositions(), Has.No.Member(mines[0]), "Wave has not reached the boundary mine.");
                }
                state.PauseOnServer(10.375d);
                refresh.Invoke(state, new object[] { 99d });
                Assert.That(state.GetMineWorldPositions(), Has.No.Member(mines[0]));
                Assert.That(state.GetSonarWave(0).ElapsedAt(99d, true), Is.EqualTo(.375d));
                state.ResumeOnServer(100d);
                Assert.That(ends[0], Is.EqualTo(100.875d));
                refresh.Invoke(state, new object[] { 100.37499d });
                Assert.That(state.GetMineWorldPositions(), Has.No.Member(mines[0]));
                refresh.Invoke(state, new object[] { 100.375d });
                Assert.That(state.GetMineWorldPositions(), Has.Member(mines[0]));
                foreach (var position in state.GetMineWorldPositions())
                    Assert.That(new Vector2(position.x - origin.x, position.z - origin.z).magnitude,
                        Is.LessThanOrEqualTo(3f));
                // The visual wave vanishes at full range, but revealed mines remain
                // until the existing 1.25-second detection deadline.
                refresh.Invoke(state, new object[] { 100.87499d });
                Assert.That(state.GetMineWorldPositions(), Has.Member(mines[0]));
                typeof(NetworkMinefieldState).GetMethod("UpdateSonarOnServer", flags)
                    .Invoke(state, new object[] { 100.875d });
                refresh.Invoke(state, new object[] { 100.875d });
                Assert.That(state.GetMineWorldPositions(), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(root);
                warning.SetValue(null, previous);
            }
        }

        [Test]
        public void Sonar_RepeatedRequestsWaitForCompletionAndCancelWhenRoundEnds()
        {
            var root = new GameObject("Queued sonar authority");
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = warning.GetValue(null);
            warning.SetValue(null, true);
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            try
            {
                var state = root.AddComponent<NetworkMinefieldState>();
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state, true);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_matchActive", true);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_phase", (byte)NetworkMinefieldPhase.Running);
                var now = Time.unscaledTimeAsDouble;
                NetworkCountdownTestAccess.SetNetworkValue(state, "_phaseEndsAt", now + 100d);
                var request = typeof(NetworkMinefieldState).GetMethod("QueueOrBeginSonarOnServer", flags);
                var update = typeof(NetworkMinefieldState).GetMethod("UpdateSonarOnServer", flags);
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
                request.Invoke(state, new object[] { 0, now });
                for (var i = 1; i <= 4; i++) request.Invoke(state, new object[] { 0, now + i * .2d });
                var deadline = now + NetworkMinefieldState.SonarDetectionSeconds;
                update.Invoke(state, new object[] { deadline - .000001d });
                Assert.That(state.GetSonarWave(0).StartedAt, Is.EqualTo(now), "Clicks must not restart the active scan.");
                UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
                update.Invoke(state, new object[] { deadline });
                Assert.That(state.IsSonarActive(0), Is.True);
                Assert.That(state.GetSonarWave(0).StartedAt, Is.EqualTo(deadline));
                update.Invoke(state, new object[] { deadline + NetworkMinefieldState.SonarDetectionSeconds });
                Assert.That(state.IsSonarActive(0), Is.False, "Repeated clicks queue exactly one follow-up.");

                UnityEngine.TestTools.LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
                request.Invoke(state, new object[] { 0, now + 4d });
                request.Invoke(state, new object[] { 0, now + 4.1d });
                NetworkCountdownTestAccess.SetNetworkValue(state, "_phaseEndsAt", now + 5d);
                update.Invoke(state, new object[] { now + 5.25d });
                Assert.That(state.IsSonarActive(0), Is.False, "Queued scans cannot start after the round deadline.");
            }
            finally { Object.DestroyImmediate(root); warning.SetValue(null, previous); }
        }

        [Test]
        public void SonarWaveSnapshot_RetainsOriginAndPausedProgressAcrossSerialization()
        {
            var original = new MinefieldSonarWaveSnapshot
            {
                Origin = new Vector3(300f, 0f, -5f), StartedAt = 123456.125d, PausedElapsed = .375d
            };
            using (var writer = new FastBufferWriter(128, Unity.Collections.Allocator.Temp))
            {
                writer.WriteNetworkSerializable(original);
                using (var reader = new FastBufferReader(writer, Unity.Collections.Allocator.Temp))
                {
                    reader.ReadNetworkSerializable(out MinefieldSonarWaveSnapshot copy);
                    Assert.That(copy.Equals(original), Is.True);
                    Assert.That(copy.ElapsedAt(999999d, true), Is.EqualTo(.375d));
                    Assert.That(copy.ElapsedAt(123456.875d, false), Is.EqualTo(.75d));
                }
            }
        }

        private static void AssertAuthoritativeLayoutContract()
        {
            Assert.That(NetworkMinefieldState.GridWidth, Is.EqualTo(2));
            Assert.That(NetworkMinefieldState.MineCount, Is.EqualTo(30));
            Assert.That(NetworkMinefieldState.SonarRadius, Is.EqualTo(3f));
            Assert.That(
                NetworkMinefieldState.SonarDetectionSeconds,
                Is.EqualTo(1.25d));

            var positions = NetworkMinefieldState.GenerateMineWorldPositions(
                0x123456789ABCDEF0UL,
                1);
            Assert.That(
                NetworkMinefieldState.GenerateMineWorldPositions(
                    0x123456789ABCDEF0UL,
                    1),
                Is.EqualTo(positions));
            Assert.That(positions, Has.Length.EqualTo(30));
            for (var index = 0; index < positions.Length; index++)
            {
                Assert.That(
                    positions[index].x,
                    Is.InRange(
                        NetworkMinefieldState.ArenaMinX +
                        NetworkMinefieldState.MineSpawnHorizontalPadding,
                        NetworkMinefieldState.ArenaMaxX -
                        NetworkMinefieldState.MineSpawnHorizontalPadding));
                Assert.That(
                    positions[index].z,
                    Is.InRange(
                        NetworkMinefieldState.ArenaMinZ +
                        NetworkMinefieldState.MineSafeZoneDepth,
                        NetworkMinefieldState.ArenaMaxZ -
                        NetworkMinefieldState.MineSafeZoneDepth));
                for (var other = index + 1;
                     other < positions.Length;
                     other++)
                {
                    Assert.That(
                        Vector3.Distance(positions[index], positions[other]),
                        Is.GreaterThanOrEqualTo(
                            NetworkMinefieldState.MinimumMineSpacing -
                            0.0001f));
                }
            }

            const BindingFlags privateInstance =
                BindingFlags.Instance | BindingFlags.NonPublic;
            var stateType = typeof(NetworkMinefieldState);
            Assert.That(
                stateType.GetField("_serverSeed", privateInstance)?.FieldType,
                Is.EqualTo(typeof(ulong)),
                "The complete layout must remain server-local.");
            Assert.That(
                stateType.GetField(
                    "_detonatedMineMask",
                    privateInstance)?.FieldType,
                Is.EqualTo(typeof(ulong)));
            Assert.That(
                stateType.GetField(
                    "_cachedDetonatedMask",
                    privateInstance)?.FieldType,
                Is.EqualTo(typeof(ulong)));
            Assert.That(
                stateType.GetField(
                    "_revealedMinePositions",
                    privateInstance)?.FieldType,
                Is.EqualTo(typeof(NetworkList<Vector3>)),
                "Only sonar-authorized positions may replicate.");
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }

            for (var index = 0; index < root.childCount; index++)
            {
                var found = FindDescendant(root.GetChild(index), name);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
