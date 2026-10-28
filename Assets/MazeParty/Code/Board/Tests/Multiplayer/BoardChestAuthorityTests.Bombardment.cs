using System.Collections;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.TestTools;

namespace MazeParty.Multiplayer.Tests
{
    public sealed partial class BoardChestAuthorityTests
    {

        [Test]
        public void Bombardment_ExplosionAppliesDistanceBandsSelfAndStacking_WithWallsAndProtection()
        {
            Set(_match, "_shieldEndsAt", 0d);
            Set(_player, "_maxHealth", 500);
            var cache = (NetworkPlayerAvatar[])typeof(NetworkMatchState).GetField("_avatarLookupCache", Private).GetValue(_match);
            cache[0] = _player;
            var item = PrototypeItemCatalog.Get(PrototypeItemId.Bombardment);
            var origin = new Vector3(0, 2, -20);
            var distances = new[] { 0f, 3f, 3.01f, 7f, 7.01f, 10f, 10.01f };
            var expected = new[] { 80, 80, 50, 50, 20, 20, 0 };
            for (var i = 0; i < distances.Length; i++)
            {
                Set(_player, "_currentHealth", 500);
                _player.transform.position = origin + Vector3.right * distances[i];
                if (expected[i] > 0) LogAssert.Expect(LogType.Error, RpcUnavailable);
                LogAssert.Expect(LogType.Error, RpcUnavailable);
                Invoke("ExplodeBoardItem", origin, 0, item);
                Assert.That(_player.CurrentHealth, Is.EqualTo(500 - expected[i]), distances[i].ToString());
            }
            Set(_player, "_currentHealth", 500);
            _player.transform.position = origin;
            for (int i = 0; i < 3; i++)
            {
                LogAssert.Expect(LogType.Error, RpcUnavailable);
                LogAssert.Expect(LogType.Error, RpcUnavailable);
                Invoke("ExplodeBoardItem", origin, 0, item);
            }
            Assert.That(_player.CurrentHealth, Is.EqualTo(260));
            _player.transform.position = origin + Vector3.right * 5;
            var wall = Child("Blast cover").AddComponent<BoxCollider>();
            wall.transform.position = origin + Vector3.right * 2.5f;
            wall.size = new Vector3(.5f, 4f, 4f);
            Physics.SyncTransforms();
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Invoke("ExplodeBoardItem", origin, 0, item);
            Assert.That(_player.CurrentHealth, Is.EqualTo(260));
            wall.enabled = false;
            Physics.SyncTransforms();
            Set(_match, "_shieldEndsAt", _match.SynchronizedNow + 10d);
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Invoke("ExplodeBoardItem", origin, 0, item);
            Assert.That(_player.CurrentHealth, Is.EqualTo(260));
        }

        private const string RpcUnavailable = "Rpc methods can only be invoked after starting the NetworkManager!";
        private void EquipBombardment()
        {
            Set(_player, "_selectedItemSlot", 0);
            Set(_player, "_occupiedItemMask", (byte)1);
            Set(_player, "_itemSlot0", (byte)PrototypeItemId.Bombardment);
            Set(_player, "_equippedItem", (byte)PrototypeItemId.Bombardment);
            Set(_player, "_itemCharges", 1);
            Set(_player, "_choiceResolution", (byte)ItemChoiceResolution.ItemSelected);
            typeof(NetworkPlayerAvatar).GetField("_topology", Private).SetValue(_player, _topology);
            var floor = Child("Bombardment ground").AddComponent<BoxCollider>();
            floor.center = new Vector3(0, -.1f, 3);
            floor.size = new Vector3(6, .2f, 6);
            Physics.SyncTransforms();
        }

