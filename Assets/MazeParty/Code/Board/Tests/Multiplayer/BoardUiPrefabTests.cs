using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardUiPrefabTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string KeyIconPath =
            "Assets/Ignore/Icon_NCI/free-icon-door-key-63432.png";
        private const string GoldIconPath =
            "Assets/Ignore/Icon_NCI/free-icon-dollar-coin-7022685.png";

        private static readonly string[,] NestedModules =
        {
            {
                "ReconnectOverlay",
                "Assets/MazeParty/Prefabs/Board/UI/Modules/ReconnectOverlay.prefab"
            },
            {
                "MinigameReadyPanel",
                "Assets/MazeParty/Prefabs/Board/UI/Modules/MinigameReadyPanel.prefab"
            },
            {
                "BoardEventPopupPanel",
                "Assets/MazeParty/Prefabs/Board/UI/Modules/BoardEventPopupPanel.prefab"
            }
        };

        [Test]
        public void BoardCanvas_ComposesConnectedAuthoredModules()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);

            for (var index = 0; index < NestedModules.GetLength(0); index++)
            {
                var objectName = NestedModules[index, 0];
                var modulePath = NestedModules[index, 1];
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<GameObject>(modulePath),
                    Is.Not.Null,
                    modulePath);
                var module = prefab.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(candidate => candidate.name == objectName);
                Assert.That(module, Is.Not.Null, objectName);
                var source = PrefabUtility.GetCorrespondingObjectFromSource(
                    module.gameObject);
                Assert.That(
                    source,
                    Is.Not.Null,
                    objectName);
                Assert.That(
                    AssetDatabase.GetAssetPath(source),
                    Is.EqualTo(modulePath),
                    objectName);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        module.gameObject),
                    Is.EqualTo(modulePath),
                    objectName);
            }
        }

        [Test]
        public void ReadyPlayerStates_AreBoundOnBoardCanvasPrefabAndSceneInstance()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var bindings = prefab.GetComponent<BoardCanvasBindings>();
            Assert.That(bindings, Is.Not.Null);
            Assert.That(bindings.HasRequiredReferences, Is.True);
            Assert.That(prefab.GetComponent<BoardUtilityItemView>().HasRequiredReferences, Is.True);
            AssertItemIconBindings(prefab, bindings);
            AssertPlayerCurrencyBindings(prefab, bindings);
            AssertPlayerHealthSliderBindings(prefab, bindings);
            AssertBoardEventPopupBindings(prefab, bindings);

            Assert.That(bindings.MinigameReadyPlayerStates.Length,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var playerState = bindings.MinigameReadyPlayerStates[slot];
                Assert.That(playerState, Is.Not.Null, "READY slot " + slot);
                Assert.That(playerState.transform.IsChildOf(
                    bindings.MinigameReadyPanel.transform), Is.True);
                Assert.That(playerState.name,
                    Is.EqualTo("MinigameReadyPlayerState" + slot));
            }

            var scene = SceneManager.GetSceneByPath(BoardScenePath);
            var openedForTest = !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    BoardScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                BoardCanvasBindings sceneBindings = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    sceneBindings = root.GetComponentInChildren<BoardCanvasBindings>(true);
                    if (sceneBindings != null)
                    {
                        break;
                    }
                }

                Assert.That(sceneBindings, Is.Not.Null);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        sceneBindings.gameObject),
                    Is.EqualTo(PrefabPath));
                Assert.That(sceneBindings.HasRequiredReferences, Is.True);
                AssertItemIconBindings(sceneBindings.gameObject, sceneBindings);
                AssertPlayerCurrencyBindings(
                    sceneBindings.gameObject,
                    sceneBindings);
                AssertPlayerHealthSliderBindings(
                    sceneBindings.gameObject,
                    sceneBindings);
                AssertBoardEventPopupBindings(
                    sceneBindings.gameObject,
                    sceneBindings);

                var utility = sceneBindings.GetComponent<BoardUtilityItemView>();
                Assert.That(utility.HasRequiredReferences, Is.True);
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(utility), Is.Not.Null);
            }
            finally
            {
                if (openedForTest)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void BoardCanvas_CanHideAllBoardUiDuringMinigameGameplay()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);

            var instance = Object.Instantiate(prefab);
            try
            {
                var view = instance.GetComponent<BoardFlowView>();
                var canvas = instance.GetComponent<Canvas>();
                var raycaster = instance.GetComponent<GraphicRaycaster>();
                Assert.That(view, Is.Not.Null);
                Assert.That(canvas, Is.Not.Null);
                Assert.That(raycaster, Is.Not.Null);

                view.SetBoardUiVisible(false);
                Assert.That(view.BoardUiVisible, Is.False);
                Assert.That(canvas.enabled, Is.False);
                Assert.That(raycaster.enabled, Is.False);

                view.SetBoardUiVisible(true);
                Assert.That(view.BoardUiVisible, Is.True);
                Assert.That(canvas.enabled, Is.True);
                Assert.That(raycaster.enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void BoardCanvas_ProvidesDistinctRuleCardsForEveryMinigame()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);

            var bindings = prefab.GetComponent<BoardCanvasBindings>();
            Assert.That(bindings, Is.Not.Null);
            Assert.That(bindings.MinigameRuleImage, Is.Not.Null);
            Assert.That(bindings.MinigameRuleImage.preserveAspect, Is.True);
            Assert.That(bindings.GetMinigameRuleCard(ScheduledMinigameId.Skip),
                Is.Null);

            var uniqueCards = new HashSet<Sprite>();
            foreach (var game in MinigameCatalog.RegisteredMinigames)
            {
                var sprite = bindings.GetMinigameRuleCard(game.Id);
                Assert.That(sprite, Is.Not.Null, game.DisplayName);
                Assert.That(AssetDatabase.GetAssetPath(sprite),
                    Does.StartWith("Assets/MazeParty/Art/Minigames/RuleCards/"),
                    game.DisplayName);
                Assert.That(uniqueCards.Add(sprite), Is.True, game.DisplayName);
            }

            Assert.That(uniqueCards.Count, Is.EqualTo(MinigameCatalog.RegisteredCount));
        }

        private static void AssertBoardEventPopupBindings(
            GameObject root,
            BoardCanvasBindings bindings)
        {
            var panel = bindings.BoardEventPopupPanel;
            var message = bindings.BoardEventPopupMessage;
            Assert.That(panel, Is.Not.Null);
            Assert.That(message, Is.Not.Null);
            Assert.That(panel.name, Is.EqualTo("BoardEventPopupPanel"));
            Assert.That(message.name, Is.EqualTo("BoardEventPopupMessage"));
            Assert.That(message.transform.IsChildOf(panel.transform), Is.True);
            Assert.That(panel.activeSelf, Is.False,
                "The server-owned event popup must start hidden.");
            Assert.That(message.raycastTarget, Is.False);

            var background = panel.GetComponent<Image>();
            Assert.That(background, Is.Not.Null);
            Assert.That(background.raycastTarget, Is.False,
                "The automatic event popup must not intercept player input.");
            Assert.That(panel.GetComponentsInChildren<Button>(true), Is.Empty,
                "The server-timed event popup is not user-dismissible.");

            var transforms = root.GetComponentsInChildren<Transform>(true);
            Assert.That(transforms.Count(candidate =>
                candidate.name == "BoardEventPopupPanel"), Is.EqualTo(1));
        }

        private static void AssertItemIconBindings(
            GameObject root,
            BoardCanvasBindings bindings)
        {
            Assert.That(bindings.InventorySlotIcons.Length,
                Is.EqualTo(GameplayInventory.Capacity));
            Assert.That(bindings.ShopOfferIcons.Length,
                Is.EqualTo(ItemShopRules.OfferCount));

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var slot = 0; slot < GameplayInventory.Capacity; slot++)
            {
                AssertItemIcon(
                    transforms,
                    bindings.InventorySlotIcons[slot],
                    "BoardInventorySlot" + slot);
            }

            for (var offer = 0; offer < ItemShopRules.OfferCount; offer++)
            {
                AssertItemIcon(
                    transforms,
                    bindings.ShopOfferIcons[offer],
                    "ItemShopOffer" + offer);
            }
        }

        private static void AssertItemIcon(
            IEnumerable<Transform> transforms,
            Image icon,
            string expectedParentName)
        {
            Assert.That(icon, Is.Not.Null, expectedParentName);
            Assert.That(icon.preserveAspect, Is.True, expectedParentName);
            Assert.That(icon.raycastTarget, Is.False, expectedParentName);

            var expectedParent = transforms.SingleOrDefault(candidate =>
                candidate.name == expectedParentName);
            Assert.That(expectedParent, Is.Not.Null, expectedParentName);
            Assert.That(icon.transform.IsChildOf(expectedParent), Is.True,
                expectedParentName);
        }

        private static void AssertPlayerHealthSliderBindings(
            GameObject root,
            BoardCanvasBindings bindings)
        {
            Assert.That(bindings.PlayerHealthSliders.Length,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));
            Assert.That(bindings.PlayerHealthFills.Length,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var card = transforms.SingleOrDefault(candidate =>
                    candidate.name == "PlayerCard" + slot);
                Assert.That(card, Is.Not.Null, "PlayerCard" + slot);

                var slider = bindings.PlayerHealthSliders[slot];
                var fill = bindings.PlayerHealthFills[slot];
                Assert.That(slider, Is.Not.Null, "PlayerHealthBar" + slot);
                Assert.That(slider.name,
                    Is.EqualTo("PlayerHealthBar" + slot));
                Assert.That(slider.transform.IsChildOf(card), Is.True);
                Assert.That(slider.direction,
                    Is.EqualTo(Slider.Direction.LeftToRight));
                Assert.That(slider.minValue, Is.EqualTo(0f));
                Assert.That(slider.maxValue, Is.EqualTo(1f));
                Assert.That(slider.wholeNumbers, Is.False);
                Assert.That(slider.interactable, Is.False);
                Assert.That(slider.transition,
                    Is.EqualTo(Selectable.Transition.None));
                Assert.That(slider.navigation.mode,
                    Is.EqualTo(Navigation.Mode.None));
                Assert.That(slider.handleRect, Is.Null);
                Assert.That(fill, Is.Not.Null, "PlayerHealthFill" + slot);
                Assert.That(fill.type, Is.EqualTo(Image.Type.Simple));
                Assert.That(slider.fillRect, Is.SameAs(fill.rectTransform));

                foreach (var graphic in
                         slider.GetComponentsInChildren<Graphic>(true))
                {
                    Assert.That(
                        graphic.raycastTarget,
                        Is.False,
                        graphic.name);
                }
            }
        }

        [TestCase(
            BoardFlowState.LandingEffectResolve,
            false,
            BoardLandingEffectType.SpecialEvent,
            "P1 gives P2 one key.",
            true)]
        [TestCase(
            BoardFlowState.LandingEffectResolve,
            true,
            BoardLandingEffectType.SpecialEvent,
            "P1 gives P2 one key.",
            false)]
        [TestCase(
            BoardFlowState.LandingEffectResolve,
            false,
            BoardLandingEffectType.SpecialEvent,
            "",
            false)]
        [TestCase(
            BoardFlowState.LandingEffectResolve,
            false,
            BoardLandingEffectType.ItemReward,
            "P1 receives an item.",
            false)]
        [TestCase(
            BoardFlowState.Action,
            false,
            BoardLandingEffectType.SpecialEvent,
            "P1 gives P2 one key.",
            false)]
        public void BoardEventPopup_OnlyShowsDuringActiveLandingResolution(
            BoardFlowState state,
            bool isGlobalSimulationPaused,
            BoardLandingEffectType effectType,
            string message,
            bool expected)
        {
            Assert.That(
                BoardFlowView.ShouldShowBoardEventPopup(
                    state,
                    isGlobalSimulationPaused,
                    effectType,
                    message),
                Is.EqualTo(expected));
        }

        private static void AssertPlayerCurrencyBindings(
            GameObject root,
            BoardCanvasBindings bindings)
        {
            Assert.That(bindings.PlayerKeyIcons.Length,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));
            Assert.That(bindings.PlayerKeyTexts.Length,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));
            Assert.That(bindings.PlayerGoldIcons.Length,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));
            Assert.That(bindings.PlayerGoldTexts.Length,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));

            var transforms = root.GetComponentsInChildren<Transform>(true);
            for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
            {
                var card = transforms.SingleOrDefault(candidate =>
                    candidate.name == "PlayerCard" + slot);
                Assert.That(card, Is.Not.Null, "PlayerCard" + slot);

                AssertCurrencyIcon(
                    bindings.PlayerKeyIcons[slot],
                    card,
                    "PlayerKeyIcon" + slot,
                    KeyIconPath);
                AssertCurrencyText(
                    bindings.PlayerKeyTexts[slot],
                    card,
                    "PlayerKeyText" + slot);
                AssertCurrencyIcon(
                    bindings.PlayerGoldIcons[slot],
                    card,
                    "PlayerGoldIcon" + slot,
                    GoldIconPath);
                AssertCurrencyText(
                    bindings.PlayerGoldTexts[slot],
                    card,
                    "PlayerGoldText" + slot);
            }
        }

        private static void AssertCurrencyIcon(
            RawImage icon,
            Transform card,
            string expectedName,
            string expectedTexturePath)
        {
            Assert.That(icon, Is.Not.Null, expectedName);
            Assert.That(icon.name, Is.EqualTo(expectedName));
            Assert.That(icon.raycastTarget, Is.False, expectedName);
            Assert.That(icon.transform.IsChildOf(card), Is.True, expectedName);
            Assert.That(
                AssetDatabase.GetAssetPath(icon.texture),
                Is.EqualTo(expectedTexturePath),
                expectedName);
        }

        private static void AssertCurrencyText(
            Text text,
            Transform card,
            string expectedName)
        {
            Assert.That(text, Is.Not.Null, expectedName);
            Assert.That(text.name, Is.EqualTo(expectedName));
            Assert.That(text.raycastTarget, Is.False, expectedName);
            Assert.That(text.transform.IsChildOf(card), Is.True, expectedName);
        }
    }
}
