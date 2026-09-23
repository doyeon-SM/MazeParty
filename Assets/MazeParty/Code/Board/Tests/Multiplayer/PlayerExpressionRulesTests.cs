using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class PlayerExpressionRulesTests
    {
        [Test]
        public void ProfileMigration_PreservesOldColorHat_AndRoundTripsEveryFace()
        {
            var old = PlayerProfilePreferences.Decode("{\"version\":1,\"displayName\":\"Legacy\",\"bodyRed\":55,\"bodyGreen\":125,\"bodyBlue\":230,\"hatId\":1}", "Fallback");
            Assert.That(old.DisplayName, Is.EqualTo("Legacy"));
            Assert.That(old.Appearance.ExpressionId, Is.Zero);
            Assert.That(old.Appearance.HatId, Is.EqualTo(1));
            Assert.That((Color32)old.Appearance.BodyColor, Is.EqualTo(LobbyColorPalette.GetColor(4)));
            for (byte i = 0; i < 4; i++)
            {
                var appearance = PlayerAppearanceState.FromColor(Color.red, 0, 0, 1, 0, i);
                var restored = PlayerProfilePreferences.Decode(PlayerProfilePreferences.Encode("Saved", appearance), "Fallback");
                Assert.That(restored.Appearance, Is.EqualTo(appearance));
                Assert.That(restored.Appearance.WithPaletteColor(3).ExpressionId, Is.EqualTo(i));
            }
            foreach (var json in new[] { "broken", "null", "{\"version\":99}", "{\"version\":0}" })
                Assert.That(PlayerProfilePreferences.Decode(json, "Fallback").Appearance, Is.EqualTo(PlayerAppearanceState.Default));
            Assert.That(PlayerAppearanceState.FromColor(Color.red, 0, 0, 0, 0, 255).ExpressionId, Is.Zero);
        }
        [Test]
        public void HandGesture_ExpiresAtOneSecond_RejectsExtensionAndDisallowedStarts()
        {
            Assert.That(HandEmoteRules.CanStart(10, 10, true, true), Is.True);
            Assert.That(HandEmoteRules.IsActive(1, 11, 10.9999), Is.True);
            Assert.That(HandEmoteRules.IsActive(1, 11, 11), Is.False);
            Assert.That(HandEmoteRules.IsActive(0, 11, 10), Is.False);
            Assert.That(HandEmoteRules.CanStart(10.9, 11, true, true), Is.False);
            foreach (var flags in new[] { new[] {false,true}, new[] {true,false}, new[] {false,false} })
                Assert.That(HandEmoteRules.CanStart(12, 11, flags[0], flags[1]), Is.False);
            Assert.That(HandEmoteRules.Select(Vector2.zero,32,3), Is.EqualTo(-1));
            Assert.That(HandEmoteRules.Select(Vector2.up*80,32,3), Is.EqualTo(0));
            Assert.That(HandEmoteRules.Select(new Vector2(80,-50),32,3), Is.EqualTo(1));
            Assert.That(HandEmoteRules.Select(new Vector2(-80,-50),32,3), Is.EqualTo(2));
        }
    }
}
