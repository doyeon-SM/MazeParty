using System.Reflection;
using MazeParty.Gameplay.Minigames.OddOneOut;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class OddOneOutAuthorityTests
    {
        private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
        [Test]
        public void PrivateChoices_AreOwnerOnly_PublicRevealSerializesAtomically()
        {
            var root = new GameObject("OddOneOut privacy contract");
            try
            {
                var avatar = root.AddComponent<NetworkPlayerAvatar>();
                var choice = (NetworkVariableBase)typeof(NetworkPlayerAvatar).GetField("_oddOneOutChoice", Flags).GetValue(avatar);
                Assert.That(choice.ReadPerm, Is.EqualTo(NetworkVariableReadPermission.Owner));
                foreach (var phase in new[] { OddOneOutPhase.Choosing, OddOneOutPhase.Reveal })
                {
                    var source = new OddOneOutPublicSnapshot { Phase = phase, Round = 2, Epoch = 7,
                        RevealedMask = phase == OddOneOutPhase.Reveal ? (byte)7 : (byte)0,
                        Winner = 3, Score3 = 2, EndsAt = 8d };
                    using (var writer = new FastBufferWriter(128, Unity.Collections.Allocator.Temp))
                    {
                        writer.WriteNetworkSerializable(source);
                        using (var reader = new FastBufferReader(writer, Unity.Collections.Allocator.Temp))
                        {
                            reader.ReadNetworkSerializable(out OddOneOutPublicSnapshot restored);
                            Assert.That(restored.Equals(source), Is.True);
                        }
                    }
                }
            }
            finally { Object.DestroyImmediate(root); }
        }
        [Test]
        public void PauseResume_PreservesPrivateChoiceAndRevealDuration_AndInvalidatesOldInputs()
        {
            var root = new GameObject("OddOneOut pause contract");
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = warning.GetValue(null); warning.SetValue(null, true);
            var state = root.AddComponent<NetworkOddOneOutState>();
            try
            {
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state, true);
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state, true);
                var simulation = new OddOneOutMatchState(); simulation.TrySetChoice(0, true);
                NetworkCountdownTestAccess.SetPrivateField(state, "_simulation", simulation);
                NetworkCountdownTestAccess.SetPrivateField(state, "_startedAt", 0d);
                typeof(NetworkOddOneOutState).GetMethod("Publish", Flags).Invoke(state, new object[] { false, true });
                var snapshot = (NetworkVariable<OddOneOutPublicSnapshot>)typeof(NetworkOddOneOutState).GetField("_snapshot", Flags).GetValue(state);
                var epoch = state.InputEpoch;
                Assert.That(snapshot.Value.RevealedMask, Is.Zero);
                state.PauseOnServer(1.25d);
                Assert.That(state.Remaining, Is.EqualTo(1.75d));
                Assert.That(state.InputEpoch, Is.Not.EqualTo(epoch));
                Assert.That(state.CanAcceptInputForSlot(0), Is.False);
                epoch = state.InputEpoch; state.ResumeOnServer(101.25d);
                Assert.That(state.InputEpoch, Is.Not.EqualTo(epoch));
                Assert.That(simulation.GetChoice(0), Is.True);
                Assert.That(snapshot.Value.EndsAt, Is.EqualTo(103d));
                Assert.That(snapshot.Value.RevealedMask, Is.Zero);
                var advance = typeof(NetworkOddOneOutState).GetMethod("Advance", Flags);
                advance.Invoke(state, new object[] { 103d });
                Assert.That(state.Phase, Is.EqualTo(OddOneOutPhase.Reveal));
                Assert.That(snapshot.Value.RevealedMask, Is.EqualTo(1));
                Assert.That(state.GetScore(0), Is.EqualTo(1));
                state.PauseOnServer(103.5d); state.ResumeOnServer(203.5d);
                Assert.That(snapshot.Value.EndsAt, Is.EqualTo(205d));
                advance.Invoke(state, new object[] { 204.99999d });
                Assert.That(state.Phase, Is.EqualTo(OddOneOutPhase.Reveal));
                advance.Invoke(state, new object[] { 205d });
                Assert.That(state.RoundNumber, Is.EqualTo(2));
                Assert.That(simulation.GetChoice(0), Is.False);
                Assert.That(snapshot.Value.RevealedMask, Is.Zero);
                Assert.That(state.GetScore(0), Is.EqualTo(1));
            }
            finally
            {
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state, false);
                Object.DestroyImmediate(root); warning.SetValue(null, previous);
            }
        }
        [Test]
        public void InputAuthority_RejectsStaleDuplicatePausedLateAndNonServerRequests()
        {
            var root = new GameObject("OddOneOut authority contract");
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = warning.GetValue(null); warning.SetValue(null, true);
            var state = root.AddComponent<NetworkOddOneOutState>();
            try
            {
                NetworkCountdownTestAccess.SetPrivateField(state, "_simulation", new OddOneOutMatchState());
                var snapshot = new OddOneOutPublicSnapshot { Phase = OddOneOutPhase.Choosing, Round = 1, Epoch = 7, EndsAt = Time.unscaledTimeAsDouble + 3d };
                NetworkCountdownTestAccess.SetNetworkValue(state, "_snapshot", snapshot);
                var gate = typeof(NetworkOddOneOutState).GetMethod("ValidateEnvelope", Flags);
                Assert.That(gate.Invoke(state, new object[] { 1, 7U, 1U, 0 }), Is.False);
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state, true);
                Assert.That(gate.Invoke(state, new object[] { 1, 7U, 1U, 0 }), Is.False);
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state, true);
                foreach (var sample in new[] { (1, 7U, 1U, 0, true), (2, 7U, 1U, 0, false), (1, 6U, 1U, 0, false),
                    (1, 0U, 1U, 0, false), (1, 7U, 0U, 0, false), (1, 7U, 1U, -1, false), (1, 7U, 1U, 4, false) })
                    Assert.That(gate.Invoke(state, new object[] { sample.Item1, sample.Item2, sample.Item3, sample.Item4 }), Is.EqualTo(sample.Item5));
                ((uint[])typeof(NetworkOddOneOutState).GetField("_sequences", Flags).GetValue(state))[0] = 2;
                Assert.That(gate.Invoke(state, new object[] { 1, 7U, 2U, 0 }), Is.False);
                snapshot.Paused = true; NetworkCountdownTestAccess.SetNetworkValue(state, "_snapshot", snapshot);
                Assert.That(gate.Invoke(state, new object[] { 1, 7U, 3U, 0 }), Is.False);
                snapshot.Paused = false; snapshot.EndsAt = Time.unscaledTimeAsDouble;
                NetworkCountdownTestAccess.SetNetworkValue(state, "_snapshot", snapshot);
                Assert.That(gate.Invoke(state, new object[] { 1, 7U, 3U, 0 }), Is.False);
                Assert.That(state.TrySetChoiceOnServer(null, true, 1, 7, 3), Is.False);
            }
            finally
            {
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state, false);
                Object.DestroyImmediate(root); warning.SetValue(null, previous);
            }
        }
    }
}
