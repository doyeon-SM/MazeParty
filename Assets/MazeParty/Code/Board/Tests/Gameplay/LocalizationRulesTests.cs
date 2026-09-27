using System;
using NUnit.Framework;

namespace MazeParty.Gameplay.Tests
{
    public sealed class LocalizationRulesTests
    {
        private const string Csv =
            "﻿source,ko,ja,zh-Hans\r\n" +
            "TURN {0},턴 {0},ターン {0},回合 {0}\r\n" +
            "\"Hello, world\",안녕,こんにちは,你好\r\n" +
            "Line\\nBreak,줄\\n바꿈,,\r\n" +
            "Broken {0},깨짐 {1},,\r\n";

        [TearDown]
        public void TearDown()
        {
            GameText.ResetForTests();
        }

        [Test]
        public void Parse_ReadsQuotedCellsEscapesAndBom()
        {
            var table = StringTable.Parse(Csv);
            Assert.That(table.Count, Is.EqualTo(4));
            Assert.That(table.TryGet("Hello, world", GameLanguage.Korean, out var hello), Is.True);
            Assert.That(hello, Is.EqualTo("안녕"));
            Assert.That(table.TryGet("Line\nBreak", GameLanguage.Korean, out var line), Is.True);
            Assert.That(line, Is.EqualTo("줄\n바꿈"));
            Assert.That(table.TryGet("Line\nBreak", GameLanguage.Japanese, out _), Is.False,
                "An empty cell falls back to English.");
        }

        [Test]
        public void Parse_RejectsDuplicatesAndMissingSourceColumn()
        {
            Assert.Throws<FormatException>(() => StringTable.Parse("source,ko\nA,가\nA,나\n"));
            Assert.Throws<FormatException>(() => StringTable.Parse("text,ko\nA,가\n"));
        }

        [Test]
        public void Escape_RoundTripsLineBreaksAndBackslashes()
        {
            const string text = "a\\b\nc";
            Assert.That(StringTable.Unescape(StringTable.Escape(text)), Is.EqualTo(text));
        }

        [Test]
        public void GameText_TranslatesFormatsAndFallsBack()
        {
            GameText.UseTableForTests(StringTable.Parse(Csv));
            Assert.That(GameText.T("TURN {0}"), Is.EqualTo("TURN {0}"));
            Assert.That(GameText.F("TURN {0}", 3), Is.EqualTo("TURN 3"));

            GameText.SetLanguage(GameLanguage.Korean);
            Assert.That(GameText.F("TURN {0}", 3), Is.EqualTo("턴 3"));
            Assert.That(GameText.T("Unknown line"), Is.EqualTo("Unknown line"));
            Assert.That(GameText.F("Broken {0}", 7), Is.EqualTo("Broken 7"),
                "A translation with bad placeholders falls back to the English format.");
            Assert.That(GameText.N("TURN {0}"), Is.EqualTo("TURN {0}"),
                "N only marks a source; it never translates.");

            GameText.SetLanguage(GameLanguage.Japanese);
            Assert.That(GameText.F("TURN {0}", 3), Is.EqualTo("ターン 3"));
        }

        [Test]
        public void GameText_UsesInvariantNumberFormatting()
        {
            GameText.UseTableForTests(StringTable.Empty);
            Assert.That(GameText.F("{0:0.0}s", 1.25), Is.EqualTo("1.3s").Or.EqualTo("1.2s"));
            Assert.That(GameText.F("{0:0.0}s", 1.5), Does.Not.Contain(","));
        }

        [Test]
        public void GameText_RaisesLanguageChangedOnlyOnChange()
        {
            var raised = 0;
            Action handler = () => raised++;
            GameText.LanguageChanged += handler;
            try
            {
                GameText.SetLanguage(GameLanguage.English);
                GameText.SetLanguage(GameLanguage.ChineseSimplified);
                GameText.SetLanguage(GameLanguage.ChineseSimplified);
            }
            finally
            {
                GameText.LanguageChanged -= handler;
            }

            Assert.That(raised, Is.EqualTo(1));
        }

        [Test]
        public void LocalizedMessage_ResolvesAgainAfterLanguageChanges()
        {
            GameText.UseTableForTests(StringTable.Parse(Csv));
            var message = new LocalizedMessage("TURN {0}", 3);

            Assert.That(message.Resolve(), Is.EqualTo("TURN 3"));
            GameText.SetLanguage(GameLanguage.Korean);
            Assert.That(message.Resolve(), Is.EqualTo("턴 3"));
        }

        [Test]
        public void Languages_HaveStableIndicesAndColumns()
        {
            Assert.That(GameLanguages.Count, Is.EqualTo(4));
            Assert.That(GameLanguages.GetColumnId(GameLanguage.Korean), Is.EqualTo("ko"));
            Assert.That(GameLanguages.GetColumnId(GameLanguage.Japanese), Is.EqualTo("ja"));
            Assert.That(GameLanguages.GetColumnId(GameLanguage.ChineseSimplified), Is.EqualTo("zh-Hans"));
            for (var index = 0; index < GameLanguages.Count; index++)
            {
                Assert.That(GameLanguages.ToIndex(GameLanguages.FromIndex(index)), Is.EqualTo(index));
                Assert.That(GameLanguages.GetNativeName(GameLanguages.FromIndex(index)), Is.Not.Empty);
            }

            Assert.That(GameLanguages.Sanitize((GameLanguage)200), Is.EqualTo(GameLanguage.English));
        }

        [Test]
        public void Audio_ChannelVolumesAreClampedAndIndependent()
        {
            try
            {
                GameAudio.SetVolumes(0.5f, 2f, -1f);
                Assert.That(GameAudio.MasterVolume, Is.EqualTo(0.5f));
                Assert.That(GameAudio.GetChannelVolume(AudioChannel.Sfx), Is.EqualTo(1f));
                Assert.That(GameAudio.GetChannelVolume(AudioChannel.Bgm), Is.EqualTo(0f));
                Assert.That(GameAudio.Sanitize(float.NaN), Is.EqualTo(GameAudio.DefaultVolume));
            }
            finally
            {
                GameAudio.SetVolumes(1f, 1f, 1f);
            }
        }
    }
}
