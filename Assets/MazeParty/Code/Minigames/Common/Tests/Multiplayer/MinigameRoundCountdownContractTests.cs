using System;
using System.Collections.Generic;
using System.Reflection;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using MazeParty.Gameplay.Minigames.Race2;
using MazeParty.Gameplay.Minigames.OddOneOut;
using MazeParty.Gameplay.Minigames.ShellGame;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameRoundCountdownContractTests
    {
        [Test]
        public void PlayerMarkers_OnlyShowDuringFirstSecondOfEachRoundCountdown()
        {
            foreach (var sample in new[] {
                (true, 3d, true), (true, 2.001d, true), (true, 2d, false),
                (true, 1d, false), (true, 0d, false), (false, 3d, false),
                (true, double.NaN, false), (true, double.PositiveInfinity, false),
                (true, 3d, true) })
                Assert.That(MinigameLocalPlayerHighlight.ShouldShowMarkers(sample.Item1, sample.Item2),
                    Is.EqualTo(sample.Item3), sample.ToString());
        }

        [Test]
        public void FlightAdapter_ExposesOnlyTheInitialCountdown()
        {
            var root = new GameObject("Flight countdown contract");
            var previous = NetworkFlightState.Instance;
            try
            {
                var state = root.AddComponent<NetworkFlightState>();
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkFlightState), state);
                var adapter = new FlightRuntimeAdapter();
                foreach (var phase in new[] { MazeParty.Gameplay.Minigames.Flight.FlightPhase.Countdown,
                    MazeParty.Gameplay.Minigames.Flight.FlightPhase.Running, MazeParty.Gameplay.Minigames.Flight.FlightPhase.Result })
                {
                    NetworkCountdownTestAccess.SetNetworkValue(state, "_snapshot", new FlightSnapshot {
                        Phase = phase, EndsAt = Time.unscaledTimeAsDouble + 3 });
                    Assert.That(adapter.TryGetRoundCountdown(out var remaining),
                        Is.EqualTo(phase == MazeParty.Gameplay.Minigames.Flight.FlightPhase.Countdown));
                }
            }
            finally
            {
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkFlightState), previous);
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Race2Hud_ConfiguredDurationSurvivesCountdownRunningAndRoundRestart()
        {
            var previousMatch = NetworkMatchState.Instance;
            var previousRace = NetworkRace2State.Instance;
            var warning = typeof(NetworkVariableBase).GetField(
                "IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            var previousWarning = warning.GetValue(null);
            var root = new GameObject("Race2 configured HUD contract");
            GameObject hudRoot = null;
            try
            {
                warning.SetValue(null, true);
                var match = root.AddComponent<NetworkMatchState>();
                var state = root.AddComponent<NetworkRace2State>();
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkMatchState), match);
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkRace2State), state);
                NetworkCountdownTestAccess.SetNetworkValue(
                    match, "_flowState", (byte)BoardFlowState.MinigamePlaying);
                NetworkCountdownTestAccess.SetNetworkValue(
                    match, "_currentMinigame", (byte)ScheduledMinigameId.Race2);
                var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab");
                Assert.That(prefab, Is.Not.Null);
                hudRoot = UnityEngine.Object.Instantiate(prefab);
                var hud = hudRoot.GetComponent<MinigameCommonHudView>();
                Assert.That(hud.HasRequiredReferences, Is.True);

                foreach (var duration in new[] { (30, "00:30", "00:15"), (180, "03:00", "01:30") })
                {
                    Assert.That(MatchGameSettings.Default.TryChangeGame(
                        ScheduledMinigameId.Race2,
                        new MinigameSetting(true, 3, duration.Item1),
                        out var settings), Is.True);
                    typeof(NetworkMatchState).GetProperty("GameSettings").SetValue(match, settings);
                    foreach (var sample in new[] {
                        (Race2Phase.Countdown, 1, 3d, duration.Item2, 1f),
                        (Race2Phase.Running, 1, (double)duration.Item1, duration.Item2, 1f),
                        (Race2Phase.Running, 1, duration.Item1 / 2d, duration.Item3, .5f),
                        (Race2Phase.Running, 1, 0d, "00:00", 0f),
                        (Race2Phase.RoundResult, 1, 4d, "00:00", 0f),
                        (Race2Phase.Countdown, 2, 3d, duration.Item2, 1f) })
                    {
                        // Freeze the snapshot clock so assertions do not depend on editor timing.
                        NetworkCountdownTestAccess.SetNetworkValue(state, "_snapshot", new Race2Snapshot {
                            Phase = sample.Item1, Round = (byte)sample.Item2, AliveMask = 15,
                            Paused = true, PausedRemaining = sample.Item3
                        });
                        NetworkCountdownTestAccess.InvokePrivate(hud, "Update");
                        var context = duration.Item1 + "s " + sample.Item1 + " round " + sample.Item2;
                        Assert.That(hud.TimerDial.gameObject.activeSelf, Is.True, context);
                        Assert.That(hud.TimerDial.TimeText.text, Is.EqualTo(sample.Item4), context);
                        Assert.That(hud.TimerDial.Ring.FillAmount,
                            Is.EqualTo(sample.Item5).Within(0.000001f), context);
                    }

                    NetworkCountdownTestAccess.SetNetworkValue(
                        state, "_snapshot", new Race2Snapshot { Phase = Race2Phase.Complete });
                    NetworkCountdownTestAccess.InvokePrivate(hud, "Update");
                    Assert.That(hud.TimerDial.gameObject.activeSelf, Is.False);
                }
            }
            finally
            {
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkMatchState), previousMatch);
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkRace2State), previousRace);
                if (hudRoot != null) UnityEngine.Object.DestroyImmediate(hudRoot);
                UnityEngine.Object.DestroyImmediate(root);
                warning.SetValue(null, previousWarning);
            }
        }


        [Test]
        public void OddOneOutHud_SeparatesInitialCountdownFromEverySelection_AndRestoresPausedTime()
        {
            var previousMatch = NetworkMatchState.Instance;
            var previousOdd = NetworkOddOneOutState.Instance;
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            var previousWarning = warning.GetValue(null);
            var root = new GameObject("OddOneOut timer contract");
            try
            {
                warning.SetValue(null, true);
                var match = root.AddComponent<NetworkMatchState>();
                var state = root.AddComponent<NetworkOddOneOutState>();
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkMatchState), match);
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkOddOneOutState), state);
                NetworkCountdownTestAccess.SetNetworkValue(match, "_gameplayEnabled", true);
                NetworkCountdownTestAccess.SetNetworkValue(match, "_flowState", (byte)BoardFlowState.MinigamePlaying);
                NetworkCountdownTestAccess.SetNetworkValue(match, "_currentMinigame", (byte)ScheduledMinigameId.OddOneOut);
                var common = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab"), root.transform).GetComponent<MinigameCommonHudView>();
                var action = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Minigames/OddOneOut/UI/OddOneOutHud.prefab"), root.transform).GetComponent<OddOneOutHudBindings>();
                var score = UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab"), root.transform).GetComponent<BouncingBallsHudBindings>();
                var presentation = new GameObject("Presentation"); presentation.transform.SetParent(root.transform);
                var anchors = new Transform[4];
                for (var i = 0; i < 4; i++) { anchors[i] = new GameObject("Anchor " + i).transform; anchors[i].SetParent(root.transform); }
                var view = root.AddComponent<OddOneOutNetworkView>();
                view.Configure(state, null, presentation, presentation.transform, anchors, score, action, null);
                var timerRoot = (GameObject)new UnityEditor.SerializedObject(action).FindProperty("selectionTimerRoot").objectReferenceValue;
                var bar = (UnityEngine.UI.Slider)new UnityEditor.SerializedObject(action).FindProperty("selectionTimeBar").objectReferenceValue;
                var adapter = new OddOneOutRuntimeAdapter();
                foreach (var sample in new[] {
                    (OddOneOutPhase.Countdown, 1, 3d, false, 0f),
                    (OddOneOutPhase.Choosing, 1, 5d, true, 1f),
                    (OddOneOutPhase.Choosing, 1, 2.5d, true, .5f),
                    (OddOneOutPhase.Choosing, 1, 0d, true, 0f),
                    (OddOneOutPhase.Reveal, 1, 2d, false, 0f),
                    (OddOneOutPhase.Choosing, 2, 5d, true, 1f),
                    (OddOneOutPhase.Complete, 2, 0d, false, 0f) })
                {
                    // A reconnect snapshot must restore its frozen clock without a local timer restart.
                    NetworkCountdownTestAccess.SetNetworkValue(state, "_snapshot", new OddOneOutPublicSnapshot {
                        Phase = sample.Item1, Round = (byte)sample.Item2, Winner = -1,
                        Paused = true, PausedRemaining = sample.Item3 });
                    Assert.That(adapter.TryGetRoundCountdown(out var countdown),
                        Is.EqualTo(sample.Item1 == OddOneOutPhase.Countdown));
                    Assert.That(match.IsMinigameRoundCountdown, Is.EqualTo(sample.Item1 == OddOneOutPhase.Countdown));
                    NetworkCountdownTestAccess.InvokePrivate(common, "Update");
                    Assert.That(common.TimerDial.gameObject.activeSelf, Is.False);
                    var overlay = common.GetComponent<MinigameStartCountdownView>();
                    overlay.RefreshForPlayer(match, 0);
                    Assert.That(overlay.IsVisible, Is.EqualTo(sample.Item1 == OddOneOutPhase.Countdown));
                    NetworkCountdownTestAccess.InvokePrivate(view, "Render", state.Phase, state.RoundNumber, -1,
                        (Func<int, bool>)(slot => false), (Func<int, int>)(slot => 0),
                        0, false, state.Remaining, null);
                    Assert.That(timerRoot.activeSelf, Is.EqualTo(sample.Item4), sample.ToString());
                    if (sample.Item4) Assert.That(bar.value, Is.EqualTo(sample.Item5).Within(1e-6), sample.ToString());
                    if (sample.Item1 == OddOneOutPhase.Choosing)
                    {
                        Assert.That(adapter.TryGetClock(0d, out var remaining, out var total, out var round), Is.True);
                        Assert.That(total, Is.EqualTo(5d)); Assert.That(remaining, Is.EqualTo(sample.Item3));
                        Assert.That(round, Is.EqualTo(sample.Item2));
                    }
                }
            }
            finally
            {
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkMatchState), previousMatch);
                NetworkCountdownTestAccess.SetInstance(typeof(NetworkOddOneOutState), previousOdd);
                UnityEngine.Object.DestroyImmediate(root); warning.SetValue(null, previousWarning);
            }
        }

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
                    typeof(NetworkRace2State), typeof(Race2RuntimeAdapter),
                    (byte)Race2Phase.Countdown);
                yield return new CountdownAdapterCase(
                    typeof(NetworkShellGameState), typeof(ShellGameRuntimeAdapter),
                    (byte)ShellGamePhase.Countdown);
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
                    typeof(NetworkCoinCollectState), typeof(CoinCollectRuntimeAdapter),
                    (byte)NetworkCoinCollectPhase.Countdown);
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
                if (state is NetworkRace2State)
                    NetworkCountdownTestAccess.SetNetworkValue(state, "_snapshot", new Race2Snapshot
                    {
                        Phase = Race2Phase.Countdown, Round = 2, AliveMask = 15,
                        EndsAt = Time.unscaledTimeAsDouble + 30d
                    });
                else if(state is NetworkShellGameState)
                    NetworkCountdownTestAccess.SetNetworkValue(state, "_snapshot", new ShellGamePublicSnapshot
                    {
                        Phase = ShellGamePhase.Countdown, Round = 2,
                        EndsAt = Time.unscaledTimeAsDouble + 30d
                    });
                else
                {
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
                }

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
                if(state is NetworkShellGameState)
                {
                    NetworkCountdownTestAccess.SetNetworkValue(state, "_snapshot", new ShellGamePublicSnapshot
                    {
                        Phase = ShellGamePhase.Choosing, Round = 2,
                        EndsAt = Time.unscaledTimeAsDouble + 10d
                    });
                    Assert.That(adapter.TryGetRoundCountdown(out _), Is.False,
                        "The selection timer must not activate the shared 3-2-1 countdown.");
                }
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
