using System;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace MazeParty.Multiplayer.Tests
{
    public sealed partial class BoardChestAuthorityTests
    {
        private BoardFlowStateMachine AmplifierAction(int turn = 2, int total = 15)
        {
            Set(_match, "_boardMapId", new FixedString64Bytes(MagicMazeRules.MapId));
            var now = _match.SynchronizedNow;
            var flow = new BoardFlowStateMachine(totalTurns: total, magicMaze: true);
            flow.Start(now - 8d, turn); flow.Tick(now);
            typeof(NetworkMatchState).GetField("_flow", Private).SetValue(_match, flow);
            Invoke("SyncFlowSnapshot", now);
            return flow;
        }

        [Test]
        public void TileAmplifier_ServerValidatesUse_OnePerPlayer_StacksAcrossUsers_SurvivesDeathAndPause()
        {
            var flow = AmplifierAction();
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.TileAmplifier), Is.True);
            foreach (var denied in new[] { "map", "client", "pause", "dead", "phase" })
            {
                Set(_match, "_boardMapId", new FixedString64Bytes(denied == "map" ? "maze-graybox" : MagicMazeRules.MapId));
                Authority(_match, denied != "client"); Set(_match, "_playerPauseActive", denied == "pause");
                Set(_player, "_currentHealth", denied == "dead" ? 0 : 100);
                Set(_match, "_flowState", (byte)(denied == "phase" ? BoardFlowState.LandingEffectResolve : BoardFlowState.Action));
                Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, Vector3.forward), Is.False, denied);
                Assert.That(Get<int>(_player, "_itemCharges"), Is.EqualTo(1));
                Assert.That(_match.HasUsedTileAmplifier(0), Is.False);
            }
            Authority(_match, true); Set(_match, "_playerPauseActive", false); Set(_player, "_currentHealth", 100);
            Set(_match, "_flowState", (byte)BoardFlowState.Action);
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, Vector3.forward), Is.True);
            Assert.That(_match.HasUsedTileAmplifier(0), Is.True);
            Assert.That(Get<int>(_player, "_itemCharges"), Is.Zero);
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.TileAmplifier), Is.True);
            typeof(NetworkPlayerAvatar).GetField("_itemCooldownRemaining", Private).SetValue(_player, 0d);
            Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, Vector3.forward), Is.False);
            Assert.That(Get<int>(_player, "_itemCharges"), Is.EqualTo(1), "Rejected reuse must not consume the second item.");
            for (var slot = 1; slot < 4; slot++)
            {
                var avatar = Child("Amplifier user " + slot).AddComponent<NetworkPlayerAvatar>();
                Authority(avatar, true); Set(avatar, "_slot", slot); Set(avatar, "_boardReady", true); Set(avatar, "_currentHealth", 100);
                Assert.That(avatar.EquipChestItemOnServer(PrototypeItemId.TileAmplifier), Is.True);
                LogAssert.Expect(LogType.Error, RpcUnavailable);
                Assert.That(_match.TryUseSelectedItemOnServer(avatar, avatar.transform.position + Vector3.up * .75f, Vector3.forward), Is.True);
                Assert.That(TileAmplifierRules.Multiplier(Get<byte>(_match, "_tileAmplifierUsers")), Is.EqualTo(slot + 2));
            }
            Set(_player, "_currentHealth", 0);
            flow.Pause(_match.SynchronizedNow);
            Assert.That(Get<byte>(_match, "_tileAmplifierUsers"), Is.EqualTo(15));
            Assert.That(_match.ShowTileAmplifierOutline(0), Is.True);
            flow.Resume(_match.SynchronizedNow + 10d);
            Assert.That(_match.HasUsedTileAmplifier(0), Is.True);
            Set(_match, "_turn", 3);
            Assert.That(_match.HasUsedTileAmplifier(0), Is.False);
        }

        [Test]
        public void TileAmplifier_LandingRewards_RespectCapacityIndependentDrawsAndExactlyOnce()
        {
            AmplifierAction();
            Set(_match, "_boardEffectSeed", 427);
            typeof(NetworkMatchState).GetField("_avatarLookupCache", Private).SetValue(_match, new[] { _player, null, null, null });
            var effects = (BoardLandingEffectType[])typeof(NetworkMatchState).GetField("_landingEffectPlan", Private).GetValue(_match);
            var tiles = (BoardTile[])typeof(NetworkMatchState).GetField("_landingEffectTiles", Private).GetValue(_match);
            tiles[0] = _topology.Tiles.First(t => t.TileType == BoardTileType.Normal);
            Set(_match, "_tileAmplifierTurn", 2);
            foreach (var users in new byte[] { 0, 1, 3, 7, 15 })
            foreach (var occupied in new byte[] { 0, 1, 5, 7 })
            {
                Set(_match, "_tileAmplifierUsers", users);
                Set(_player, "_occupiedItemMask", occupied);
                Set(_player, "_itemSlot0", (byte)PrototypeItemId.Mine);
                Set(_player, "_itemSlot1", (byte)PrototypeItemId.Pistol);
                Set(_player, "_itemSlot2", (byte)PrototypeItemId.Sniper);
                typeof(NetworkMatchState).GetField("_landingEffectAppliedMask", Private).SetValue(_match, (byte)0);
                effects[0] = BoardLandingEffectType.ItemReward;
                var seed = (int)typeof(NetworkMatchState).GetMethod("GetLandingItemRewardSeed", Private).Invoke(_match, new object[] { 0 });
                var random = new System.Random(seed);
                var remaining = Math.Min(3, TileAmplifierRules.Multiplier(users));
                var expected = new byte[] { (byte)PrototypeItemId.Mine, (byte)PrototypeItemId.Pistol, (byte)PrototypeItemId.Sniper };
                var expectedMask = occupied;
                for (var index = 0; index < 3 && remaining > 0; index++)
                    if ((occupied & (1 << index)) == 0)
                    {
                        expected[index] = (byte)PrototypeItemCatalog.GetRandomId(random, MagicMazeRules.MapId);
                        expectedMask |= (byte)(1 << index); remaining--;
                    }
                Invoke("ApplyLandingEffectForSlotOnServer", 0);
                for (var twice = 0; twice < 2; twice++)
                {
                    Assert.That(Get<byte>(_player, "_occupiedItemMask"), Is.EqualTo(expectedMask));
                    Assert.That(new[] { Get<byte>(_player, "_itemSlot0"), Get<byte>(_player, "_itemSlot1"), Get<byte>(_player, "_itemSlot2") }, Is.EqualTo(expected));
                    Invoke("ApplyLandingEffectForSlotOnServer", 0);
                }
            }
        }

        [Test]
        public void TileAmplifier_RequestedAndPeriodicShufflesCommitExactlyOnce_AndRestoreWithoutReplay()
        {
            foreach (var turn in new[] { 2, 3, 5 })
            {
                AmplifierAction(turn);
                Set(_match, "_boardEffectSeed", 315); Set(_match, "_boardEffectRevision", 1);
                Invoke("InitializeMagicMazeOnServer");
                var flow = new BoardFlowStateMachine(totalTurns: 15, magicMaze: true);
                flow.Start(0, turn); flow.Tick(7);
                typeof(NetworkMatchState).GetField("_flow", Private).SetValue(_match, flow);
                Invoke("RegisterTileAmplifierOnServer", 0); Invoke("RegisterTileAmplifierOnServer", 1);
                for (var slot = 0; slot < 4; slot++) flow.TryReportPlayerArrived(slot, 8);
                flow.Tick(13); flow.TryCompleteCombat(13); flow.Tick(17);
                Invoke("SyncFlowSnapshot", 17d);
                Assert.That(_match.IsMagicMazeShuffle, Is.True);
                var revision = _match.BoardEffectRevision;
                Invoke("AdvanceMagicMazeOnServer", 21.499d);
                Assert.That(_match.BoardEffectRevision, Is.EqualTo(revision));
                Invoke("AdvanceMagicMazeOnServer", 21.5d);
                Invoke("CommitMagicMazeShuffleOnServer");
                Assert.That(_match.BoardEffectRevision, Is.EqualTo(revision + 1));
                var saved = new MatchRecoverySnapshot { boardMapId = MagicMazeRules.MapId, boardEffectSeed = 315,
                    boardEffectRevision = revision + 1, magicMazeShuffledTurn = turn,
                    magicMazeEffects = (MagicMazeEffectSnapshot[])typeof(NetworkMatchState).GetMethod("CaptureMagicMazeEffects", Private).Invoke(_match, null) };
                Invoke("InitializeMagicMazeOnServer"); Invoke("RestoreBoardEffects", saved);
                Invoke("CommitMagicMazeShuffleOnServer");
                Assert.That(_match.BoardEffectRevision, Is.EqualTo(revision + 1));
                Assert.That((MagicMazeEffectSnapshot[])typeof(NetworkMatchState).GetMethod("CaptureMagicMazeEffects", Private).Invoke(_match, null), Is.EqualTo(saved.magicMazeEffects));
            }
        }

        [Test]
        public void TileAmplifier_RecoveryRemapsUsers_AndItemBelongsOnlyToMagicMazePools()
        {
            AmplifierAction();
            var saved = new MatchRecoverySnapshot { boardMapId = MagicMazeRules.MapId, turn = 2, scheduleTurnCount = 15,
                checkpoint = MatchRecoveryCheckpoint.MinigameIntroReady, tileAmplifierTurn = 2, magicMazeShuffledTurn = 2,
                players = new[] { new MatchRecoveryPlayerSnapshot { playerKey = "a", usedTileAmplifier = true },
                    new MatchRecoveryPlayerSnapshot { playerKey = "b" }, new MatchRecoveryPlayerSnapshot { playerKey = "c", usedTileAmplifier = true }, new MatchRecoveryPlayerSnapshot { playerKey = "d" } } };
            Assert.That(NetworkMatchState.AreTileAmplifierSnapshotsValid(saved), Is.True);
            var remapped = new[] { saved.players[1], saved.players[0], saved.players[3], saved.players[2] };
            Invoke("RestoreTileAmplifier", saved, remapped);
            Assert.That(Get<byte>(_match, "_tileAmplifierUsers"), Is.EqualTo(10));
            Set(_match, "_flowState", (byte)BoardFlowState.MinigameIntroReady);
            Assert.That(_match.ShowTileAmplifierOutline(1), Is.False);
            foreach (var player in remapped)
            {
                var seed = TileAmplifierRules.RewardSeed(31, 2, player.playerKey);
                var before = new System.Random(seed); var after = new System.Random(TileAmplifierRules.RewardSeed(31, 2, player.playerKey));
                for (var i = 0; i < 3; i++) Assert.That(PrototypeItemCatalog.GetRandomId(after, MagicMazeRules.MapId), Is.EqualTo(PrototypeItemCatalog.GetRandomId(before, MagicMazeRules.MapId)));
            }
            var seen = new bool[3];
            foreach (var map in new[] { "maze-graybox", BoardForestGhostRules.MapId, MagicMazeRules.MapId })
            for (var seed = 0; seed < 200; seed++)
            {
                var reward = PrototypeItemCatalog.GetRandomId(new System.Random(seed), map);
                var chest = BoardChestRules.DrawItem(PrototypeItemCatalog.All, seed, map);
                var stock = new ItemShopStock(seed, map);
                var offered = Enumerable.Range(0, ItemShopRules.OfferCount).Any(i => stock.GetOffer(i) == PrototypeItemId.TileAmplifier);
                if (map != MagicMazeRules.MapId)
                {
                    Assert.That(reward, Is.Not.EqualTo(PrototypeItemId.TileAmplifier));
                    Assert.That(chest, Is.Not.EqualTo(PrototypeItemId.TileAmplifier)); Assert.That(offered, Is.False);
                }
                else { seen[0] |= reward == PrototypeItemId.TileAmplifier; seen[1] |= chest == PrototypeItemId.TileAmplifier; seen[2] |= offered; }
            }
            Assert.That(seen, Is.All.True);
        }
    }
}
