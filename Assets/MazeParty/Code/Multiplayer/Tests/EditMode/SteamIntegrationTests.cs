using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class SteamIntegrationTests
    {
        private string _directory;
        [SetUp] public void Setup() => _directory = Path.Combine(Path.GetTempPath(), "MazeParty-Steam-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void Cleanup() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
        private AchievementJournal Journal() => new AchievementJournal(Path.Combine(_directory, "account.json"), "steam-test");
        private static AchievementReceipt Receipt() => new AchievementReceipt
        {
            matchId = Guid.NewGuid().ToString("N"), playerKey = "player", mapId = "forest-graybox",
            turns = 10, rank = 1, finalKeys = 0, progress = new AchievementMatchProgress()
        };
        [Test] public void StoreAcknowledgement_DoesNotConsumeNewerReceipts_AndFailedStoresRetryAfterRestart()
        {
            var journal = Journal(); journal.Commit(Receipt(), "player");
            var api = new FakeApi(); var sync = new SteamAchievementSync(journal, api);
            Assert.That(sync.Tick(0), Is.True);
            journal.Commit(Receipt(), "player");
            sync.Complete(true);
            Assert.That(journal.Snapshot.confirmedValues[0], Is.EqualTo(1));
            Assert.That(journal.Pending, Is.Not.Zero);
            Assert.That(sync.Tick(59.999), Is.True);
            Assert.That(api.Stores, Is.EqualTo(1));
            sync.Tick(60); sync.Complete(false);
            Assert.That(Journal().Snapshot.confirmedValues[0], Is.EqualTo(1));
            var restarted = new SteamAchievementSync(Journal(), api);
            restarted.Tick(0); restarted.Complete(true);
            Assert.That(Journal().Pending, Is.Zero);
            Assert.That(Journal().Snapshot.confirmedValues[0], Is.EqualTo(2));
        }
        [Test] public void Timeout_DoesNotLetLateCallbacksConfirmAnotherBatch()
        {
            var journal = Journal(); journal.Commit(Receipt(), "player");
            var api = new FakeApi(); var sync = new SteamAchievementSync(journal, api);
            sync.Tick(0);
            Assert.That(sync.Tick(29.999), Is.True);
            Assert.That(sync.Tick(30), Is.False);
            sync.Complete(true); sync.Tick(120);
            Assert.That(api.Stores, Is.EqualTo(1));
            Assert.That(journal.Pending, Is.Not.Zero);
            var restart = new SteamAchievementSync(journal, api);
            restart.Tick(0); restart.Complete(true);
            Assert.That(Journal().Pending, Is.Zero);
        }
        [Test] public void PublishedSchemaRequired_RemoteProgressNeverDecreases_AndThresholdsUnlockBothTiers()
        {
            var journal = Journal(); var receipt = Receipt(); receipt.progress.keysBought = 12;
            journal.Commit(receipt, "player");
            var api = new FakeApi { Missing = true }; var sync = new SteamAchievementSync(journal, api);
            Assert.That(sync.Tick(0), Is.False);
            Assert.That(api.Stores, Is.Zero);
            Assert.That(api.Writes, Is.Zero);
            api.Missing = false; api.Stats["MP_ACH_01"] = 5;
            sync.Tick(60);
            Assert.That(api.Stats["MP_ACH_01"], Is.EqualTo(5));
            Assert.That(api.Stats["MP_ACH_07"], Is.EqualTo(12));
            foreach (var name in new[] { "MP_ACH_01_1", "MP_ACH_01_5", "MP_ACH_07_1", "MP_ACH_07_10" })
                Assert.That(api.Unlocked, Does.Contain(name));
            sync.Complete(true);
            Assert.That(journal.Snapshot.matches, Is.EqualTo(1), "Remote stats must not invent local match receipts.");
        }
        [Test] public void Invites_RejectForeignScopeAndCommands_QueueOnlyWhileOutsideRooms_ExpireAtBoundary()
        {
            var valid = SteamInvite.Create("production", "0.0.56", "AB12CD");
            Assert.That(SteamInvite.TryRead(valid, "production", "0.0.56", out var code), Is.True);
            Assert.That(code, Is.EqualTo("AB12CD"));
            foreach (var value in new[] { null, "", valid + " --extra", valid + "\n", "+connect_lobby 123",
                "+mp_join staging/0.0.56/AB12CD", "+mp_join production/0.0.55/AB12CD", "+mp_join production/0.0.56/abc", "+mp_join production/0.0.56/AB;CD" })
                Assert.That(SteamInvite.TryRead(value, "production", "0.0.56", out _), Is.False, value);
            Assert.That(SteamInvite.FromArguments(new[] { "game.exe", "+mp_join", "production/0.0.56/AB12CD" }), Is.EqualTo(valid));
            Assert.That(SteamInvite.FromArguments(new[] { "game.exe", "+mp_join", "x", "+mp_join", "y" }), Is.Null);
            var inbox = new SteamInviteInbox();
            Assert.That(inbox.Accept(valid, "production", "0.0.56", true, 0), Is.False);
            inbox.Accept(valid, "production", "0.0.56", false, 0);
            Assert.That(inbox.TryTake(false, false, false, 1, out _), Is.False);
            Assert.That(inbox.TryTake(true, true, false, 2, out _), Is.False);
            Assert.That(inbox.TryTake(true, false, false, 89.999, out code), Is.True);
            Assert.That(inbox.TryTake(true, false, false, 89.999, out _), Is.False);
            inbox.Accept(valid, "production", "0.0.56", false, 100);
            Assert.That(inbox.TryTake(true, false, false, 190, out _), Is.False);
            inbox.Accept(valid, "production", "0.0.56", false, 200);
            Assert.That(inbox.TryTake(true, false, true, 201, out _), Is.False);
            Assert.That(inbox.TryTake(true, false, false, 202, out _), Is.False);
        }
        [Test] public void Invites_AreOffForFullPlayingRecoveryOrBusyRooms()
        {
            var players = new List<OnlinePlayerSnapshot> { new OnlinePlayerSnapshot("p", "P", 0, false, true) };
            foreach (var phase in new[] { MultiplayerConstants.LobbyPhase, MultiplayerConstants.PlayingPhase, MultiplayerConstants.HostRecoveryPhase })
            {
                var room = new SessionSnapshot("ABCD", true, phase, "p", players);
                Assert.That(SteamInvite.CanInvite(room, true, false), Is.EqualTo(phase == MultiplayerConstants.LobbyPhase));
                Assert.That(SteamInvite.CanInvite(room, true, true), Is.False);
                Assert.That(SteamInvite.CanInvite(room, false, false), Is.False);
            }
            for (var i = 1; i < 4; i++) players.Add(new OnlinePlayerSnapshot("p" + i, "P", i, false, false));
            Assert.That(SteamInvite.CanInvite(new SessionSnapshot("ABCD", true, MultiplayerConstants.LobbyPhase, "p", players), true, false), Is.False);
        }
        [Test] public void SteamAccountScopes_IsolateUsersAppsEnvironmentsAndNeverSerializeStoveCredentials()
        {
            var settings = new PlatformLaunchSettings { stoveGameId = "private-game", stoveAppKey = "private-key" };
            var original = PlatformAccountScope.AuthenticationProfile("steam", settings, 11);
            Assert.That(PlatformAccountScope.AuthenticationProfile("steam", settings, 12), Is.Not.EqualTo(original));
            Assert.That(PlatformAccountScope.AuthenticationProfile("stove", settings, 11), Is.Not.EqualTo(original));
            settings.steamAppId++;
            Assert.That(PlatformAccountScope.AuthenticationProfile("steam", settings, 11), Is.Not.EqualTo(original));
            settings.steamAppId--; settings.ugsEnvironment = "staging";
            Assert.That(PlatformAccountScope.AuthenticationProfile("steam", settings, 11), Is.Not.EqualTo(original));
            var json = settings.ToClientJson("steam");
            Assert.That(json, Does.Not.Contain("private-"));
            Assert.That(json, Does.Not.Contain("stove"));
            settings.steamAppId = 480;
            Assert.Throws<InvalidOperationException>(() => settings.Validate("steam"));
        }
        private sealed class FakeApi : ISteamAchievementApi
        {
            public readonly Dictionary<string, int> Stats = new Dictionary<string, int>();
            public readonly HashSet<string> Unlocked = new HashSet<string>();
            public int Stores, Writes; public bool Missing;
            public bool GetStat(string name, out int value) { Stats.TryGetValue(name, out value); return !Missing; }
            public bool GetAchievement(string name, out bool achieved) { achieved = Unlocked.Contains(name); return !Missing; }
            public bool SetStat(string name, int value) { Writes++; Stats[name] = value; return true; }
            public bool SetAchievement(string name) { Writes++; Unlocked.Add(name); return true; }
            public bool StoreStats() { Stores++; return true; }
        }
    }
}
