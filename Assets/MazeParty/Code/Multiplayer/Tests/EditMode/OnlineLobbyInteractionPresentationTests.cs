using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class OnlineLobbyInteractionPresentationTests
    {
        private const string LobbyPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";

        [Test]
        public void JoinPopup_UsesMaskedInputAndOpensOnlyWhenRequested()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var view = GetLobbyView(root);
                var joinPopup = GetField<GameObject>(view, "joinCodePopup");
                var joinCodeInput = GetField<InputField>(view, "joinCodeInput");
                var openButton = GetField<Button>(view, "openJoinPopupButton");
                var cancelButton = GetField<Button>(view, "cancelJoinButton");

                Assert.That(joinPopup.activeSelf, Is.False,
                    "The authored popup must start closed.");
                Assert.That(joinCodeInput.contentType,
                    Is.EqualTo(InputField.ContentType.Custom));
                Assert.That(joinCodeInput.inputType,
                    Is.EqualTo(InputField.InputType.Password));
                Assert.That(joinCodeInput.characterValidation,
                    Is.EqualTo(InputField.CharacterValidation.Alphanumeric));
                Assert.That(joinCodeInput.asteriskChar, Is.EqualTo('*'));

                BindButtonEvents(view);
                view.Render(SessionSnapshot.Empty, false, false, string.Empty);
                openButton.onClick.Invoke();
                Assert.That(joinPopup.activeSelf, Is.True);

                joinCodeInput.SetTextWithoutNotify("secret");
                cancelButton.onClick.Invoke();
                Assert.That(joinPopup.activeSelf, Is.False);
                Assert.That(joinCodeInput.text, Is.Empty,
                    "Closing the popup must not retain a room code.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void InviteCode_RemainsMaskedForCopyAndRevealsOnlyWhileHeld()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var view = GetLobbyView(root);
                var inviteCodeText = GetField<Text>(view, "inviteCodeText");
                var revealButton = GetField<HoldToRevealButton>(
                    view,
                    "inviteCodeRevealButton");
                var copyButton = GetField<Button>(view, "copyButton");
                var maskedText = GameText.F("Invite Code: {0}", "****");
                var revealedText = GameText.F("Invite Code: {0}", "ABCD");
                var copyRequests = 0;

                BindButtonEvents(view);
                view.CopyRequested += () => copyRequests++;
                view.Render(CreateSnapshot(MultiplayerConstants.LobbyPhase),
                    true,
                    false,
                    string.Empty);

                Assert.That(inviteCodeText.text, Is.EqualTo(maskedText));
                copyButton.onClick.Invoke();
                Assert.That(copyRequests, Is.EqualTo(1));
                Assert.That(inviteCodeText.text, Is.EqualTo(maskedText),
                    "Copying must not expose the invite code in the lobby.");

                var pointerEvent = new PointerEventData(EventSystem.current)
                {
                    button = PointerEventData.InputButton.Left
                };
                revealButton.OnPointerDown(pointerEvent);
                Assert.That(inviteCodeText.text, Is.EqualTo(revealedText));

                revealButton.OnPointerUp(pointerEvent);
                Assert.That(inviteCodeText.text, Is.EqualTo(maskedText));
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void Wardrobe_DefaultsClosedAndToggleStateResetsOutsideLobby()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var view = GetLobbyView(root);
                var wardrobePanel = GetField<GameObject>(
                    view,
                    "customizationPanel");
                var wardrobeButton = GetField<Button>(
                    view,
                    "customizationButton");

                BindButtonEvents(view);
                view.Render(CreateSnapshot(MultiplayerConstants.LobbyPhase),
                    true,
                    false,
                    string.Empty);
                Assert.That(wardrobeButton.gameObject.activeSelf, Is.True);
                Assert.That(wardrobePanel.activeSelf, Is.False);

                wardrobeButton.onClick.Invoke();
                Assert.That(wardrobePanel.activeSelf, Is.True);
                wardrobeButton.onClick.Invoke();
                Assert.That(wardrobePanel.activeSelf, Is.False);

                wardrobeButton.onClick.Invoke();
                view.Render(CreateSnapshot(MultiplayerConstants.PlayingPhase),
                    true,
                    false,
                    string.Empty);
                Assert.That(wardrobeButton.gameObject.activeSelf, Is.False);
                Assert.That(wardrobePanel.activeSelf, Is.False);

                view.Render(CreateSnapshot(MultiplayerConstants.LobbyPhase),
                    true,
                    false,
                    string.Empty);
                Assert.That(wardrobeButton.gameObject.activeSelf, Is.True);
                Assert.That(wardrobePanel.activeSelf, Is.False,
                    "Returning to the lobby must not reopen the wardrobe.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void BoardMapSelector_ShowsNameToEveryoneAndOnlyHostCanCycle()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var view = GetLobbyView(root);
                var selector = GetField<GameObject>(
                    view,
                    "boardMapSelectionRoot");
                var previous = GetField<Button>(
                    view,
                    "previousBoardMapButton");
                var next = GetField<Button>(
                    view,
                    "nextBoardMapButton");
                var mapName = GetField<Text>(view, "boardMapNameText");
                var ready = GetField<Button>(view, "readyButton");
                var sessionPanel = GetField<GameObject>(view, "sessionPanel");

                Assert.That(selector.transform.parent,
                    Is.SameAs(sessionPanel.transform));
                Assert.That(selector.transform.GetSiblingIndex(),
                    Is.LessThan(ready.transform.GetSiblingIndex()));
                Assert.That(
                    ((RectTransform)selector.transform).sizeDelta.y,
                    Is.EqualTo(48f).Within(0.01f),
                    "Session Panel does not control child height, so the authored row height is the runtime height.");
                Assert.That(previous.transform.IsChildOf(selector.transform), Is.True);
                Assert.That(next.transform.IsChildOf(selector.transform), Is.True);
                Assert.That(mapName.transform.IsChildOf(selector.transform), Is.True);

                BindButtonEvents(view);
                var deltas = new List<int>();
                view.MapSelectionDeltaRequested += deltas.Add;

                view.Render(CreateSnapshot(
                        MultiplayerConstants.LobbyPhase,
                        true),
                    true,
                    false,
                    string.Empty);
                Assert.That(selector.activeSelf, Is.True);
                Assert.That(mapName.text,
                    Is.EqualTo(GameText.F(
                        "Map: {0}",
                        GameText.T("Forest Graybox"))));
                Assert.That(previous.gameObject.activeSelf, Is.True);
                Assert.That(next.gameObject.activeSelf, Is.True);
                Assert.That(previous.interactable, Is.False,
                    "The shipped catalog currently contains one selectable map.");
                Assert.That(next.interactable, Is.False);
                previous.onClick.Invoke();
                next.onClick.Invoke();
                Assert.That(deltas, Is.Empty,
                    "Disabled single-map controls must not publish requests.");

                previous.interactable = true;
                next.interactable = true;
                previous.onClick.Invoke();
                next.onClick.Invoke();
                Assert.That(deltas, Is.EqualTo(new[] { -1, 1 }));

                view.Render(CreateSnapshot(
                        MultiplayerConstants.LobbyPhase,
                        false),
                    true,
                    false,
                    string.Empty);
                Assert.That(selector.activeSelf, Is.True);
                Assert.That(mapName.text,
                    Is.EqualTo(GameText.F(
                        "Map: {0}",
                        GameText.T("Forest Graybox"))));
                Assert.That(previous.gameObject.activeSelf, Is.False);
                Assert.That(next.gameObject.activeSelf, Is.False);

                view.Render(CreateSnapshot(
                        MultiplayerConstants.LobbyPhase,
                        false,
                        new BoardMapSelection("forest-graybox", 999)),
                    true,
                    false,
                    string.Empty);
                Assert.That(mapName.text,
                    Is.EqualTo(GameText.F(
                        "Map: {0}",
                        GameText.T("Unavailable Map"))));

                view.Render(CreateSnapshot(
                        MultiplayerConstants.LobbyPhase,
                        true),
                    true,
                    true,
                    string.Empty);
                Assert.That(previous.gameObject.activeSelf, Is.True);
                Assert.That(next.gameObject.activeSelf, Is.True);
                Assert.That(previous.interactable, Is.False);
                Assert.That(next.interactable, Is.False);

                view.SetBoardMapSelectionLocked(true);
                view.Render(CreateSnapshot(
                        MultiplayerConstants.LobbyPhase,
                        true),
                    true,
                    false,
                    string.Empty);
                Assert.That(previous.interactable, Is.False);
                Assert.That(next.interactable, Is.False);
                view.SetBoardMapSelectionLocked(false);

                view.Render(CreateSnapshot(
                        MultiplayerConstants.PlayingPhase,
                        true),
                    true,
                    false,
                    string.Empty);
                Assert.That(selector.activeSelf, Is.False);

                view.Render(SessionSnapshot.Empty, false, false, string.Empty);
                Assert.That(selector.activeSelf, Is.False);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static OnlineLobbyView GetLobbyView(GameObject root)
        {
            var view = root.GetComponentInChildren<OnlineLobbyView>(true);
            Assert.That(view, Is.Not.Null, LobbyPrefabPath);
            Assert.That(view.HasRequiredReferences, Is.True, LobbyPrefabPath);
            return view;
        }

        private static SessionSnapshot CreateSnapshot(
            string phase,
            bool isHost = true)
        {
            return CreateSnapshot(
                phase,
                isHost,
                new BoardMapSelection("forest-graybox", 4));
        }

        private static SessionSnapshot CreateSnapshot(
            string phase,
            bool isHost,
            BoardMapSelection boardMapSelection)
        {
            return new SessionSnapshot(
                "ABCD",
                isHost,
                phase,
                isHost ? "host" : "guest",
                new[]
                {
                    new OnlinePlayerSnapshot("host", "Host", 0, true, true),
                    new OnlinePlayerSnapshot("guest", "Guest", 1, true, false)
                },
                boardMapSelection);
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

        private static void BindButtonEvents(OnlineLobbyView view)
        {
            var method = typeof(OnlineLobbyView).GetMethod(
                "BindButtonEvents",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(view, null);
        }
    }
}
