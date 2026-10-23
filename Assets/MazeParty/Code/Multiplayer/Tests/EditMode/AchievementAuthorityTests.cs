using System;
using System.Reflection;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class AchievementAuthorityTests
    {
        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
        private GameObject _root;
        private NetworkMatchState _match, _previous;
        private AchievementMatchProgress _progress;
        private bool _previousWarning;
        [SetUp] public void SetUp()
        {
            var warning = typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic);
            _previousWarning = (bool)warning.GetValue(null); warning.SetValue(null, true);
            _previous = NetworkMatchState.Instance;
            _root = new GameObject("Achievement authority fixture");
            _match = _root.AddComponent<NetworkMatchState>();
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, _match);
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(_match, true);
            typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(_match, true);
            Variable(_match, "_gameplayEnabled", true);
            Field("_achievementMatchId", Guid.NewGuid().ToString("N"));
            _progress = new AchievementMatchProgress();
            ((AchievementMatchProgress[])typeof(NetworkMatchState).GetField("_achievementPlayers", Private).GetValue(_match))[0] = _progress;
        }
        [TearDown] public void TearDown()
        {
            typeof(NetworkMatchState).GetProperty("Instance").SetValue(null, _previous);
            foreach (var behaviour in _root.GetComponentsInChildren<NetworkBehaviour>())
                typeof(NetworkBehaviour).GetProperty("IsSpawned").SetValue(behaviour, false);
            UnityEngine.Object.DestroyImmediate(_root);
            typeof(NetworkVariableBase).GetField("IgnoreInitializeWarning", BindingFlags.Static | BindingFlags.NonPublic).SetValue(null, _previousWarning);
        }
        [Test] public void AchievementEvents_RequireServerLiveEligibleMatch_AndFreezeAfterFinalReceipt()
        {
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(_match, false);
            _match.RecordAchievementOnServer(0, PartyAchievement.Swap);
            Assert.That(_progress.flags, Is.Zero);
            typeof(NetworkBehaviour).GetProperty("IsServer").SetValue(_match, true);
            Variable(_match, "_gameplayEnabled", false);
            _match.RecordAchievementOnServer(0, PartyAchievement.Swap);
            Assert.That(_progress.flags, Is.Zero);
            Variable(_match, "_gameplayEnabled", true);
            _match.RecordAchievementOnServer(-1, PartyAchievement.Swap);
            _match.RecordAchievementOnServer(4, PartyAchievement.Swap);
            Assert.That(_progress.flags, Is.Zero);
            _match.RecordAchievementOnServer(0, PartyAchievement.Swap);
            Assert.That(_progress.flags, Is.EqualTo(PartyAchievements.Bit(PartyAchievement.Swap)));
            Field("_achievementReceipts", new string[4]);
            _match.RecordAchievementOnServer(0, PartyAchievement.BoardWinner);
            Assert.That(_progress.flags, Is.EqualTo(PartyAchievements.Bit(PartyAchievement.Swap)));
            Field("_achievementReceipts", null);
            typeof(NetworkMatchState).GetMethod("BeginAchievementsOnServer", Private).Invoke(_match, null);
            _match.RecordAchievementOnServer(0, PartyAchievement.BoardWinner);
            Assert.That(_progress.flags, Is.EqualTo(PartyAchievements.Bit(PartyAchievement.Swap)), "Editor must not create an eligible match.");
        }
        [Test] public void MinigameEvents_RejectWrongGameAndNonPlayingPhase()
        {
            Variable(_match, "_currentMinigame", (byte)ScheduledMinigameId.GiftGrab);
            Variable(_match, "_flowState", (byte)BoardFlowState.MinigamePlaying);
            _match.RecordStolenGiftsOnServer(0, 3);
            Assert.That(_progress.stolenGifts, Is.EqualTo(3));
            _match.RecordMinigameAchievementOnServer(0, ScheduledMinigameId.WrongWay, PartyAchievement.PerfectStairs);
            Assert.That(_progress.flags, Is.Zero);
            Variable(_match, "_currentMinigame", (byte)ScheduledMinigameId.WrongWay);
            _match.RecordMinigameAchievementOnServer(0, ScheduledMinigameId.WrongWay, PartyAchievement.PerfectStairs);
            Assert.That(_progress.flags, Is.EqualTo(PartyAchievements.Bit(PartyAchievement.PerfectStairs)));
            Variable(_match, "_flowState", (byte)BoardFlowState.MinigameResult);
            _match.RecordMinigameAchievementOnServer(0, ScheduledMinigameId.WrongWay, PartyAchievement.PerfectBalloon);
            Assert.That(_progress.flags, Is.EqualTo(PartyAchievements.Bit(PartyAchievement.PerfectStairs)));
        }
        private void Field(string name, object value) => typeof(NetworkMatchState).GetField(name, Private).SetValue(_match, value);
        private static void Variable<T>(object target, string name, T value) where T : unmanaged =>
            ((NetworkVariable<T>)target.GetType().GetField(name, Private).GetValue(target)).Value = value;
    }
}
