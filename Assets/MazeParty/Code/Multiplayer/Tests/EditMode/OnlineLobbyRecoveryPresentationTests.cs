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
        [Test]
        public void AuthoredRecoveryEntry_SeparatesReconnectRestartAndFreshCreateFromWaitingRoomActions()
        {
            var root = PrefabUtility.LoadPrefabContents("Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab");
            try
            {
                var lobby = root.GetComponent<OnlineLobbyView>();
                var view = lobby.RecoveryView;
                Assert.That(lobby.HasRequiredReferences, Is.True);
                typeof(SessionRecoveryView).GetMethod("Awake", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(view, null);
                int reconnects = 0, restarts = 0, creations = 0;
                view.ReconnectRequested += () => reconnects++;
                view.RestartRequested += () => restarts++;
                view.FreshCreateConfirmed += () => creations++;
                view.Initialize(true);
                view.Render(false, false, true, true, "date\nA\nB\nC\nD", "");
                Assert.That(view.ReconnectPopup.activeSelf, Is.True);
                Assert.That(view.RestartButton.gameObject.activeSelf, Is.True);
                view.ReconnectButton.onClick.Invoke(); view.RestartButton.onClick.Invoke();
                Assert.That(reconnects, Is.EqualTo(1)); Assert.That(restarts, Is.EqualTo(1));
                view.ShowCreateConfirmation();
                Assert.That(view.CreateConfirmationPopup.activeSelf, Is.True);
                Get<Button>(view, "cancelCreateButton").onClick.Invoke(); Assert.That(creations, Is.Zero);
                view.ShowCreateConfirmation(); Get<Button>(view, "confirmCreateButton").onClick.Invoke();
                Assert.That(creations, Is.EqualTo(1));
                view.Render(false, false, true, false, "", "");
                Assert.That(view.RestartButton.gameObject.activeSelf, Is.False, "A guest cannot see host restart.");
                view.Render(true, false, true, true, "", "");
                Assert.That(view.ReconnectPopup.activeSelf, Is.False);
                Assert.That(view.CreateConfirmationPopup.activeSelf, Is.False);
                Assert.That(view.RestartButton.gameObject.activeSelf, Is.False);
                var snapshot = new SessionSnapshot("ROOM", true, MultiplayerConstants.LobbyPhase, "host",
                    new[] { new OnlinePlayerSnapshot("host", "Host", 0, false, true) },
                    new BoardMapSelection("forest-graybox", 4), new[] { "Guest A", "Guest B" });
                lobby.Render(snapshot, true, false, "");
                Assert.That(Get<Text>(lobby, "readyButtonText").text, Is.EqualTo(GameText.T("Ready")));
                Assert.That(Get<Text>(lobby, "startButtonText").text, Is.Not.EqualTo(GameText.T("Continue Saved Match")));
                Assert.That(Get<GameObject>(lobby, "startHint").GetComponent<Text>().text, Does.Contain("Guest A"));
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        private static T Get<T>(object instance, string field) => (T)instance.GetType().GetField(field,
            BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }
}
