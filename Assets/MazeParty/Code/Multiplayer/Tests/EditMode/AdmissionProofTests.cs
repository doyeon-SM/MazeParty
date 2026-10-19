using System;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class AdmissionProofTests
    {
        [Test]
        public void AdmissionRequiresUgsMemberKeyRoomPlatformAndFreshSignature()
        {
            using (var client = new AdmissionProof())
            using (var stranger = new AdmissionProof())
            {
                const long now = 1800000000;
                var members = new Dictionary<string, string> { ["player-a"] = client.PublicKey, ["player-b"] = stranger.PublicKey };
                var payload = client.Create("stove", "production", "1", "code:ABCDEF", "player-a", now);
                Assert.That(Verify(payload, members, now), Is.True);
                Assert.That(Verify(payload, members, now + 60), Is.True);
                Assert.That(Verify(payload, members, now + 61), Is.False);
                Assert.That(Verify(payload, members, now - 10), Is.True);
                Assert.That(Verify(payload, members, now - 11), Is.False);
                foreach (var forged in new[]
                {
                    client.Create("steam", "production", "1", "code:ABCDEF", "player-a", now),
                    client.Create("development", "production", "1", "code:ABCDEF", "player-a", now),
                    client.Create("stove", "other", "1", "code:ABCDEF", "player-a", now),
                    client.Create("stove", "production", "2", "code:ABCDEF", "player-a", now),
                    client.Create("stove", "production", "1", "code:ZZZZZZ", "player-a", now),
                    client.Create("stove", "production", "1", "id:other-room", "player-a", now),
                    client.Create("stove", "production", "1", "code:ABCDEF", "player-b", now),
                    client.Create("stove", "production", "1", "code:ABCDEF", "non-member", now),
                    stranger.Create("stove", "production", "1", "code:ABCDEF", "player-a", now),
                    new byte[0], new byte[AdmissionProof.MaximumPayloadBytes + 1]
                }) Assert.That(Verify(forged, members, now), Is.False);
                Assert.That(Verify(client.Create("stove", "production", "1", "id:room-id", "player-a", now), members, now), Is.True);
                var truncated = new byte[payload.Length - 1];
                Array.Copy(payload, truncated, truncated.Length);
                var trailing = new byte[payload.Length + 1];
                Array.Copy(payload, trailing, payload.Length);
                Assert.That(Verify(truncated, members, now), Is.False);
                Assert.That(Verify(trailing, members, now), Is.False);
                members["player-a"] = stranger.PublicKey;
                Assert.That(Verify(payload, members, now), Is.False, "A restarted client replaces its UGS member key before reconnecting.");
                members["player-a"] = client.PublicKey;
                payload[payload.Length - 1] ^= 1;
                Assert.That(Verify(payload, members, now), Is.False);
            }
        }

        private static bool Verify(byte[] payload, Dictionary<string, string> members, long now) =>
            AdmissionProof.TryVerify(payload, "stove", "production", "1", "ABCDEF", "room-id", now, out var proof) &&
            members.TryGetValue(proof.PlayerId, out var registeredKey) && proof.MatchesMember(proof.PlayerId, registeredKey);

        [Test]
        public void ReplayCannotBeReusedOrEvictedByFloodBeforeExpiry()
        {
            var guard = new AdmissionReplayGuard();
            var proof = new VerifiedAdmission("player-a", "nonce", "unused-in-replay-check");
            Assert.That(guard.TryUse(proof, 100), Is.True);
            Assert.That(guard.TryUse(proof, 170), Is.False);
            for (var i = 1; i < 1024; i++) Assert.That(guard.TryUse(new VerifiedAdmission("player", i.ToString(), ""), 100), Is.True);
            Assert.That(guard.TryUse(new VerifiedAdmission("player", "overflow", ""), 100), Is.False);
            Assert.That(guard.TryUse(proof, 170), Is.False);
            Assert.That(guard.TryUse(proof, 171), Is.True);
        }

        [Test]
        public void HostBindingsRejectStaleSeatOwnersAndDuplicateConnectionsWithinOneProperty()
        {
            var owners = new[] { "host", "guest-a", "guest-b", "guest-c" };
            var state = PlatformSessionAdmission.Empty("stove", "production");
            for (var i = 0; i < 4; i++) state = PlatformSessionAdmission.WithConnection(state, i, (ulong)i, owners[i]);
            for (var i = 0; i < 4; i++)
            {
                Assert.That(PlatformSessionAdmission.TryGetSlot(state, owners, (ulong)i, out var slot), Is.True);
                Assert.That(slot, Is.EqualTo(i));
            }
            Assert.That(PlatformSessionAdmission.HasScope(state, "steam", "production"), Is.False);
            Assert.That(PlatformSessionAdmission.HasScope(state, "development", "production"), Is.False);
            Assert.That(PlatformSessionAdmission.HasScope(state, "stove", "other"), Is.False);
            owners[1] = "replacement";
            Assert.That(PlatformSessionAdmission.TryGetSlot(state, owners, 1, out _), Is.False);
            state = PlatformSessionAdmission.WithConnection(state, 1, 2, owners[1]);
            Assert.That(PlatformSessionAdmission.TryGetSlot(state, owners, 2, out _), Is.False);
            Assert.That(PlatformSessionAdmission.TryGetSlot(PlatformSessionAdmission.Empty("stove", "production"),
                owners, 0, out _), Is.False, "Network recovery clears previous connection IDs, including the host.");
            state = PlatformSessionAdmission.Empty("stove", "production");
            for (var i = 0; i < 4; i++) state = PlatformSessionAdmission.WithConnection(state, i, ulong.MaxValue, new string('a', 128));
            Assert.That(Encoding.UTF8.GetByteCount(state), Is.LessThan(2048), "MPS non-indexed data size limit");
        }

        [Test]
        public void StoreConfigurationFailsClosedAndExportsOnlyClientFields()
        {
            var settings = new PlatformLaunchSettings();
            Assert.Throws<InvalidOperationException>(() => settings.Validate("stove"));
            settings.stoveGameId = "test-game";
            settings.stoveAppKey = "test-client-key";
            Assert.DoesNotThrow(() => settings.Validate("stove"));
            var clean = JsonUtility.FromJson<PlatformLaunchSettings>(settings.ToClientJson("stove").TrimEnd('}') +
                ",\"authBridgeUrl\":\"https://obsolete.invalid\",\"grantKeyModulus\":\"retired\",\"applicationSecret\":\"must-not-export\"}");
            var output = clean.ToClientJson("stove");
            Assert.That(output, Does.Not.Contain("authBridgeUrl").And.Not.Contain("grantKeyModulus").And.Not.Contain("applicationSecret").And.Not.Contain("must-not-export"));
            Assert.That(clean.ToClientJson("steam"), Does.Not.Contain("test-client-key").And.Not.Contain("stoveGameId"));
            settings.ugsEnvironment = "";
            Assert.Throws<InvalidOperationException>(() => settings.Validate("stove"));
            settings.ugsEnvironment = "production";
            foreach (var environment in new[] { "LIVE", "live", " real ", "SANDBOX", "sandbox" })
            {
                settings.stoveEnvironment = environment;
                Assert.DoesNotThrow(() => settings.Validate("stove"));
                var packaged = JsonUtility.FromJson<PlatformLaunchSettings>(settings.ToClientJson("stove"));
                var expected = environment.Trim().Equals("sandbox", StringComparison.OrdinalIgnoreCase) ? "SANDBOX" : "LIVE";
                Assert.That(packaged.stoveEnvironment, Is.EqualTo(expected), "The SDK must never receive legacy real or an unrecognized environment.");
                Assert.That(settings.StoveSdkEnvironment, Is.EqualTo(packaged.stoveEnvironment), "Editor and packaged SDK calls must agree.");
                Assert.That(packaged.stoveGameId, Is.EqualTo(settings.stoveGameId));
                Assert.That(packaged.stoveAppKey, Is.EqualTo(settings.stoveAppKey));
            }
            foreach (var invalid in new[] { null, "", " ", "production", "lvei", "live-typo" })
            {
                settings.stoveEnvironment = invalid;
                Assert.Throws<InvalidOperationException>(() => settings.Validate("stove"));
                Assert.Throws<InvalidOperationException>(() => settings.ToClientJson("stove"));
                Assert.DoesNotThrow(() => settings.Validate("steam"), "STOVE configuration must not gate another store.");
            }
        }

        [Test]
        public void LocalReleaseAccountsNeverShareCachesAcrossStoreAccountGameOrEnvironment()
        {
            var settings = new PlatformLaunchSettings { stoveGameId = "game-a" };
            var profile = PlatformAccountScope.AuthenticationProfile("stove", settings, 123);
            Assert.That(PlatformAccountScope.AuthenticationProfile("stove", settings, 123), Is.EqualTo(profile), "Same local account survives process restart.");
            Assert.That(profile, Is.Not.EqualTo("default").And.Not.StartsWith("mp-stove-"));
            Assert.That(PlatformAccountScope.AuthenticationProfile("stove", settings, 124), Is.Not.EqualTo(profile));
            Assert.That(PlatformAccountScope.AuthenticationProfile("steam", settings, 123), Is.Not.EqualTo(profile));
            settings.ugsEnvironment = "test";
            Assert.That(PlatformAccountScope.AuthenticationProfile("stove", settings, 123), Is.Not.EqualTo(profile));
            settings.ugsEnvironment = "production";
            foreach (var alias in new[] { "real", "live", " LIVE " })
            {
                settings.stoveEnvironment = alias;
                Assert.That(PlatformAccountScope.AuthenticationProfile("stove", settings, 123), Is.EqualTo(profile), "Equivalent Live settings must reuse one local UGS account.");
            }
            settings.stoveEnvironment = "SANDBOX";
            Assert.That(PlatformAccountScope.AuthenticationProfile("stove", settings, 123), Is.Not.EqualTo(profile), "Sandbox must not reuse a Live account cache.");
            settings.stoveEnvironment = "LIVE";
            settings.stoveGameId = "game-b";
            Assert.That(PlatformAccountScope.AuthenticationProfile("stove", settings, 123), Is.Not.EqualTo(profile));
            Assert.Throws<ArgumentException>(() => PlatformAccountScope.AuthenticationProfile("stove", settings, 0));
        }
    }
}
