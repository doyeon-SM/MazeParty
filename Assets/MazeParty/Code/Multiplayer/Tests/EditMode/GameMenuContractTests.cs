using System.Linq;
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
        private const string LobbyPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";
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
        }

        [Test]
        public void MenuPrefab_OrdersControlsWithApplyThenExitAtTheBottom()
        {
            var bindings = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath)
                .GetComponent<GameMenuBindings>();
            var panel = bindings.ApplyButton.transform.parent;
            Assert.That(bindings.ExitButton.transform.parent, Is.SameAs(panel));
            Assert.That(bindings.ExitButton.transform.GetSiblingIndex(),
                Is.EqualTo(panel.childCount - 1), "The exit button is the last control.");
            Assert.That(bindings.ApplyButton.transform.GetSiblingIndex(),
                Is.EqualTo(panel.childCount - 2), "Apply sits directly above the exit button.");
            Assert.That(bindings.PauseButton.transform.GetSiblingIndex(),
                Is.LessThan(bindings.ApplyButton.transform.GetSiblingIndex()));
        }

        [Test]
        public void MenuPrefab_StaticLabelsAreLocalizedAndRuntimeLabelsAreNot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuPrefabPath);
            var bindings = prefab.GetComponent<GameMenuBindings>();
            var runtimeTexts = new[]
            {
                bindings.MasterValueText,
                bindings.SfxValueText,
                bindings.BgmValueText,
                bindings.DisplayValueText,
                bindings.PauseButtonText,
                bindings.ExitButtonText,
                bindings.NoticeMessageText,
                bindings.PauseBannerText,
                bindings.PauseTimerText
            };
            foreach (var text in runtimeTexts)
            {
                Assert.That(text.GetComponent<LocalizedText>(), Is.Null, text.name);
            }

            var sources = prefab.GetComponentsInChildren<LocalizedText>(true)
                .Select(label => label.SourceText)
                .ToArray();
            Assert.That(sources, Does.Contain("SETTINGS"));
            Assert.That(sources, Does.Contain("Master"));
            Assert.That(sources, Does.Contain("SFX"));
            Assert.That(sources, Does.Contain("BGM"));
            Assert.That(sources, Does.Contain("Apply"));
            Assert.That(sources, Does.Contain("Are you sure you want to leave?"));
            Assert.That(sources, Does.Contain("Release Pause"));
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
        public void LobbyPrefab_NoLongerOwnsLeaveOrQuitButtons()
        {
            var lobby = AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPrefabPath);
            Assert.That(lobby, Is.Not.Null);
            var names = lobby.GetComponentsInChildren<Transform>(true).Select(t => t.name).ToArray();
            Assert.That(names, Does.Not.Contain("Leave Session Button"));
            Assert.That(names, Does.Not.Contain("Quit Game Button"));
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

        [TestCase(300d, "5:00")]
        [TestCase(299.2d, "5:00")]
        [TestCase(61d, "1:01")]
        [TestCase(0.1d, "0:01")]
        [TestCase(0d, "0:00")]
        [TestCase(-3d, "0:00")]
        public void PauseClock_RoundsUpToWholeSeconds(double seconds, string expected)
        {
            Assert.That(GameMenuRules.FormatPauseClock(seconds), Is.EqualTo(expected));
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
        public void WaitingRoomExit_LeavesTheRoomWithoutConfirmation()
        {
            // A player back in the waiting room during someone else's award
            // ceremony uses this context.
            Assert.That(GameMenuRules.GetExitAction(GameMenuContext.WaitingRoom),
                Is.EqualTo(GameMenuExitAction.LeaveWaitingRoom));
            Assert.That(GameMenuRules.ShowsPauseButton(GameMenuContext.WaitingRoom), Is.False);
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
        public void LandingEffectMessage_IsFormattedInTheReceiversLanguage()
        {
            var encoded = LandingEffectMessage.Encode(0, true, 3, 4, "ITEM REWARD +1 {0}", "Mine");
            Assert.That(encoded.Length, Is.LessThan(125), "Must fit FixedString128Bytes.");
            Assert.That(LandingEffectMessage.Format(encoded),
                Is.EqualTo("P1 (3,4): ITEM REWARD +1 Mine"));
            Assert.That(LandingEffectMessage.Format(
                    LandingEffectMessage.Encode(3, false, 0, 0, "NO EFFECT (0 change)")),
                Is.EqualTo("P4: NO EFFECT (0 change)"));

            GameText.UseTableForTests(StringTable.Parse(
                "source,ko,ja,zh-Hans\n" +
                "ITEM REWARD +1 {0},아이템 보상 +1 {0},,\n" +
                "Mine,지뢰,,\n"));
            GameText.SetLanguage(GameLanguage.Korean);
            Assert.That(LandingEffectMessage.Format(encoded),
                Is.EqualTo("P1 (3,4): 아이템 보상 +1 지뢰"));
            Assert.That(LandingEffectMessage.Format(string.Empty), Is.Empty);
        }
    }
}
