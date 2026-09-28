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
        public void Wardrobe_UsesBoundFaceAndHatSelectorsWithinAuthoredFooter()
        {
            var root = PrefabUtility.LoadPrefabContents(LobbyPrefabPath);
            try
            {
                var view = GetLobbyView(root);
                var face = root.GetComponentsInChildren<LobbyExpressionView>(true)
                    .Single();
                var hat = root.GetComponentsInChildren<LobbyHatView>(true)
                    .Single();
                Assert.That(face.HasRequiredReferences, Is.True);
                Assert.That(hat.HasRequiredReferences, Is.True);

                var footer = root.GetComponentsInChildren<RectTransform>(true)
                    .Single(item => item.name == "Customization Footer");
                var layout = footer.GetComponent<HorizontalLayoutGroup>();
                var selectors = new[]
                {
                    face.GetComponent<LayoutElement>(),
                    hat.GetComponent<LayoutElement>()
                };
                Assert.That(selectors, Has.None.Null);
                Assert.That(
                    selectors.Sum(item => item.preferredWidth) + layout.spacing,
                    Is.LessThanOrEqualTo(footer.rect.width));
                Assert.That(footer.GetComponentsInChildren<Toggle>(true), Is.Empty,
                    "The legacy test-hat toggle must not return.");

                PlayerAppearanceState? published = null;
                view.AppearanceChanged += appearance => published = appearance;
                view.SetAppearance(PlayerAppearanceState.FromColor(
                    LobbyColorPalette.GetColor(2), 0, 0, 3, 0, 2));
                view.SelectHat(2);
                Assert.That(view.SelectedHat, Is.EqualTo(2));
                Assert.That(view.SelectedExpression, Is.EqualTo(2));
                Assert.That(published.HasValue, Is.True);
                Assert.That(published.Value.HatId, Is.EqualTo(2));
                Assert.That(published.Value.ExpressionId, Is.EqualTo(2));
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

        private static SessionSnapshot CreateSnapshot(string phase)
        {
            return new SessionSnapshot(
                "ABCD",
                true,
                phase,
                "host",
                new[]
                {
                    new OnlinePlayerSnapshot("host", "Host", 0, true, true)
                });
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
