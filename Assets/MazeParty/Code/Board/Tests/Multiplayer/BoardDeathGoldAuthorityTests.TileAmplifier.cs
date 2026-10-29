using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.TestTools;

namespace MazeParty.Multiplayer.Tests
{
    public sealed partial class BoardDeathGoldAuthorityTests
    {
        [Test]
        public void TileAmplifier_ScalesAllPlayersGoldAndHealth_ButNeverSpecialEvents()
        {
            Set(_match, "_boardMapId", new FixedString64Bytes(MagicMazeRules.MapId));
            Set(_match, "_turn", 2); Set(_match, "_tileAmplifierTurn", 2); Set(_match, "_tileAmplifierUsers", (byte)3);
            var effects = (BoardLandingEffectType[])typeof(NetworkMatchState).GetField("_landingEffectPlan", Private).GetValue(_match);
            var tiles = (BoardTile[])typeof(NetworkMatchState).GetField("_landingEffectTiles", Private).GetValue(_match);
            var events = (BoardSpecialEventRules.Resolution[])typeof(NetworkMatchState).GetField("_specialEventPlan", Private).GetValue(_match);
            for (var slot = 0; slot < 4; slot++)
            {
                var tileObject = new GameObject("Amplified tile " + slot); tileObject.transform.SetParent(_root.transform);
                tiles[slot] = tileObject.AddComponent<BoardTile>(); tiles[slot].Configure(new Vector2Int(slot, 0), BoardTileType.Normal);
                Set(_players[slot], "_maxHealth", 200); Set(_players[slot], "_currentHealth", 150);
            }
            foreach (var effect in new[] { BoardLandingEffectType.GoldGain, BoardLandingEffectType.GoldLoss,
                BoardLandingEffectType.Healing10, BoardLandingEffectType.Healing20, BoardLandingEffectType.Damage20, BoardLandingEffectType.Damage40 })
            {
                typeof(NetworkMatchState).GetField("_landingEffectAppliedMask", Private).SetValue(_match, (byte)0);
                for (var slot = 0; slot < 4; slot++)
                {
                    Set(_players[slot], "_gold", 100); Set(_players[slot], "_currentHealth", 150);
                    effects[slot] = effect;
                    if (effect == BoardLandingEffectType.Damage20 || effect == BoardLandingEffectType.Damage40)
                        LogAssert.Expect(LogType.Error, "Rpc methods can only be invoked after starting the NetworkManager!");
                    typeof(NetworkMatchState).GetMethod("ApplyLandingEffectForSlotOnServer", Private).Invoke(_match, new object[] { slot });
                    typeof(NetworkMatchState).GetMethod("ApplyLandingEffectForSlotOnServer", Private).Invoke(_match, new object[] { slot });
                    Assert.That(_players[slot].Gold, Is.EqualTo(100 + BoardLandingEffectLayout.GetGoldDelta(effect) * 3));
                    Assert.That(_players[slot].CurrentHealth, Is.EqualTo(Mathf.Min(200, 150 + BoardLandingEffectLayout.GetHealthDelta(effect) * 3)));
                }
            }
            typeof(NetworkMatchState).GetField("_landingEffectAppliedMask", Private).SetValue(_match, (byte)0);
            effects[0] = BoardLandingEffectType.SpecialEvent;
            events[0] = new BoardSpecialEventRules.Resolution(BoardSpecialEventFamily.Direct, BoardSpecialEventAudience.Everyone,
                BoardSpecialEventOperation.Gain, BoardSpecialEventResource.Gold, -1, 10);
            typeof(NetworkMatchState).GetMethod("ApplyLandingEffectForSlotOnServer", Private).Invoke(_match, new object[] { 0 });
            foreach (var player in _players) Assert.That(player.Gold, Is.EqualTo(110));
        }
    }
}
