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

        [SetUp]
        public void SetUpPopupStack()
        {
            UiPopupStack.ClearForTests();
        }

        [TearDown]
        public void TearDownPopupStack()
        {
            UiPopupStack.ClearForTests();
        }

        [Test]
        public void PreLobbyGameplayBackground_IsAuthoredAndFollowsSessionVisibility()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var view = GetLobbyView(root);
                var connectionPanel = GetField<GameObject>(
                    view,
                    "connectionPanel");
                var sessionHeader = GetField<GameObject>(
                    view,
                    "sessionHeaderRoot");
                var background = connectionPanel.transform.Find(
                    "Pre-Lobby Gameplay Background");

                Assert.That(background, Is.Not.Null,
                    "The pre-session gameplay art must be authored directly under Connection Panel.");
                Assert.That(background.parent, Is.SameAs(connectionPanel.transform));

                var image = background.GetComponent<Image>();
                Assert.That(image, Is.Not.Null,
                    "The pre-session background must use an authored uGUI Image.");
                Assert.That(image.sprite, Is.Not.Null,
                    "The pre-session background must reference an imported sprite.");
                Assert.That(
                    AssetDatabase.GetAssetPath(image.sprite),
                    Does.StartWith("Assets/Ignore/AIImage/"),
                    "Temporary generated lobby art must stay under Assets/Ignore/AIImage.");

                view.Render(
                    SessionSnapshot.Empty,
                    false,
                    false,
                    string.Empty);
                Assert.That(connectionPanel.activeSelf, Is.True);
                Assert.That(sessionHeader.activeSelf, Is.False);
                Assert.That(background.gameObject.activeInHierarchy, Is.True,
                    "The gameplay background must be visible before joining a session.");

                view.Render(
                    CreateSnapshot(MultiplayerConstants.LobbyPhase),
                    true,
                    false,
                    string.Empty);
                Assert.That(connectionPanel.activeSelf, Is.False);
                Assert.That(sessionHeader.activeSelf, Is.True);
                Assert.That(background.gameObject.activeInHierarchy, Is.False,
                    "The gameplay background must be hidden once the session UI is active.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        [Test]
        public void LocalizedLogo_IsVisibleOnlyBeforeJoiningSession()
        {
            GameText.SetLanguage(GameLanguage.English);
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            LocalizedLogo localizedLogo = null;
            try
            {
                var view = GetLobbyView(root);
                localizedLogo = root.GetComponentInChildren<LocalizedLogo>(true);
                Assert.That(localizedLogo, Is.Not.Null, LobbyPrefabPath);

                var serializedLogo = new SerializedObject(localizedLogo);
                var englishLogo = serializedLogo.FindProperty("englishLogo")
                    .objectReferenceValue as GameObject;
                var koreanLogo = serializedLogo.FindProperty("koreanLogo")
                    .objectReferenceValue as GameObject;
                Assert.That(englishLogo, Is.Not.Null);
                Assert.That(koreanLogo, Is.Not.Null);

                InvokeLogoLifecycle(localizedLogo, "OnDisable");
                InvokeLogoLifecycle(localizedLogo, "OnEnable");

                view.Render(SessionSnapshot.Empty, false, false, string.Empty);
                Assert.That(englishLogo.activeSelf, Is.True);
                Assert.That(koreanLogo.activeSelf, Is.False);

                view.Render(
                    CreateSnapshot(MultiplayerConstants.LobbyPhase),
                    true,
                    false,
                    string.Empty);
                Assert.That(englishLogo.activeSelf, Is.False);
                Assert.That(koreanLogo.activeSelf, Is.False);

                GameText.SetLanguage(GameLanguage.Korean);
                Assert.That(englishLogo.activeSelf, Is.False);
                Assert.That(koreanLogo.activeSelf, Is.False,
                    "Changing language in a session must not reveal the logo.");

                view.Render(SessionSnapshot.Empty, false, false, string.Empty);
                Assert.That(englishLogo.activeSelf, Is.False);
                Assert.That(koreanLogo.activeSelf, Is.True,
                    "Leaving the session must restore the current-language logo.");
            }
            finally
            {
                if (localizedLogo != null)
                {
                    InvokeLogoLifecycle(localizedLogo, "OnDisable");
                }

                PrefabUtility.UnloadPrefabContents(root);
                GameText.ResetForTests();
            }
        }

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
                Assert.That(UiPopupStack.IsTop(joinPopup), Is.True);

                joinCodeInput.SetTextWithoutNotify("secret");
                cancelButton.onClick.Invoke();
                Assert.That(joinPopup.activeSelf, Is.False);
                Assert.That(UiPopupStack.Count, Is.Zero);
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
                var sessionHeader = GetField<GameObject>(
                    view,
                    "sessionHeaderRoot");
                var inviteCodeText = GetField<Text>(view, "inviteCodeText");
                var revealButton = GetField<HoldToRevealButton>(
                    view,
                    "inviteCodeRevealButton");
                var copyButton = GetField<Button>(view, "copyButton");
                var maskedText = GameText.F("Invite Code: {0}", "****");
                var revealedText = GameText.F("Invite Code: {0}", "ABCD");
                var copyRequests = 0;

                Assert.That(sessionHeader.transform.parent, Is.SameAs(root.transform),
                    "The invite code controls must be authored as a root-level lobby header.");
                Assert.That(inviteCodeText.transform.parent,
                    Is.SameAs(sessionHeader.transform));
                Assert.That(revealButton.transform.parent,
                    Is.SameAs(sessionHeader.transform));
                Assert.That(copyButton.transform.parent,
                    Is.SameAs(sessionHeader.transform));
                AssertIconOnlyButton(revealButton.GetComponent<Button>(),
                    "Invite-code reveal");
                AssertIconOnlyButton(copyButton, "Invite-code copy");

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
        public void ReadyGuidance_ReplacesLegacyRosterAndTracksFourReadyPlayers()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var view = GetLobbyView(root);
                var readyGuidance = GetField<GameObject>(view, "startHint");

                var authoredNames = root
                    .GetComponentsInChildren<Transform>(true)
                    .Select(transform => transform.name)
                    .ToArray();
                Assert.That(authoredNames, Does.Not.Contain("Session Panel"));
                Assert.That(
                    authoredNames.Any(name =>
                        name.StartsWith("Player Row") ||
                        name.StartsWith("Player Slot") ||
                        name == "Host Star Icon"),
                    Is.False,
                    "Readiness and host state belong on world nameplates, not a lobby roster panel.");

                view.Render(CreateSnapshotWithMixedReadiness(),
                    true,
                    false,
                    string.Empty);
                Assert.That(readyGuidance.activeSelf, Is.True);

                view.Render(CreateSnapshot(
                        MultiplayerConstants.LobbyPhase,
                        false),
                    true,
                    false,
                    string.Empty);
                Assert.That(readyGuidance.activeSelf, Is.True,
                    "Every player should see the readiness guidance while the room cannot start.");

                view.Render(CreateFourReadySnapshot(),
                    true,
                    false,
                    string.Empty);
                Assert.That(readyGuidance.activeSelf, Is.False,
                    "The guidance must clear once the host can start with four ready players.");
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
                var closeWardrobeButton = GetField<Button>(
                    view,
                    "closeCustomizationButton");

                Assert.That(closeWardrobeButton.transform.IsChildOf(
                    wardrobePanel.transform), Is.True);

                BindButtonEvents(view);
                view.Render(CreateSnapshot(MultiplayerConstants.LobbyPhase),
                    true,
                    false,
                    string.Empty);
                Assert.That(wardrobeButton.gameObject.activeSelf, Is.True);
                Assert.That(wardrobePanel.activeSelf, Is.False);

                wardrobeButton.onClick.Invoke();
                Assert.That(wardrobePanel.activeSelf, Is.True);
                Assert.That(UiPopupStack.IsTop(wardrobePanel), Is.True);
                closeWardrobeButton.onClick.Invoke();
                Assert.That(wardrobePanel.activeSelf, Is.False);
                Assert.That(UiPopupStack.Count, Is.Zero);

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
        public void BoardSettingsPopup_TogglesSeparatelyAndOnlyHostCanCycleMaps()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var view = GetLobbyView(root);
                var boardSettingsPanel = GetField<GameObject>(
                    view,
                    "boardSettingsPanel");
                var boardSettingsButton = GetField<Button>(
                    view,
                    "boardSettingsButton");
                var closeBoardSettingsButton = GetField<Button>(
                    view,
                    "closeBoardSettingsButton");
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
                var start = GetField<Button>(view, "startButton");
                var sessionActions = GetField<GameObject>(
                    view,
                    "sessionActionsRoot");
                var wardrobeButton = GetField<Button>(
                    view,
                    "customizationButton");
                var wardrobePanel = GetField<GameObject>(
                    view,
                    "customizationPanel");

                Assert.That(boardSettingsPanel.transform.parent,
                    Is.SameAs(root.transform),
                    "Board settings must be a separate root-level popup.");
                Assert.That(selector.transform.IsChildOf(
                    boardSettingsPanel.transform), Is.True);
                Assert.That(closeBoardSettingsButton.transform.IsChildOf(
                    boardSettingsPanel.transform), Is.True);
                Assert.That(previous.transform.IsChildOf(selector.transform), Is.True);
                Assert.That(next.transform.IsChildOf(selector.transform), Is.True);
                Assert.That(mapName.transform.IsChildOf(selector.transform), Is.True);
                Assert.That(boardSettingsButton.transform.parent,
                    Is.SameAs(wardrobeButton.transform.parent));

                Assert.That(sessionActions.transform.parent,
                    Is.SameAs(root.transform));
                Assert.That(ready.transform.parent,
                    Is.SameAs(sessionActions.transform));
                Assert.That(start.transform.parent,
                    Is.SameAs(sessionActions.transform));

                BindButtonEvents(view);
                var deltas = new List<int>();
                view.MapSelectionDeltaRequested += deltas.Add;

                view.Render(CreateSnapshot(
                        MultiplayerConstants.LobbyPhase,
                        true),
                    true,
                    false,
                    string.Empty);
                Assert.That(boardSettingsButton.gameObject.activeSelf, Is.True);
                Assert.That(boardSettingsPanel.activeSelf, Is.False,
                    "Board settings must start closed each time the lobby is entered.");
                Assert.That(selector.activeInHierarchy, Is.False);

                boardSettingsButton.onClick.Invoke();
                Assert.That(boardSettingsPanel.activeSelf, Is.True);
                Assert.That(UiPopupStack.IsTop(boardSettingsPanel), Is.True);
                Assert.That(selector.activeInHierarchy, Is.True);
                Assert.That(mapName.text,
                    Is.EqualTo(GameText.F(
                        "Map: {0}",
                        GameText.T("Forest"))));
                Assert.That(previous.gameObject.activeSelf, Is.True);
                Assert.That(next.gameObject.activeSelf, Is.True);
                Assert.That(previous.interactable, Is.True,
                    "The shipped catalog contains two selectable maps (forest-graybox, maze-graybox).");
                Assert.That(next.interactable, Is.True);
                previous.interactable = false;
                next.interactable = false;
                previous.onClick.Invoke();
                next.onClick.Invoke();
                Assert.That(deltas, Is.Empty,
                    "Disabled map controls must not publish requests.");

                previous.interactable = true;
                next.interactable = true;
                previous.onClick.Invoke();
                next.onClick.Invoke();
                Assert.That(deltas, Is.EqualTo(new[] { -1, 1 }));

                wardrobeButton.onClick.Invoke();
                Assert.That(boardSettingsPanel.activeSelf, Is.False,
                    "Opening Wardrobe must close Board Settings.");
                Assert.That(wardrobePanel.activeSelf, Is.True);
                boardSettingsButton.onClick.Invoke();
                Assert.That(boardSettingsPanel.activeSelf, Is.True);
                Assert.That(wardrobePanel.activeSelf, Is.False,
                    "Opening Board Settings must close Wardrobe.");
                closeBoardSettingsButton.onClick.Invoke();
                Assert.That(boardSettingsPanel.activeSelf, Is.False);
                Assert.That(UiPopupStack.Count, Is.Zero);

                view.Render(CreateSnapshot(
                        MultiplayerConstants.LobbyPhase,
                        false),
                    true,
                    false,
                    string.Empty);
                Assert.That(boardSettingsButton.gameObject.activeSelf, Is.True);
                Assert.That(boardSettingsPanel.activeSelf, Is.False);
                boardSettingsButton.onClick.Invoke();
                Assert.That(boardSettingsPanel.activeSelf, Is.True);
                Assert.That(mapName.text,
                    Is.EqualTo(GameText.F(
                        "Map: {0}",
                        GameText.T("Forest"))));
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
                Assert.That(boardSettingsPanel.activeSelf, Is.True);
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
                Assert.That(boardSettingsPanel.activeSelf, Is.True);
                Assert.That(previous.interactable, Is.False);
                Assert.That(next.interactable, Is.False);
                view.SetBoardMapSelectionLocked(false);

                view.Render(CreateSnapshot(
                        MultiplayerConstants.PlayingPhase,
                        true),
                    true,
                    false,
                    string.Empty);
                Assert.That(boardSettingsButton.gameObject.activeSelf, Is.False);
                Assert.That(boardSettingsPanel.activeSelf, Is.False);
                Assert.That(selector.activeInHierarchy, Is.False);

                view.Render(CreateSnapshot(
                        MultiplayerConstants.LobbyPhase,
                        true),
                    true,
                    false,
                    string.Empty);
                Assert.That(boardSettingsButton.gameObject.activeSelf, Is.True);
                Assert.That(boardSettingsPanel.activeSelf, Is.False,
                    "Returning to the lobby must not reopen Board Settings.");

                view.Render(SessionSnapshot.Empty, false, false, string.Empty);
                Assert.That(boardSettingsButton.gameObject.activeSelf, Is.False);
                Assert.That(boardSettingsPanel.activeSelf, Is.False);
                Assert.That(selector.activeInHierarchy, Is.False);
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

        private static SessionSnapshot CreateSnapshotWithMixedReadiness()
        {
            return new SessionSnapshot(
                "ABCD",
                true,
                MultiplayerConstants.LobbyPhase,
                "host",
                new[]
                {
                    new OnlinePlayerSnapshot("host", "Host", 0, false, true),
                    new OnlinePlayerSnapshot("guest", "Guest", 1, true, false)
                },
                new BoardMapSelection("forest-graybox", 4));
        }

        private static SessionSnapshot CreateFourReadySnapshot()
        {
            return new SessionSnapshot(
                "ABCD",
                true,
                MultiplayerConstants.LobbyPhase,
                "host",
                new[]
                {
                    new OnlinePlayerSnapshot("host", "Host", 0, true, true),
                    new OnlinePlayerSnapshot("guest-1", "Guest 1", 1, true, false),
                    new OnlinePlayerSnapshot("guest-2", "Guest 2", 2, true, false),
                    new OnlinePlayerSnapshot("guest-3", "Guest 3", 3, true, false)
                },
                new BoardMapSelection("forest-graybox", 4));
        }

        private static T GetField<T>(OnlineLobbyView view, string name)
        {
            var field = typeof(OnlineLobbyView).GetField(
                name,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, name);
            return (T)field.GetValue(view);
        }

        private static void AssertIconOnlyButton(Button button, string context)
        {
            Assert.That(button, Is.Not.Null, context);
            Assert.That(
                button.GetComponentsInChildren<Text>(true)
                    .All(label => !label.gameObject.activeSelf),
                Is.True,
                context + " button must not show a text label.");
            Assert.That(
                button.GetComponentsInChildren<Image>(true)
                    .Any(image =>
                        image.transform != button.transform &&
                        image.gameObject.activeSelf &&
                        image.sprite != null),
                Is.True,
                context + " button must show an authored icon.");
        }

        private static void BindButtonEvents(OnlineLobbyView view)
        {
            var method = typeof(OnlineLobbyView).GetMethod(
                "BindButtonEvents",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(view, null);
        }

        private static void InvokeLogoLifecycle(
            LocalizedLogo localizedLogo,
            string methodName)
        {
            var method = typeof(LocalizedLogo).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, methodName);
            method.Invoke(localizedLogo, null);
        }
    }
}
