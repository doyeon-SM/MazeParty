using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameRoundCountdownContractTests
    {
        private static IEnumerable<CountdownAdapterCase>
            MultiRoundAdapters
        {
            get
            {
                yield return new CountdownAdapterCase(
                    typeof(NetworkMinefieldState),
                    typeof(MinefieldRuntimeAdapter),
                    (byte)NetworkMinefieldPhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkWrongWayState),
                    typeof(WrongWayRuntimeAdapter),
                    (byte)NetworkWrongWayPhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkRedLightGreenLightState),
                    typeof(RedLightGreenLightRuntimeAdapter),
                    (byte)NetworkRedLightGreenLightPhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkStableFootingState),
                    typeof(StableFootingRuntimeAdapter),
                    (byte)NetworkStableFootingPhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkBalloonBlowState),
                    typeof(BalloonBlowRuntimeAdapter),
                    (byte)NetworkBalloonBlowPhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkGiftGrabState),
                    typeof(GiftGrabRuntimeAdapter),
                    (byte)NetworkGiftGrabPhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkTagChaseState),
                    typeof(TagChaseRuntimeAdapter),
                    (byte)NetworkTagChasePhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkRaceState),
                    typeof(RaceRuntimeAdapter),
                    (byte)NetworkRacePhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkBouncingBallsState),
                    typeof(BouncingBallsRuntimeAdapter),
                    (byte)NetworkBouncingBallsPhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkSnowySpinState),
                    typeof(SnowySpinRuntimeAdapter),
                    (byte)NetworkSnowySpinPhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkSequenceMemoryState),
                    typeof(SequenceMemoryRuntimeAdapter),
                    (byte)NetworkSequenceMemoryPhase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkCliffBarrageState),
                    typeof(CliffBarrageRuntimeAdapter),
                    (byte)NetworkCliffBarragePhase.Countdown);
            }
        }

        [TestCaseSource(nameof(MultiRoundAdapters))]
        public void Adapter_ExposesCountdownForSecondRound(
            CountdownAdapterCase testCase)
        {
            var root = new GameObject(
                testCase.StateType.Name + " Countdown Contract");
            object previousInstance = null;
            try
            {
                var state = (NetworkBehaviour)root.AddComponent(
                    testCase.StateType);
                previousInstance = NetworkCountdownTestAccess.GetInstance(
                    testCase.StateType);
                NetworkCountdownTestAccess.SetInstance(
                    testCase.StateType,
                    state);
                NetworkCountdownTestAccess.SetNetworkValue(
                    state,
                    "_phase",
                    testCase.CountdownPhase);
                NetworkCountdownTestAccess.SetRoundNumber(state, 2);
                NetworkCountdownTestAccess.TrySetNetworkValue(
                    state,
                    "_matchActive",
                    true);
                NetworkCountdownTestAccess.TrySetNetworkValue(
                    state,
                    "_paused",
                    false);
                NetworkCountdownTestAccess.SetNetworkValue(
                    state,
                    "_phaseEndsAt",
                    Time.unscaledTimeAsDouble + 30d);

                var adapter = (IMinigameRuntimeAdapter)
                    Activator.CreateInstance(testCase.AdapterType, true);

                Assert.That(
                    adapter.TryGetRoundCountdown(out var remaining),
                    Is.True,
                    testCase.AdapterType.Name);
                Assert.That(
                    remaining,
                    Is.GreaterThan(0d).And.LessThanOrEqualTo(30.001d),
                    testCase.AdapterType.Name);
            }
            finally
            {
                NetworkCountdownTestAccess.SetInstance(
                    testCase.StateType,
                    previousInstance);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        public sealed class CountdownAdapterCase
        {
            public CountdownAdapterCase(
                Type stateType,
                Type adapterType,
                byte countdownPhase)
            {
                StateType = stateType;
                AdapterType = adapterType;
                CountdownPhase = countdownPhase;
            }

            public Type StateType { get; }
            public Type AdapterType { get; }
            public byte CountdownPhase { get; }

            public override string ToString()
            {
                return AdapterType.Name;
            }
        }
    }

    internal static class NetworkCountdownTestAccess
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags PublicStatic =
            BindingFlags.Static | BindingFlags.Public;

        public static object GetInstance(Type stateType)
        {
            return GetInstanceProperty(stateType).GetValue(null);
        }

        public static void SetInstance(Type stateType, object value)
        {
            GetInstanceProperty(stateType)
                .GetSetMethod(true)
                .Invoke(null, new[] { value });
        }

        public static void SetPrivateField(
            object target,
            string fieldName,
            object value)
        {
            var field = target.GetType().GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        public static void SetNetworkValue<T>(
            object target,
            string fieldName,
            T value)
        {
            var variable = GetNetworkVariable<T>(target, fieldName);
            variable.Value = value;
        }

        public static void SetRoundNumber(object target, int roundNumber)
        {
            var field = target.GetType().GetField(
                "_roundNumber",
                PrivateInstance);
            Assert.That(field, Is.Not.Null, "_roundNumber");
            var value = field.GetValue(target);
            if (value is NetworkVariable<byte> byteVariable)
            {
                byteVariable.Value = checked((byte)roundNumber);
                return;
            }
            if (value is NetworkVariable<int> intVariable)
            {
                intVariable.Value = roundNumber;
                return;
            }

            Assert.Fail(
                target.GetType().Name +
                " uses an unsupported round-number network type.");
        }

        public static void TrySetNetworkValue<T>(
            object target,
            string fieldName,
            T value)
        {
            var field = target.GetType().GetField(
                fieldName,
                PrivateInstance);
            if (field?.GetValue(target) is NetworkVariable<T> variable)
            {
                variable.Value = value;
            }
        }

        public static T GetNetworkValue<T>(
            object target,
            string fieldName)
        {
            return GetNetworkVariable<T>(target, fieldName).Value;
        }

        public static void InvokePrivate(
            object target,
            string methodName,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                methodName,
                PrivateInstance);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(target, arguments);
        }

        private static PropertyInfo GetInstanceProperty(Type stateType)
        {
            var property = stateType.GetProperty(
                "Instance",
                PublicStatic);
            Assert.That(property, Is.Not.Null, stateType.Name);
            Assert.That(property.GetSetMethod(true), Is.Not.Null,
                stateType.Name);
            return property;
        }

        private static NetworkVariable<T> GetNetworkVariable<T>(
            object target,
            string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null, fieldName);
            var variable = field.GetValue(target) as NetworkVariable<T>;
            Assert.That(variable, Is.Not.Null, fieldName);
            return variable;
        }
    }
}
