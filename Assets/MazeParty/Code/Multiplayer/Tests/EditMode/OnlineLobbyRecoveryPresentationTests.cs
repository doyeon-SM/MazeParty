using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class OnlineLobbyRecoveryPresentationTests
    {
        private const string LobbyPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";

        [Test]
        public void AuthoredButtons_RouteHostRecoveryChoiceAndRestoreLobbyActions()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var view = root.GetComponentInChildren<OnlineLobbyView>(true);
                Assert.That(view, Is.Not.Null, LobbyPrefabPath);
                Assert.That(view.HasRequiredReferences, Is.True, LobbyPrefabPath);

                var readyButton = GetField<Button>(view, "readyButton");
                var startButton = GetField<Button>(view, "startButton");
                var readyButtonText = GetField<Text>(view, "readyButtonText");
                var startButtonText = GetField<Text>(view, "startButtonText");
                var startHint = GetField<GameObject>(view, "startHint");
                var runningMessage = GetField<GameObject>(view, "runningMessage");
                var boardMapSelectionRoot = GetField<GameObject>(
                    view,
                    "boardMapSelectionRoot");
                var previousBoardMapButton = GetField<Button>(
                    view,
                    "previousBoardMapButton");
                var nextBoardMapButton = GetField<Button>(
                    view,
                    "nextBoardMapButton");
                var customizationPanel = GetField<GameObject>(view, "customizationPanel");
                Assert.That(readyButton.transform.IsChildOf(root.transform), Is.True);
                Assert.That(startButton.transform.IsChildOf(root.transform), Is.True);

                InvokePrivate(view, "BindButtonEvents");
                var readyRequests = 0;
                var startRequests = 0;
                var continueRequests = 0;
                var discardRequests = 0;
                view.ReadyRequested += () => readyRequests++;
                view.StartRequested += () => startRequests++;
                view.RecoveryContinueRequested += () => continueRequests++;
                view.RecoveryDiscardRequested += () => discardRequests++;

                var hostSnapshot = CreateSnapshot(true, "host");
                var normalStartLabel = startButtonText.text;
                view.SetRecoveryChoice(true);
                view.Render(hostSnapshot, true, false, string.Empty);

                Assert.That(readyButton.gameObject.activeSelf, Is.True);
                Assert.That(startButton.gameObject.activeSelf, Is.True);
                Assert.That(readyButtonText.text,
                    Is.EqualTo(GameText.T("Discard Saved Match")));
                Assert.That(startButtonText.text,
                    Is.EqualTo(GameText.T("Continue Saved Match")));
                Assert.That(startHint.activeSelf, Is.False);
                Assert.That(runningMessage.activeSelf, Is.False);
                Assert.That(boardMapSelectionRoot.activeSelf, Is.False);
                Assert.That(customizationPanel.activeSelf, Is.False);

                readyButton.onClick.Invoke();
                startButton.onClick.Invoke();
                Assert.That(discardRequests, Is.EqualTo(1));
                Assert.That(continueRequests, Is.EqualTo(1));
                Assert.That(readyRequests, Is.Zero);
                Assert.That(startRequests, Is.Zero);

                var availabilityCases = new[]
                {
                    new { Busy = false, CanChoose = true, Expected = true },
                    new { Busy = false, CanChoose = false, Expected = false },
                    new { Busy = true, CanChoose = true, Expected = false }
                };
                foreach (var testCase in availabilityCases)
                {
                    view.SetRecoveryChoice(true, testCase.CanChoose);
                    view.Render(hostSnapshot, true, testCase.Busy, string.Empty);
                    Assert.That(readyButton.interactable,
                        Is.EqualTo(testCase.Expected));
                    Assert.That(startButton.interactable,
                        Is.EqualTo(testCase.Expected));
                }

                view.SetRecoveryChoice(false);
                view.Render(hostSnapshot, true, false, string.Empty);
                Assert.That(readyButtonText.text,
                    Is.EqualTo(GameText.T("Cancel Ready")));
                Assert.That(startButtonText.text, Is.EqualTo(normalStartLabel));
                Assert.That(readyButton.interactable, Is.True);
                Assert.That(startButton.interactable, Is.True);
                Assert.That(boardMapSelectionRoot.activeSelf, Is.True);
                Assert.That(customizationPanel.activeSelf, Is.False);

                previousBoardMapButton.interactable = true;
                nextBoardMapButton.interactable = true;
                view.SetBoardMapSelectionLocked(true);
                Assert.That(previousBoardMapButton.interactable, Is.False);
                Assert.That(nextBoardMapButton.interactable, Is.False);
                view.Render(hostSnapshot, true, false, string.Empty);
                Assert.That(previousBoardMapButton.gameObject.activeSelf, Is.True);
                Assert.That(nextBoardMapButton.gameObject.activeSelf, Is.True);
                Assert.That(previousBoardMapButton.interactable, Is.False);
                Assert.That(nextBoardMapButton.interactable, Is.False);
                view.SetBoardMapSelectionLocked(false);

                readyButton.onClick.Invoke();
                startButton.onClick.Invoke();
                Assert.That(readyRequests, Is.EqualTo(1));
                Assert.That(startRequests, Is.EqualTo(1));
                Assert.That(discardRequests, Is.EqualTo(1));
                Assert.That(continueRequests, Is.EqualTo(1));

                view.SetRecoveryChoice(true);
                view.Render(CreateSnapshot(false, "guest"), true, false,
                    string.Empty);
                Assert.That(readyButton.gameObject.activeSelf, Is.True);
                Assert.That(startButton.gameObject.activeSelf, Is.False);
                Assert.That(boardMapSelectionRoot.activeSelf, Is.False);
                Assert.That(customizationPanel.activeSelf, Is.False);

                readyButton.onClick.Invoke();
                Assert.That(readyRequests, Is.EqualTo(2));
                Assert.That(discardRequests, Is.EqualTo(1));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static SessionSnapshot CreateSnapshot(bool isHost, string localPlayerId)
        {
            return new SessionSnapshot(
                "ABCD",
                isHost,
                MultiplayerConstants.LobbyPhase,
                localPlayerId,
                new[]
                {
                    new OnlinePlayerSnapshot("host", "Host", 0, true, true),
                    new OnlinePlayerSnapshot("guest", "Guest", 1, true, false),
                    new OnlinePlayerSnapshot("p2", "Player 3", 2, true, false),
                    new OnlinePlayerSnapshot("p3", "Player 4", 3, true, false)
                },
                new BoardMapSelection("forest-graybox", 3));
        }

        private static T GetField<T>(OnlineLobbyView view, string name)
            where T : Object
        {
            var field = typeof(OnlineLobbyView).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(view);
        }

        private static void InvokePrivate(OnlineLobbyView view, string name)
        {
            var method = typeof(OnlineLobbyView).GetMethod(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, name);
            method.Invoke(view, null);
        }
    }
}
