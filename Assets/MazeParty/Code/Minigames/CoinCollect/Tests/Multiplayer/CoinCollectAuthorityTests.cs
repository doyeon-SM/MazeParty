using System;
using System.Reflection;
using MazeParty.Gameplay.Minigames.CoinCollect;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class CoinCollectAuthorityTests
    {
        [Test]
        public void ItemSnapshot_PreservesProtectionDeadlineAcrossNetworkSerialization()
        {
            foreach (var unlockAt in new[] { 0d, 11d })
            {
                var source = new CoinCollectItemNetworkSnapshot
                {
                    Position = new Vector2(702.3f, 0f), BornAt = 10d, InitialHeight = 2.5f,
                    Kind = (byte)CoinCollectItemKind.Money, Active = true, PickupUnlockAt = unlockAt
                };
                using (var writer = new FastBufferWriter(128, Unity.Collections.Allocator.Temp))
                {
                    writer.WriteNetworkSerializable(source);
                    using (var reader = new FastBufferReader(writer, Unity.Collections.Allocator.Temp))
                    {
                        reader.ReadNetworkSerializable(out CoinCollectItemNetworkSnapshot restored);
                        Assert.That(restored.Equals(source), Is.True);
                        var item = restored.ToPresentation();
                        Assert.That(item.IsPickupLockedAt(10.999d), Is.EqualTo(unlockAt > 0d));
                        Assert.That(item.IsPickupLockedAt(11d), Is.False);
                        Assert.That(item.Consumed().PickupUnlockAt, Is.EqualTo(unlockAt));
                    }
                }
            }
        }

        [Test]
        public void InputEnvelope_RejectsClientsStaleEpochsWrongRoundsPausedAndExpiredPlay()
        {
            var root = new GameObject("CoinCollect authority contract");
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            var previous = warning.GetValue(null); warning.SetValue(null, true);
            try
            {
                var state = root.AddComponent<NetworkCoinCollectState>();
                NetworkCountdownTestAccess.SetPrivateField(state, "_simulation", new CoinCollectMatchState(123));
                NetworkCountdownTestAccess.SetNetworkValue(state, "_phase", (byte)NetworkCoinCollectPhase.Running);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_roundNumber", (byte)1);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_inputEpoch", 7U);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_phaseEndsAt", Time.unscaledTimeAsDouble + 60d);
                var gate = typeof(NetworkCoinCollectState).GetMethod("ValidateEnvelope", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(gate.Invoke(state, new object[] { 1, 7U }), Is.False);
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(state, true);
                Assert.That(gate.Invoke(state, new object[] { 1, 7U }), Is.False, "A spawned client cannot run authority.");
                typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(state, true);
                foreach (var sample in new[] { (1, 7U, true), (0, 7U, false), (2, 7U, false), (1, 0U, false), (1, 6U, false), (1, 8U, false) })
                    Assert.That(gate.Invoke(state, new object[] { sample.Item1, sample.Item2 }), Is.EqualTo(sample.Item3));
                NetworkCountdownTestAccess.SetNetworkValue(state, "_paused", true);
                Assert.That(gate.Invoke(state, new object[] { 1, 7U }), Is.False);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_paused", false);
                NetworkCountdownTestAccess.SetNetworkValue(state, "_phaseEndsAt", Time.unscaledTimeAsDouble - 1d);
                Assert.That(gate.Invoke(state, new object[] { 1, 7U }), Is.False);
                Assert.That(state.ReceiveInputOnServer(null, Vector2.one, 1, 7U), Is.False);
                Assert.That(state.TryPrimaryActionOnServer(null, 1, 7U), Is.False);
            }
            finally
            {
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(root.GetComponent<NetworkCoinCollectState>(), false);
                UnityEngine.Object.DestroyImmediate(root); warning.SetValue(null, previous);
            }
        }
    }
}
