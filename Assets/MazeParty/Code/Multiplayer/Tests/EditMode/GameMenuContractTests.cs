using System.Collections.Generic;
using System.Linq;
using System.Text;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    /// <summary>
    /// Contract for the common settings/pause/leave menu shared by the lobby,
    /// the waiting room and the match.
    /// </summary>
    public sealed class GameMenuContractTests
    {
        private const string MenuPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/GameMenuCanvas.prefab";
        private const string BootstrapScenePath =
            "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity";

        [TearDown]
        public void TearDown()
        {
            GameText.ResetForTests();
        }

        [Test]
        public void MenuPrefab_HasCompleteBindingsAndHiddenPopupsByDefault()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
            Assert.That(prefab, Is.Not.Null, MenuPrefabPath);
            var bindings = prefab.GetComponent<GameMenuBindings>();
            var view = prefab.GetComponent<GameMenuView>();
            Assert.That(bindings, Is.Not.Null);
            Assert.That(view, Is.Not.Null);
            Assert.That(bindings.HasRequiredReferences, Is.True);
            Assert.That(view.Bindings, Is.SameAs(bindings));
            Assert.That(prefab.GetComponentsInChildren<Canvas>(true), Has.Length.EqualTo(1));
            Assert.That(bindings.RootCanvas.sortingOrder, Is.GreaterThanOrEqualTo(1000),
                "The menu must draw above every gameplay Canvas.");

            Assert.That(bindings.MenuRoot.activeSelf, Is.False);
            Assert.That(bindings.ConfirmRoot.activeSelf, Is.False);
            Assert.That(bindings.NoticeRoot.activeSelf, Is.False);
            Assert.That(bindings.PauseBannerRoot.activeSelf, Is.False);
            Assert.That(bindings.GearButton.gameObject.activeSelf, Is.True);

            Assert.That(bindings.MasterSlider.minValue, Is.EqualTo(0f));
            Assert.That(bindings.MasterSlider.maxValue, Is.EqualTo(1f));
            Assert.That(bindings.SfxSlider.maxValue, Is.EqualTo(1f));
            Assert.That(bindings.BgmSlider.maxValue, Is.EqualTo(1f));
            Assert.That(bindings.MouseSensitivitySlider.minValue,
                Is.EqualTo(GameSettingsData.MinimumMouseSensitivity));
            Assert.That(bindings.MouseSensitivitySlider.maxValue,
                Is.EqualTo(GameSettingsData.MaximumMouseSensitivity));
        }

        [Test]
        public void MenuPrefab_ApplyLabelFitsEveryLanguageFont()
        {
            var root = PrefabUtility.LoadPrefabContents(MenuPrefabPath);
            try
            {
                var bindings = root.GetComponent<GameMenuBindings>();
                var label = bindings.ApplyButton.transform.Find("Label")
                    .GetComponent<Text>();

                foreach (var language in new[]
                         {
                             GameLanguage.English,
                             GameLanguage.Korean,
                             GameLanguage.Japanese,
                             GameLanguage.ChineseSimplified
                         })
                {
                    GameText.SetLanguage(language);
                    label.font = GameFonts.Get(language);
                    label.text = GameText.T("Apply");
                    var generator = new TextGenerator();
                    var settings = label.GetGenerationSettings(
                        label.rectTransform.rect.size);
                    Assert.That(generator.Populate(label.text, settings), Is.True,
                        language + " Apply label could not be generated.");
                    Assert.That(generator.characterCountVisible,
                        Is.EqualTo(label.text.Length),
                        language + " Apply label is not fully visible.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void BootstrapScene_HasExactlyOneMenuPrefabInstance()
        {
            var scene = SceneManager.GetSceneByPath(BootstrapScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(BootstrapScenePath, OpenSceneMode.Additive);
            }

            try
            {
                var views = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<GameMenuView>(true))
                    .ToArray();
                Assert.That(views, Has.Length.EqualTo(1));
                Assert.That(views[0].transform.parent, Is.Null);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(views[0].gameObject),
                    Is.EqualTo(MenuPrefabPath));
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void MenuRules_ChooseExitBehaviourAndButtonsPerContext()
        {
            Assert.That(GameMenuRules.GetExitAction(GameMenuContext.Lobby),
                Is.EqualTo(GameMenuExitAction.QuitApplication));
            Assert.That(GameMenuRules.GetExitAction(GameMenuContext.WaitingRoom),
                Is.EqualTo(GameMenuExitAction.LeaveWaitingRoom));
            Assert.That(GameMenuRules.GetExitAction(GameMenuContext.InGame),
                Is.EqualTo(GameMenuExitAction.ConfirmMatchLeave));

            Assert.That(GameMenuRules.GetExitLabelSource(GameMenuContext.Lobby), Is.EqualTo("Quit Game"));
            Assert.That(GameMenuRules.GetExitLabelSource(GameMenuContext.WaitingRoom), Is.EqualTo("Leave Game"));
            Assert.That(GameMenuRules.GetExitLabelSource(GameMenuContext.InGame), Is.EqualTo("Leave Game"));

            Assert.That(GameMenuRules.ShowsPauseButton(GameMenuContext.InGame), Is.True);
            Assert.That(GameMenuRules.ShowsPauseButton(GameMenuContext.WaitingRoom), Is.False);
            Assert.That(GameMenuRules.ShowsGearButton(GameMenuContext.Lobby), Is.True);
            Assert.That(GameMenuRules.ShowsGearButton(GameMenuContext.WaitingRoom), Is.True);
            Assert.That(GameMenuRules.ShowsGearButton(GameMenuContext.InGame), Is.False);
        }

        [Test]
        public void PauseClock_RoundsUpToWholeSeconds()
        {
            var cases = new[]
            {
                (seconds: 300d, expected: "5:00"),
                (seconds: 299.2d, expected: "5:00"),
                (seconds: 61d, expected: "1:01"),
                (seconds: 0.1d, expected: "0:01"),
                (seconds: 0d, expected: "0:00"),
                (seconds: -3d, expected: "0:00")
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    GameMenuRules.FormatPauseClock(testCase.seconds),
                    Is.EqualTo(testCase.expected),
                    testCase.seconds.ToString());
            }
        }

        [Test]
        public void VoluntaryLeave_ReturnsRemainingPlayersUnlessHostOrAlreadyReturning()
        {
            Assert.That(VoluntaryLeaveRules.Resolve(false, false, false),
                Is.EqualTo(VoluntaryLeaveDisposition.Ignore));
            Assert.That(VoluntaryLeaveRules.Resolve(true, false, false),
                Is.EqualTo(VoluntaryLeaveDisposition.ReturnRemainingPlayersToLobby));
            Assert.That(VoluntaryLeaveRules.Resolve(true, true, false),
                Is.EqualTo(VoluntaryLeaveDisposition.AcknowledgeOnly));
            Assert.That(VoluntaryLeaveRules.Resolve(true, false, true),
                Is.EqualTo(VoluntaryLeaveDisposition.AcknowledgeOnly));
        }

        [Test]
        public void VoluntaryLeave_AfterFinalRankingLeavesWithoutEndingTheCeremony()
        {
            Assert.That(VoluntaryLeaveRules.Resolve(true, false, false, finalRankingLocked: true),
                Is.EqualTo(VoluntaryLeaveDisposition.LeaveCompletedMatch));
            // A guest leaving while the room return is starting is still not
            // announced as having ended the game.
            Assert.That(VoluntaryLeaveRules.Resolve(true, false, true, finalRankingLocked: true),
                Is.EqualTo(VoluntaryLeaveDisposition.LeaveCompletedMatch));
            // The host still closes the room; hosts do not migrate.
            Assert.That(VoluntaryLeaveRules.Resolve(true, true, false, finalRankingLocked: true),
                Is.EqualTo(VoluntaryLeaveDisposition.AcknowledgeOnly));
        }

        [Test]
        public void DisplayModeSelector_WrapsInBothDirections()
        {
            Assert.That(DisplayModeOptions.Step(DisplayModeOption.Windowed, -1),
                Is.EqualTo(DisplayModeOption.BorderlessFullscreen));
            Assert.That(DisplayModeOptions.Step(DisplayModeOption.BorderlessFullscreen, 1),
                Is.EqualTo(DisplayModeOption.Windowed));
            for (var index = 0; index < DisplayModeOptions.Count; index++)
            {
                var option = (DisplayModeOption)index;
                Assert.That(DisplayModeOptions.FromFullScreenMode(
                        DisplayModeOptions.ToFullScreenMode(option)),
                    Is.EqualTo(option));
                Assert.That(DisplayModeOptions.GetLabelSource(option), Is.Not.Empty);
            }

            Assert.That(DisplayModeOptions.Sanitize((DisplayModeOption)9),
                Is.EqualTo(DisplayModeOption.BorderlessFullscreen));
        }

        [Test]
        public void SettingsData_SanitizesOutOfRangeValues()
        {
            var data = new GameSettingsData(
                2f,
                -1f,
                float.NaN,
                (GameLanguage)77,
                (DisplayModeOption)12).Sanitized();
            Assert.That(data.MasterVolume, Is.EqualTo(1f));
            Assert.That(data.SfxVolume, Is.EqualTo(0f));
            Assert.That(data.BgmVolume, Is.EqualTo(GameAudio.DefaultVolume));
            Assert.That(data.Language, Is.EqualTo(GameLanguage.English));
            Assert.That(data.DisplayMode, Is.EqualTo(DisplayModeOption.BorderlessFullscreen));
            Assert.That(GameSettingsData.Default.Language, Is.EqualTo(GameLanguage.English));
            Assert.That(data, Is.EqualTo(data.Sanitized()));
        }

        [Test]
        public void Settings_PersistenceRoundTripIncludesGraphicsInputAndAccessibility()
        {
            var store = new MemorySettingsStore();
            var expected = new GameSettingsData(
                0.8f, 0.7f, 0.6f, GameLanguage.Korean,
                ResolutionOption.Qhd1440, DisplayModeOption.Windowed,
                QualityPresetOption.Low, FrameRateCapOption.Fps30,
                1.35f, true, true, true);

            GameSettings.Save(store, expected);
            var loaded = GameSettings.Load(store);

            Assert.That(loaded, Is.EqualTo(expected));
            Assert.That(store.SaveCount, Is.EqualTo(1));
        }

        [Test]
        public void Settings_LoadRejectsRawEnumIntegersBeforeByteConversion()
        {
            var store = new MemorySettingsStore();
            store.SetInt(GameSettings.LanguageKey, 257);
            store.SetInt(GameSettings.ResolutionKey, 256);
            store.SetInt(GameSettings.DisplayModeKey, 256);
            store.SetInt(GameSettings.QualityPresetKey, 256);
            store.SetInt(GameSettings.FrameRateCapKey, 256);

            var loaded = GameSettings.Load(store);
            var defaults = GameSettingsData.Default;

            Assert.That(loaded.Language, Is.EqualTo(defaults.Language));
            Assert.That(loaded.Resolution, Is.EqualTo(defaults.Resolution));
            Assert.That(loaded.DisplayMode, Is.EqualTo(defaults.DisplayMode));
            Assert.That(loaded.QualityPreset, Is.EqualTo(defaults.QualityPreset));
            Assert.That(loaded.FrameRateCap, Is.EqualTo(defaults.FrameRateCap));
        }

        [Test]
        public void Settings_PlatformChangesContainOnlyChangedExpensiveFields()
        {
            var original = GameSettingsData.Default;
            var nonPlatform = original;
            nonPlatform.MasterVolume = 0.5f;
            nonPlatform.MouseSensitivity = 1.5f;
            nonPlatform.ReduceFlashes = true;
            Assert.That(GameSettings.GetPlatformChanges(original, nonPlatform),
                Is.EqualTo(GameSettingsPlatformChanges.None));

            var display = original;
            display.Resolution = ResolutionOption.Hd720;
            Assert.That(GameSettings.GetPlatformChanges(original, display),
                Is.EqualTo(GameSettingsPlatformChanges.Display));

            var quality = original;
            quality.QualityPreset = QualityPresetOption.Low;
            Assert.That(GameSettings.GetPlatformChanges(original, quality),
                Is.EqualTo(GameSettingsPlatformChanges.Quality));

            var frameRate = original;
            frameRate.FrameRateCap = FrameRateCapOption.Fps30;
            Assert.That(GameSettings.GetPlatformChanges(original, frameRate),
                Is.EqualTo(GameSettingsPlatformChanges.FrameRate));

            var all = original;
            all.DisplayMode = DisplayModeOption.Windowed;
            all.QualityPreset = QualityPresetOption.Low;
            all.FrameRateCap = FrameRateCapOption.Unlimited;
            Assert.That(GameSettings.GetPlatformChanges(original, all),
                Is.EqualTo(GameSettingsPlatformChanges.All));
        }

        [Test]
        public void Settings_NormalizeForDisplayAlignsStoredOptionAndApplicationPlan()
        {
            var cases = new[]
            {
                (ResolutionOption.Uhd2160, 1920, 1080, ResolutionOption.FullHd1080),
                (ResolutionOption.Qhd1440, 2000, 1200, ResolutionOption.FullHd1080),
                (ResolutionOption.HdPlus900, 1366, 768, ResolutionOption.Hd720),
                (ResolutionOption.Qhd1440, 3840, 2160, ResolutionOption.Qhd1440),
                (ResolutionOption.Uhd2160, 1024, 600, ResolutionOption.Hd720)
            };

            foreach (var testCase in cases)
            {
                var data = GameSettingsData.Default;
                data.Resolution = testCase.Item1;

                var normalized = GameSettings.NormalizeForDisplay(
                    data,
                    testCase.Item2,
                    testCase.Item3);
                var plan = GameSettings.CreateApplicationPlan(
                    normalized,
                    testCase.Item2,
                    testCase.Item3);
                var expectedSize = ResolutionOptions.GetSize(testCase.Item4);

                Assert.That(normalized.Resolution, Is.EqualTo(testCase.Item4));
                Assert.That(plan.Width, Is.EqualTo(expectedSize.x));
                Assert.That(plan.Height, Is.EqualTo(expectedSize.y));
            }
        }

        [Test]
        public void Settings_ExclusiveFullscreenUsesSupportedPresetsAcrossModeBoundaries()
        {
            var without900 = new[]
            {
                new Vector2Int(1280, 720),
                new Vector2Int(1920, 1080)
            };
            var with900 = new[]
            {
                new Vector2Int(1280, 720),
                new Vector2Int(1600, 900),
                new Vector2Int(1920, 1080)
            };
            var cases = new[]
            {
                (DisplayModeOption.Fullscreen, without900, ResolutionOption.Hd720),
                (DisplayModeOption.Fullscreen, with900, ResolutionOption.HdPlus900),
                (DisplayModeOption.Windowed, without900, ResolutionOption.HdPlus900),
                (DisplayModeOption.BorderlessFullscreen, without900,
                    ResolutionOption.HdPlus900)
            };

            foreach (var testCase in cases)
            {
                var data = GameSettingsData.Default;
                data.DisplayMode = testCase.Item1;
                data.Resolution = ResolutionOption.HdPlus900;

                var normalized = GameSettings.NormalizeForDisplay(
                    data,
                    1920,
                    1080,
                    testCase.Item2);
                var plan = GameSettings.CreateApplicationPlan(
                    normalized,
                    1920,
                    1080,
                    testCase.Item2);
                var expectedSize = ResolutionOptions.GetSize(testCase.Item3);

                Assert.That(normalized.Resolution, Is.EqualTo(testCase.Item3),
                    testCase.Item1.ToString());
                Assert.That(plan.Width, Is.EqualTo(expectedSize.x),
                    testCase.Item1.ToString());
                Assert.That(plan.Height, Is.EqualTo(expectedSize.y),
                    testCase.Item1.ToString());
            }
        }

        [Test]
        public void Settings_ApplicationPlanClampsResolutionAndAppliesEveryPlatformChoice()
        {
            var data = new GameSettingsData(
                1f, 1f, 1f, GameLanguage.English,
                ResolutionOption.Uhd2160, DisplayModeOption.Fullscreen,
                QualityPresetOption.Low, FrameRateCapOption.Fps30,
                1f, false, false, false);

            var plan = GameSettings.CreateApplicationPlan(data, 1920, 1080);

            Assert.That(plan.Width, Is.EqualTo(1920));
            Assert.That(plan.Height, Is.EqualTo(1080));
            Assert.That(plan.FullScreenMode,
                Is.EqualTo(FullScreenMode.ExclusiveFullScreen));
            Assert.That(plan.QualityLevel, Is.EqualTo(0));
            Assert.That(plan.TargetFrameRate, Is.EqualTo(30));
        }

        [Test]
        public void OnlineLook_AppliesSensitivityAndOptionalYInversion()
        {
            var settings = GameSettingsData.Default;
            settings.MouseSensitivity = 1.5f;
            var normal = NetworkPlayerAvatar.ResolveLookDelta(
                new Vector2(2f, 3f), 0.1f, settings);
            settings.InvertY = true;
            var inverted = NetworkPlayerAvatar.ResolveLookDelta(
                new Vector2(2f, 3f), 0.1f, settings);

            Assert.That(normal.x, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(normal.y, Is.EqualTo(-0.45f).Within(0.0001f));
            Assert.That(inverted.x, Is.EqualTo(0.3f).Within(0.0001f));
            Assert.That(inverted.y, Is.EqualTo(0.45f).Within(0.0001f));
        }

        [Test]
        public void LandingEffectMessage_IsFormattedInTheReceiversLanguage()
        {
            var encoded = LandingEffectMessage.Encode(0, true, 3, 4, "ITEM REWARD +1 {0}", "Mine");
            Assert.That(encoded.Length, Is.LessThan(125), "Must fit FixedString128Bytes.");
            Assert.That(LandingEffectMessage.Format(encoded),
                Is.EqualTo("P1 (3,4): ITEM REWARD +1 Mine"));
            Assert.That(LandingEffectMessage.Format(
                    LandingEffectMessage.Encode(3, false, 0, 0, "NO EFFECT (0 change)")),
                Is.EqualTo("P4: NO EFFECT (0 change)"));

            var roulette = LandingEffectMessage.Encode(
                3,
                true,
                -12,
                34,
                "SPECIAL EVENT  {0}  {1}  ACTION > {2}",
                "EVERYONE ELSE",
                "30 GOLD",
                "RECEIVE");
            Assert.That(
                Encoding.UTF8.GetByteCount(roulette),
                Is.LessThan(125),
                "The longest roulette line must fit FixedString128Bytes.");
            var transferResult = LandingEffectMessage.Encode(
                3,
                true,
                -12,
                34,
                "EVENT RESULT: P{0} STEALS {2} GOLD FROM P{1}",
                "4",
                "1",
                "30");
            Assert.That(
                Encoding.UTF8.GetByteCount(transferResult),
                Is.LessThan(125),
                "The longest event result must fit FixedString128Bytes.");

            GameText.UseTableForTests(StringTable.Parse(
                "source,ko,ja,zh-Hans\n" +
                "ITEM REWARD +1 {0},아이템 보상 +1 {0},,\n" +
                "Mine,지뢰,,\n"));
            GameText.SetLanguage(GameLanguage.Korean);
            Assert.That(LandingEffectMessage.Format(encoded),
                Is.EqualTo("P1 (3,4): 아이템 보상 +1 지뢰"));
            Assert.That(LandingEffectMessage.Format(string.Empty), Is.Empty);
        }

        private sealed class MemorySettingsStore : IGameSettingsStore
        {
            private readonly Dictionary<string, float> _floats =
                new Dictionary<string, float>();
            private readonly Dictionary<string, int> _ints =
                new Dictionary<string, int>();

            public int SaveCount { get; private set; }
            public float GetFloat(string key, float defaultValue) =>
                _floats.TryGetValue(key, out var value) ? value : defaultValue;
            public int GetInt(string key, int defaultValue) =>
                _ints.TryGetValue(key, out var value) ? value : defaultValue;
            public void SetFloat(string key, float value) => _floats[key] = value;
            public void SetInt(string key, int value) => _ints[key] = value;
            public void Save() => SaveCount++;
        }
    }
}