        [Test]
        public void Bombardment_StartConsumesOnce_RejectsUnauthorizedActions_AndSelectionCannotCancel()
        {
            EquipBombardment();
            Authority(_match, false);
            Assert.That(_match.TryBeginBombardmentOnServer(_player), Is.False);
            Authority(_match, true);
            Set(_match, "_playerPauseActive", true);
            Assert.That(_match.TryBeginBombardmentOnServer(_player), Is.False);
            Set(_match, "_playerPauseActive", false);
            Set(_player, "_currentHealth", 0);
            Assert.That(_match.TryBeginBombardmentOnServer(_player), Is.False);
            Set(_player, "_currentHealth", 100);
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Assert.That(_match.TryBeginBombardmentOnServer(_player), Is.True);
            Assert.That(Get<byte>(_player, "_occupiedItemMask"), Is.Zero);
            Assert.That(Get<int>(_player, "_itemCharges"), Is.Zero);
            Assert.That(_match.TryBeginBombardmentOnServer(_player), Is.False);
            Assert.That(_player.EquipChestItemOnServer(PrototypeItemId.Pistol), Is.False);
            Assert.That(_match.TryUseSelectedItemOnServer(_player, _player.EyePivot.position, Vector3.forward), Is.False);
            var point = new Vector3(0, 0, 3);
            Assert.That(_match.TryChooseBombardmentTargetOnServer(_player, point, 0, 0), Is.False);
            Assert.That(_match.TryChooseBombardmentTargetOnServer(_player, point, 0, 1), Is.True);
            Assert.That(_match.TryChooseBombardmentTargetOnServer(_player, point, 0, 1), Is.False);
            Assert.That(_player.LocalBombardmentTargetCount, Is.EqualTo(1));
            Set(_match, "_shieldEndsAt", 0d);
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Assert.That(_player.ApplyDamage(new DamageRequest(50, DamageKind.Item, null)), Is.EqualTo(DamageResult.Applied));
            typeof(NetworkPlayerAvatar).GetMethod("TickBombardmentSelection", Private).Invoke(_player, null);
            Assert.That(_player.IsSettingBombardment, Is.True);
            var snapshot = _player.CreateReconnectSnapshotOnServer();
            Assert.That(snapshot.BombardmentSelecting, Is.True);
            Assert.That(snapshot.BombardmentTargets, Has.Length.EqualTo(1));
            typeof(NetworkPlayerAvatar).GetMethod("ClearBombardmentSelectionOnServer", Private).Invoke(_player, null);
            typeof(NetworkPlayerAvatar).GetMethod("RestoreBombardmentSelection", Private).Invoke(_player, new object[] { snapshot });
            Assert.That(_player.LocalBombardmentTargetCount, Is.EqualTo(1));
            Assert.That(Get<int>(_player, "_itemCharges"), Is.Zero);
            Set(_player, "_currentHealth", 0);
            typeof(NetworkPlayerAvatar).GetMethod("TickBombardmentSelection", Private).Invoke(_player, null);
            Assert.That(_player.IsSettingBombardment, Is.False);
            Assert.That(((IList)typeof(NetworkMatchState).GetField("_boardSalvos", Private).GetValue(_match)).Count, Is.Zero);
            Assert.That(Get<byte>(_player, "_occupiedItemMask"), Is.Zero);
        }

        [Test]
        public void Bombardment_ThreeRepeatedTargetsCommitOnce_AndDeathDoesNotRevokeSalvo()
        {
            EquipBombardment();
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Assert.That(_match.TryBeginBombardmentOnServer(_player), Is.True);
            var point = new Vector3(0, 0, 3);
            for (byte i = 0; i < 3; i++)
            {
                if (i == 2) LogAssert.Expect(LogType.Error, RpcUnavailable);
                Assert.That(_match.TryChooseBombardmentTargetOnServer(_player, point, i, 1), Is.True);
            }
            Assert.That(_player.IsSettingBombardment, Is.False);
            Assert.That(_match.TryChooseBombardmentTargetOnServer(_player, point, 2, 1), Is.False);
            var salvos = (IList)typeof(NetworkMatchState).GetField("_boardSalvos", Private).GetValue(_match);
            Assert.That(salvos.Count, Is.EqualTo(1));
            Set(_player, "_currentHealth", 0);
            _player.ClearUtilityEffectsOnServer();
            Assert.That(salvos.Count, Is.EqualTo(1));
            var salvo = salvos[0];
            var sequence = (BoardBombardmentSequence)salvo.GetType().GetField("Sequence").GetValue(salvo);
            sequence.Tick(_match.SynchronizedNow + 3d, false);
            for (int i = 0; i < 4; i++) LogAssert.Expect(LogType.Error, RpcUnavailable);
            Invoke("TickBoardBombardments");
            Assert.That(salvos.Count, Is.Zero);
            Assert.That(((IList)typeof(NetworkMatchState).GetField("_boardGrenades", Private).GetValue(_match)).Count, Is.Zero);
        }

        [Test]
        public void Bombardment_IncompleteSelectionExpiresWithoutRefund_AndCannotTargetWhenPaused()
        {
            EquipBombardment();
            LogAssert.Expect(LogType.Error, RpcUnavailable);
            Assert.That(_match.TryBeginBombardmentOnServer(_player), Is.True);
            Set(_match, "_playerPauseActive", true);
            Assert.That(_match.TryChooseBombardmentTargetOnServer(_player, new Vector3(0, 0, 3), 0, 1), Is.False);
            typeof(NetworkPlayerAvatar).GetMethod("TickBombardmentSelection", Private).Invoke(_player, null);
            Assert.That(_player.IsSettingBombardment, Is.True);
            Set(_match, "_playerPauseActive", false);
            Set(_match, "_flowState", (byte)BoardFlowState.AscendingResolve);
            typeof(NetworkPlayerAvatar).GetMethod("TickBombardmentSelection", Private).Invoke(_player, null);
            Assert.That(_player.IsSettingBombardment, Is.False);
            Assert.That(Get<byte>(_player, "_occupiedItemMask"), Is.Zero);
        }
    }
}
