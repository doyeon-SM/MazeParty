using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class SoundRulesTests
    {
        [Test]
        public void VariationPicker_StaysInRangeCoversEveryClipAndAvoidsImmediateRepeats()
        {
            var random = new Random(1234);
            foreach (SoundVariationMode mode in Enum.GetValues(typeof(SoundVariationMode)))
            {
                for (var count = 0; count <= 5; count++)
                {
                    var picker = new SoundVariationPicker();
                    var previous = -1;
                    var seen = new HashSet<int>();
                    for (var play = 0; play < 60; play++)
                    {
                        var index = picker.Next(count, mode, random);
                        if (count == 0)
                        {
                            Assert.That(index, Is.EqualTo(-1), mode + " with no clips");
                            continue;
                        }

                        Assert.That(index, Is.InRange(0, count - 1), mode + " x" + count);
                        if (count > 1 && mode != SoundVariationMode.Random)
                        {
                            Assert.That(index, Is.Not.EqualTo(previous), mode + " repeated a clip");
                        }

                        if (mode == SoundVariationMode.Sequential && previous >= 0)
                        {
                            Assert.That(index, Is.EqualTo((previous + 1) % count));
                        }

                        seen.Add(index);
                        previous = index;
                    }

                    Assert.That(seen.Count, Is.EqualTo(count), mode + " skipped a clip");
                }
            }

            // Shuffle plays every clip once per round.
            var shuffle = new SoundVariationPicker();
            for (var round = 0; round < 20; round++)
            {
                var block = Enumerable.Range(0, 4)
                    .Select(_ => shuffle.Next(4, SoundVariationMode.Shuffle, random))
                    .ToArray();
                Assert.That(block.Distinct().Count(), Is.EqualTo(4), "shuffle round " + round);
            }

            // A cue whose clip list shrank keeps working.
            var shrinking = new SoundVariationPicker();
            shrinking.Next(5, SoundVariationMode.Sequential, random);
            shrinking.Next(5, SoundVariationMode.Sequential, random);
            shrinking.Next(5, SoundVariationMode.Sequential, random);
            Assert.That(
                shrinking.Next(2, SoundVariationMode.RandomNoRepeat, random),
                Is.InRange(0, 1));
        }

        [Test]
        public void VoiceRules_LimitInstancesAndStealOnlyLowerPriorityOneShots()
        {
            Assert.That(SoundVoiceRules.PassesInterval(1.00, 0.97, 0.05f), Is.False);
            Assert.That(SoundVoiceRules.PassesInterval(1.00, 0.95, 0.05f), Is.True);
            Assert.That(SoundVoiceRules.PassesInterval(1.00, -1d, 0.05f), Is.True);
            Assert.That(SoundVoiceRules.PassesInterval(1.00, 1.00, 0f), Is.True);

            // A free voice is used first.
            Assert.That(SoundVoiceRules.ChooseVoice(
                new[] { Voice(true, 1, 50, 0.0), Voice(false, 0, 0, 0.0), Voice(true, 2, 50, 0.1) },
                cueId: 3, maxInstances: 0, priority: 50), Is.EqualTo(1));

            // A cue at its limit replaces its own oldest instance.
            Assert.That(SoundVoiceRules.ChooseVoice(
                new[] { Voice(true, 7, 70, 0.3), Voice(true, 7, 70, 0.1), Voice(false, 0, 0, 0.0) },
                cueId: 7, maxInstances: 2, priority: 70), Is.EqualTo(1));

            // All busy: the lowest priority, then the oldest, is stolen.
            Assert.That(SoundVoiceRules.ChooseVoice(
                new[] { Voice(true, 1, 60, 0.1), Voice(true, 2, 20, 0.5), Voice(true, 3, 20, 0.2) },
                cueId: 9, maxInstances: 0, priority: 50), Is.EqualTo(2));

            // Higher-priority sounds and loops are never stolen.
            Assert.That(SoundVoiceRules.ChooseVoice(
                new[] { Voice(true, 1, 80, 0.1), Voice(true, 2, 10, 0.0, loop: true) },
                cueId: 9, maxInstances: 0, priority: 50), Is.EqualTo(-1));
        }

        [Test]
        public void Keys_CoverEveryMinigameAndItemAndMapToChannelsAndDecibels()
        {
            var keys = SoundKeys.All();
            Assert.That(keys.Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(keys.Count));
            foreach (var key in keys)
            {
                Assert.That(Regex.IsMatch(key, @"^[a-z0-9_]+(\.[a-z0-9_]+)+$"), Is.True, key);
            }

            foreach (var definition in MinigameCatalog.RegisteredMinigames)
            {
                Assert.That(keys, Does.Contain(SoundKeys.MinigameBgm(definition.Id)));
            }

            Assert.That(
                SoundKeys.MinigameBgm(ScheduledMinigameId.ArenaCombat),
                Is.EqualTo("bgm.minigame.arena_combat"));
            Assert.That(
                SoundKeys.ItemUse(PrototypeItemId.PositionSwapper),
                Is.EqualTo("item.use.position_swapper"));
            Assert.That(SoundKeys.CombatHit(PlayerHitRegion.Head), Is.EqualTo(SoundKeys.CombatHitHead));

            Assert.That(SoundKeys.GetChannel(SoundKeys.BgmBoard), Is.EqualTo(AudioChannel.Bgm));
            Assert.That(
                SoundKeys.GetChannel(SoundKeys.MinigameBgm(ScheduledMinigameId.Race)),
                Is.EqualTo(AudioChannel.Bgm));
            Assert.That(SoundKeys.GetChannel(SoundKeys.UiClick), Is.EqualTo(AudioChannel.Ui));
            Assert.That(SoundKeys.GetChannel(SoundKeys.CombatHitHead), Is.EqualTo(AudioChannel.Sfx));

            Assert.That(GameAudio.ToDecibels(1f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(GameAudio.ToDecibels(0.5f), Is.EqualTo(-6.02f).Within(0.01f));
            Assert.That(GameAudio.ToDecibels(0f), Is.EqualTo(GameAudio.MinDecibels));
            Assert.That(GameAudio.ToDecibels(float.NaN), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Bgm_FollowsTheGameFlowAndFallsBackToTracksWithClips()
        {
            var arena = ScheduledMinigameId.ArenaCombat;
            var skip = ScheduledMinigameId.Skip;
            var cases = new (bool Session, bool Match, bool Back, BoardFlowState Flow, bool Ceremony,
                ScheduledMinigameId Minigame, BgmScene Expected)[]
            {
                (false, false, false, BoardFlowState.TurnOverview, false, skip, BgmScene.Lobby),
                (true, false, false, BoardFlowState.TurnOverview, false, skip, BgmScene.WaitingRoom),
                (true, true, false, BoardFlowState.Action, false, arena, BgmScene.Board),
                (true, true, false, BoardFlowState.MinigameIntroReady, false, arena, BgmScene.Board),
                (true, true, false, BoardFlowState.MinigameLoading, false, arena, BgmScene.Minigame),
                (true, true, false, BoardFlowState.MinigamePlaying, false, arena, BgmScene.Minigame),
                (true, true, false, BoardFlowState.MinigameResult, false, arena, BgmScene.Minigame),
                (true, true, false, BoardFlowState.MinigameResult, false, skip, BgmScene.Board),
                (true, true, false, BoardFlowState.MatchComplete, true, arena, BgmScene.Ceremony),
                (true, true, true, BoardFlowState.MatchComplete, true, arena, BgmScene.WaitingRoom)
            };
            foreach (var test in cases)
            {
                Assert.That(
                    BgmTrackRules.Resolve(
                        test.Session, test.Match, test.Back, test.Flow, test.Ceremony, test.Minigame),
                    Is.EqualTo(test.Expected),
                    test.ToString());
            }

            var withClips = new HashSet<string> { SoundKeys.BgmMinigame, SoundKeys.BgmBoard };
            var minigameTracks = BgmTrackRules.Candidates(BgmScene.Minigame, arena);
            Assert.That(minigameTracks[0], Is.EqualTo(SoundKeys.MinigameBgm(arena)));
            Assert.That(BgmTrackRules.Pick(minigameTracks, withClips.Contains), Is.EqualTo(SoundKeys.BgmMinigame));
            withClips.Add(SoundKeys.MinigameBgm(arena));
            Assert.That(
                BgmTrackRules.Pick(minigameTracks, withClips.Contains),
                Is.EqualTo(SoundKeys.MinigameBgm(arena)));
            Assert.That(
                BgmTrackRules.Pick(BgmTrackRules.Candidates(BgmScene.Lobby, skip), withClips.Contains),
                Is.Null);
        }

        [Test]
        public void Feedback_ClassifiesWalletAndFlowChanges()
        {
            // gold, keys, items: before → after.
            Assert.That(BoardFeedbackSoundRules.ClassifyWalletChange(30, 10, 1, 2, 0, 0), Is.EqualTo(SoundKeys.BoardKeyBuy));
            Assert.That(BoardFeedbackSoundRules.ClassifyWalletChange(30, 25, 1, 1, 0, 1), Is.EqualTo(SoundKeys.BoardShopBuy));
            Assert.That(BoardFeedbackSoundRules.ClassifyWalletChange(10, 13, 0, 0, 1, 1), Is.EqualTo(SoundKeys.BoardGoldGain));
            Assert.That(BoardFeedbackSoundRules.ClassifyWalletChange(10, 5, 0, 0, 1, 1), Is.EqualTo(SoundKeys.BoardGoldLoss));
            Assert.That(BoardFeedbackSoundRules.ClassifyWalletChange(10, 10, 0, 0, 2, 1), Is.Null);

            Assert.That(
                BoardFeedbackSoundRules.ClassifyFlowTransition(BoardFlowState.MinigameResult, BoardFlowState.TurnOverview),
                Is.EqualTo(SoundKeys.BoardTurnStart));
            Assert.That(
                BoardFeedbackSoundRules.ClassifyFlowTransition(BoardFlowState.MinigamePlaying, BoardFlowState.MinigameResult),
                Is.EqualTo(SoundKeys.MinigameFinish));
            Assert.That(
                BoardFeedbackSoundRules.ClassifyFlowTransition(BoardFlowState.MinigameIntroReady, BoardFlowState.MinigameResult),
                Is.Null);
            Assert.That(
                BoardFeedbackSoundRules.ClassifyFlowTransition(BoardFlowState.Action, BoardFlowState.Action),
                Is.Null);
        }

        private static SoundVoiceState Voice(
            bool busy,
            int cueId,
            int priority,
            double startedAt,
            bool loop = false)
        {
            return new SoundVoiceState(busy, cueId, priority, startedAt, loop);
        }
    }
}
