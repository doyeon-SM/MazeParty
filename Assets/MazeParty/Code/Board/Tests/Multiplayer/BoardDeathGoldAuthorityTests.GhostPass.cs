using System;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Collections;

namespace MazeParty.Multiplayer.Tests
{
    public sealed partial class BoardDeathGoldAuthorityTests
    {
        [Test]
        public void GhostPass_FirstUserWins_TargetExcludesSelfDeadAndRespawn_AndExpiresNextTurn()
        {
            PrepareGhostBoard();
            var register = typeof(NetworkMatchState).GetMethod("RegisterGhostPassOnServer", Private);
            Authority(_match, false);
            register.Invoke(_match, new object[] { 0 });
            Assert.That(_match.HasUsedGhostPass(0), Is.False);
            Authority(_match, true);
            register.Invoke(_match, new object[] { 0 });
            register.Invoke(_match, new object[] { 1 });
            Assert.That(_match.HasUsedGhostPass(0) && _match.HasUsedGhostPass(1), Is.True);
            Set(_players[0], "_currentHealth", 0);
            Set(_players[2], "_currentHealth", 0);
            for (var seed = 1; seed <= 80; seed++)
            {
                Set(_match, "_boardEffectSeed", seed);
                typeof(NetworkMatchState).GetMethod("ResetForestGhostOnServer", Private).Invoke(_match, null);
                Invoke("AdvanceForestGhostOnServer", 100d);
                Assert.That(_match.ForestGhost.Beneficiary, Is.EqualTo(1));
                Assert.That(_match.ForestGhost.Slot, Is.EqualTo(1).Or.EqualTo(3));
                Assert.That(_match.ForestGhost.Amount, Is.LessThan(0));
                var expected = BoardForestGhostRules.Resolve(seed, 20, 15, 0);
                Assert.That(_match.ForestGhost.Resource, Is.EqualTo((byte)expected.Resource));
                Assert.That(_match.ForestGhost.Amount, Is.EqualTo(-Math.Abs(expected.Amount)));
            }
            Set(_players[1], "_currentHealth", 0);
            Set(_players[3], "_currentHealth", 0);
            typeof(NetworkMatchState).GetMethod("ResetForestGhostOnServer", Private).Invoke(_match, null);
            Invoke("AdvanceForestGhostOnServer", 110d);
            Assert.That(_match.ForestGhost.Active, Is.False);
            Assert.That(_match.HasUsedGhostPass(0) || _match.HasUsedGhostPass(1), Is.False);
            Set(_match, "_turn", 21);
            register.Invoke(_match, new object[] { 2 });
            Assert.That(typeof(NetworkMatchState).GetProperty("GhostPassBeneficiary", Private).GetValue(_match), Is.EqualTo(2));
        }

        [Test]
        public void GhostPass_TransfersActualStolenAmountOnce_AfterTheft_WithPauseAndDisconnectedRecipient()
        {
            PrepareGhostBoard();
            var cases = new[] {
                (ForestGhostResource.Gold, 6, 10, 20, 6, 16),
                (ForestGhostResource.Key, 1, 0, 1, 1, 1),
                (ForestGhostResource.Key, 0, 0, 1, 0, 0),
                (ForestGhostResource.Health, 80, 90, 50, 50, 100),
                (ForestGhostResource.Health, 80, 0, 50, 50, 0)
            };
            foreach (var entry in cases)
            {
                var field = entry.Item1 == ForestGhostResource.Gold ? "_gold" :
                    entry.Item1 == ForestGhostResource.Key ? "_keyCount" : "_currentHealth";
                Set(_players[0], "_currentHealth", 0); // Dead owners still receive gold and keys.
                Set(_players[1], "_currentHealth", 100);
                Set(_players[1], field, entry.Item2); Set(_players[0], field, entry.Item3);
                Set(_match, "_ghostPassTurn", 20); Set(_match, "_ghostPassOwner", 0); Set(_match, "_ghostPassUsers", (byte)1);
                typeof(NetworkMatchState).GetMethod("ResetForestGhostOnServer", Private).Invoke(_match, null);
                Set(_match, "_forestGhost", new BoardForestGhostSnapshot { Active = true, Slot = 1,
                    Beneficiary = 1, Revision = 1, Amount = -entry.Item4, StartedAt = 100d, Resource = (byte)entry.Item1 });
                if (entry.Item1 == ForestGhostResource.Health) ExpectPresentationRpcs(1);
                Invoke("AdvanceForestGhostOnServer", 101.249999d);
                Assert.That(_match.ForestGhost.Applied, Is.False);
                Invoke("AdvanceForestGhostOnServer", 101.25d);
                Invoke("AdvanceForestGhostOnServer", 101.25d);
                Assert.That(_match.ForestGhost.ActualAmount, Is.EqualTo(-entry.Item5));
                Invoke("AdvanceForestGhostOnServer", 102.999999d);
                Assert.That(_match.ForestGhost.IsDelivery, Is.False);
                if (entry.Item5 == 0)
                {
                    Invoke("AdvanceForestGhostOnServer", 103d);
                    Assert.That(_match.ForestGhost.Active, Is.False);
                    continue;
                }
                var owner = _players[0]; _players[0] = null;
                typeof(Unity.Netcode.NetworkBehaviour).GetProperty("IsSpawned").SetValue(owner, false);
                Invoke("AdvanceForestGhostOnServer", 103d);
                Assert.That(_match.ForestGhost.IsDelivery, Is.False);
                _players[0] = owner;
                typeof(Unity.Netcode.NetworkBehaviour).GetProperty("IsSpawned").SetValue(owner, true);
                Invoke("AdvanceForestGhostOnServer", 103d);
                Assert.That(_match.ForestGhost.IsDelivery, Is.True);
                Assert.That(_match.ForestGhost.Slot, Is.Zero);
                Invoke("PauseForestGhostOnServer", 103.5d);
                Invoke("AdvanceForestGhostOnServer", 200d);
                Assert.That(_match.ForestGhost.Applied, Is.False);
                Invoke("ResumeForestGhostOnServer", 203.5d);
                Invoke("AdvanceForestGhostOnServer", 204.249999d);
                Assert.That(_match.ForestGhost.Applied, Is.False);
                Invoke("AdvanceForestGhostOnServer", 204.25d);
                Invoke("AdvanceForestGhostOnServer", 204.25d);
                var actual = entry.Item1 == ForestGhostResource.Gold ? owner.Gold :
                    entry.Item1 == ForestGhostResource.Key ? owner.KeyCount : owner.CurrentHealth;
                Assert.That(actual, Is.EqualTo(entry.Item6), entry.Item1.ToString());
                Invoke("AdvanceForestGhostOnServer", 206d);
                Assert.That(_match.ForestGhost.Active, Is.False);
                Assert.That(_match.HasUsedGhostPass(0), Is.False);
            }
        }
    }
}
