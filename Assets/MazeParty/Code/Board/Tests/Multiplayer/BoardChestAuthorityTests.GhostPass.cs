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
        [Test]
        public void GhostPass_UseValidatesMapAuthorityPauseAndCharges_AndConsumesLaterUsers()
        {
            Set(_match, "_boardMapId", new FixedString64Bytes(BoardForestGhostRules.MapId));
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.GhostPass), Is.True);
            foreach (var denied in new[] { "map", "client", "pause", "dead", "first turn" })
            {
                Set(_match, "_boardMapId", new FixedString64Bytes(denied == "map" ? "maze-graybox" : BoardForestGhostRules.MapId));
                Set(_match, "_turn", denied == "first turn" ? 1 : 2);
                Authority(_match, denied != "client");
                Set(_match, "_playerPauseActive", denied == "pause");
                Set(_player, "_currentHealth", denied == "dead" ? 0 : 100);
                Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, Vector3.forward), Is.False, denied);
                Assert.That(Get<int>(_player, "_itemCharges"), Is.EqualTo(1));
            }
            Set(_match, "_turn", 2);
            Authority(_match, true); Set(_match, "_playerPauseActive", false); Set(_player, "_currentHealth", 100);
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, Vector3.forward), Is.True);
            Assert.That(_match.HasUsedGhostPass(0), Is.True);
            Assert.That(Get<int>(_player, "_itemCharges"), Is.Zero);
            Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, Vector3.forward), Is.False);

            var later = Child("Later user").AddComponent<NetworkPlayerAvatar>();
            Authority(later, true); Set(later, "_slot", 1); Set(later, "_boardReady", true);
            Set(later, "_currentHealth", 100);
            Assert.That(later.EquipChestItemOnServer(PrototypeItemId.GhostPass), Is.True);
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Assert.That(_match.TryUseSelectedItemOnServer(later, later.transform.position + Vector3.up * .75f, Vector3.forward), Is.True);
            Assert.That(Get<int>(later, "_itemCharges"), Is.Zero);
            Assert.That(Get<int>(_match, "_ghostPassOwner"), Is.Zero);
            Assert.That(_match.HasUsedGhostPass(1), Is.True);
        }

        [Test]
        public void MapRestrictedItems_StayInForestPools_AndShopRestoreRejectsWrongMap()
        {
            var forest = BoardForestGhostRules.MapId;
            var seen = new bool[3];
            for (var seed = 0; seed < 200; seed++)
            {
                foreach (var map in new[] { "maze-graybox", forest })
                {
                    var reward = PrototypeItemCatalog.GetRandomId(new System.Random(seed), map);
                    var chest = BoardChestRules.DrawItem(PrototypeItemCatalog.All, seed, map);
                    var shop = new ItemShopStock(seed, map);
                    var offers = Enumerable.Range(0, ItemShopRules.OfferCount).Select(shop.GetOffer).ToArray();
                    Assert.That(PrototypeItemCatalog.Get(reward).IsAvailableOnMap(map), Is.True);
                    Assert.That(PrototypeItemCatalog.Get(chest).IsAvailableOnMap(map), Is.True);
                    Assert.That(offers.All(id => PrototypeItemCatalog.Get(id).IsAvailableOnMap(map)), Is.True);
                    var restored = ItemShopStock.Restore(offers, 3, seed, map);
                    Assert.That(Enumerable.Range(0, 5).Select(restored.GetOffer), Is.EqualTo(offers));
                    if (map == forest)
                    {
                        seen[0] |= reward == PrototypeItemId.GhostPass;
                        seen[1] |= chest == PrototypeItemId.GhostPass;
                        seen[2] |= offers.Contains(PrototypeItemId.GhostPass);
                        if (offers.Contains(PrototypeItemId.GhostPass))
                            Assert.Throws<System.ArgumentException>(() => ItemShopStock.Restore(offers, 0, mapId: "maze-graybox"));
                    }
                }
            }
            Assert.That(seen, Is.All.True);
            var validate = typeof(NetworkMatchState).GetMethod("ValidateRecoveryItemMaps", Private);
            var saved = new MatchRecoverySnapshot
            {
                boardMapId = forest,
                players = new[] { new MatchRecoveryPlayerSnapshot { occupiedItemMask = 1, itemSlot0 = (byte)PrototypeItemId.GhostPass } },
                itemShops = new[] { new MatchRecoveryItemShopSnapshot { active = true,
                    offers = new[] { (int)PrototypeItemId.GhostPass, (int)PrototypeItemId.Grenade } } }
            };
            Assert.That(validate.Invoke(_match, new object[] { saved }), Is.True);
            saved.boardMapId = "maze-graybox";
            var before = JsonUtility.ToJson(saved);
            Assert.That(validate.Invoke(_match, new object[] { saved }), Is.False);
            Assert.That(JsonUtility.ToJson(saved), Is.EqualTo(before));
            saved.itemShops[0].offers = new[] { (int)PrototypeItemId.Grenade };
            Assert.That(validate.Invoke(_match, new object[] { saved }), Is.False);
            saved.players[0].itemSlot0 = (byte)PrototypeItemId.Grenade;
            Assert.That(validate.Invoke(_match, new object[] { saved }), Is.True);
        }
    }
}
