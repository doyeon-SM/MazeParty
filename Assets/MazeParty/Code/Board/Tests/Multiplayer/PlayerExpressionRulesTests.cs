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
            var removedFace = PlayerProfilePreferences.Decode(
                "{\"version\":2,\"displayName\":\"Legacy Face\",\"bodyRed\":55,\"bodyGreen\":125,\"bodyBlue\":230,\"expressionId\":3}",
                "Fallback");
            Assert.That(removedFace.Appearance.ExpressionId, Is.Zero,
                "The removed fourth face must fall back to Face1.");
            var removedHat = PlayerProfilePreferences.Decode(
                "{\"version\":2,\"displayName\":\"Legacy Hat\",\"bodyRed\":55,\"bodyGreen\":125,\"bodyBlue\":230,\"hatId\":4}",
                "Fallback");
            Assert.That(removedHat.Appearance.HatId, Is.Zero,
                "Old profile versions did not define a fourth hat.");
            var legacyAppearance = new PlayerAppearanceState
            {
                Version = 2,
                BodyRed = 55,
                BodyGreen = 125,
                BodyBlue = 230,
                HatId = 4,
                ExpressionId = 3
            }.Sanitized();
            Assert.That(legacyAppearance.Version, Is.EqualTo(PlayerAppearanceState.CurrentVersion));
            Assert.That(legacyAppearance.HatId, Is.Zero);
            Assert.That(legacyAppearance.ExpressionId, Is.Zero);

            var catalog = PlayerExpressionCatalog.Instance;
            Assert.That(catalog, Is.Not.Null);
            for (byte face = 0; face < catalog.Faces.Length; face++)
            {
                for (byte hat = 0; hat <= catalog.Hats.Length; hat++)
                {
                    var appearance = PlayerAppearanceState.FromColor(
                        Color.red, 0, 0, hat, 0, face);
                    var encoded = PlayerProfilePreferences.Encode("Saved", appearance);
                    Assert.That(encoded, Does.Contain("\"version\":4"));
                    var restored = PlayerProfilePreferences.Decode(encoded, "Fallback");
                    Assert.That(restored.Appearance, Is.EqualTo(appearance));
                    Assert.That(
                        restored.Appearance.WithPaletteColor(3).ExpressionId,
                        Is.EqualTo(face));
                    Assert.That(restored.Appearance.HatId, Is.EqualTo(hat));
                }
            }
            foreach (var json in new[] { "broken", "null", "{\"version\":99}", "{\"version\":0}" })
                Assert.That(PlayerProfilePreferences.Decode(json, "Fallback").Appearance, Is.EqualTo(PlayerAppearanceState.Default));
            Assert.That(PlayerAppearanceState.FromColor(Color.red, 0, 0, 0, 0, 255).ExpressionId, Is.Zero);
            Assert.That(PlayerAppearanceState.FromColor(Color.red, 0, 0, 255, 0, 0).HatId, Is.Zero);

            var legacyEmotes = PlayerProfilePreferences.Decode(
                "{\"version\":3,\"displayName\":\"Legacy Emotes\",\"bodyRed\":226,\"bodyGreen\":61,\"bodyBlue\":56,\"expressionId\":5}",
                "Fallback");
            Assert.That(
                legacyEmotes.EmoteFaces,
                Is.EqualTo(HandEmoteFaceSelections.Uniform(5)));

            var selectedEmotes = new HandEmoteFaceSelections
            {
                Greeting = 1,
                Salute = 2,
                Insult = 3,
                Heart = 4,
                Surprise = 5,
                Surrender = 6,
                Pleading = 7,
                EyesCover = 8
            };
            var emoteJson = PlayerProfilePreferences.Encode(
                "Emotes",
                PlayerAppearanceState.FromColor(Color.red, 0, 0, 0, 0, 9),
                selectedEmotes);
            var emoteRoundTrip = PlayerProfilePreferences.Decode(
                emoteJson,
                "Fallback");
            Assert.That(emoteRoundTrip.EmoteFaces, Is.EqualTo(selectedEmotes));
        }
        [Test]
        public void HandGesture_UsesEightSectors_TwoSecondLock_AndValidatedWardrobeFace()
        {
            Assert.That(HandEmoteRules.CanStart(10, 10, true, true), Is.True);
            Assert.That(HandEmoteRules.Duration, Is.EqualTo(2d));
            Assert.That(HandEmoteRules.IsActive(1, 12, 11.9999), Is.True);
            Assert.That(HandEmoteRules.IsActive(1, 12, 12), Is.False);
            Assert.That(HandEmoteRules.IsActive(0, 12, 10), Is.False);
            Assert.That(HandEmoteRules.CanStart(11.9, 12, true, true), Is.False);
            foreach (var flags in new[] { new[] {false,true}, new[] {true,false}, new[] {false,false} })
                Assert.That(HandEmoteRules.CanStart(13, 12, flags[0], flags[1]), Is.False);
            Assert.That(HandEmoteRules.Select(Vector2.zero,32,8), Is.EqualTo(-1));
            Assert.That(HandEmoteRules.Select(Vector2.up*80,32,8), Is.EqualTo(0));
            Assert.That(HandEmoteRules.Select(Vector2.right*80,32,8), Is.EqualTo(2));
            Assert.That(HandEmoteRules.Select(Vector2.down*80,32,8), Is.EqualTo(4));
            Assert.That(HandEmoteRules.Select(Vector2.left*80,32,8), Is.EqualTo(6));
            Assert.That(
                HandEmoteRules.ResolveExpression(
                    (byte)HandEmoteId.Greeting, 7, 2, 15),
                Is.EqualTo(7));
            Assert.That(
                HandEmoteRules.ResolveExpression(
                    (byte)HandEmoteId.Greeting, 99, 2, 15),
                Is.EqualTo(2));
            Assert.That(
                HandEmoteRules.ResolveExpression(
                    (byte)HandEmoteId.EyesCover, 7, 2, 15),
                Is.EqualTo(2));
        }
    }
}
