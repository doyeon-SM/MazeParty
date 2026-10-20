using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardUiPrefabTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        private const string HudPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab";
        private const string MapPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab";
        private const string ItemPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardItemCanvas.prefab";
        private const string BoardScenePath =
            "Assets/MazeParty/Scenes/Board/Board.unity";
        private const string KeyIconPath =
            "Assets/Ignore/Icon_NCI/free-icon-door-key-63432.png";
        private const string GoldIconPath =
            "Assets/Ignore/Icon_NCI/free-icon-dollar-coin-7022685.png";
        private const string KillFeedModulePath =
            "Assets/MazeParty/Prefabs/Board/UI/Modules/BoardKillFeedPanel.prefab";
        private const string ReadyPanelModulePath =
            "Assets/MazeParty/Prefabs/Board/UI/Modules/MinigameReadyPanel.prefab";
        private const string LocalizationTablePath =
            "Assets/MazeParty/Resources/MazeParty/Localization/StringTable.csv";
        private const string InputSpriteAssetPath =
            "Assets/Ignore/Input Sprites for TextMesh Pro/all input icons same size.asset";

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
            },
            {
                "BoardKillFeedPanel",
                KillFeedModulePath
            }
        };

        [Test]
        public void BoardCanvas_IsNonVisualAggregateOfExactlyThreeAuthoredCanvases()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            var bindings = prefab.GetComponent<BoardCanvasBindings>();
            Assert.That(bindings, Is.Not.Null);
            Assert.That(bindings.HasRequiredReferences, Is.True);
            AssertAggregateCanvasContract(prefab, bindings);

            var hudPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            Assert.That(hudPrefab, Is.Not.Null, HudPrefabPath);
            for (var index = 0; index < NestedModules.GetLength(0); index++)
            {
                var objectName = NestedModules[index, 0];
                var modulePath = NestedModules[index, 1];
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<GameObject>(modulePath),
                    Is.Not.Null,
                    modulePath);
                var module = hudPrefab.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(candidate => candidate.name == objectName);
                Assert.That(module, Is.Not.Null, objectName);
                Assert.That(
                    PrefabUtility.GetCorrespondingObjectFromSource(
                        module.gameObject),
                    Is.Not.Null,
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
            AssertSplitOwnershipBindings(bindings);
            AssertReticleBindings(bindings);
            Assert.That(bindings.StatusText, Is.Null,
                "BoardStatusText was intentionally removed from the authored HUD.");
            Assert.That(prefab.GetComponentsInChildren<Text>(true)
                .Any(text => text.name == "BoardStatusText"), Is.False);
            Assert.That(
                bindings.Item.GetComponent<BoardUtilityItemView>()
                    .HasRequiredReferences,
                Is.True);
            AssertItemIconBindings(prefab, bindings);
            AssertPlayerCurrencyBindings(prefab, bindings);
            AssertPlayerHealthSliderBindings(prefab, bindings);
            AssertLandingDeltaBindings(prefab, bindings.Hud);
            AssertBoardEventPopupBindings(prefab, bindings);
            AssertBoardKillFeedBindings(prefab, bindings);
            AssertMinigameReadyPresentationBindings(prefab, bindings);

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
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    BoardScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var sceneBindingsInstances = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<BoardCanvasBindings>(true))
                    .ToArray();
                Assert.That(sceneBindingsInstances, Has.Length.EqualTo(1));
                var sceneBindings = sceneBindingsInstances[0];
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        sceneBindings.gameObject),
                    Is.EqualTo(PrefabPath));
                Assert.That(sceneBindings.HasRequiredReferences, Is.True);
                AssertAggregateCanvasContract(
                    sceneBindings.gameObject,
                    sceneBindings);
                AssertSplitOwnershipBindings(sceneBindings);
                AssertReticleBindings(sceneBindings);
                Assert.That(sceneBindings.StatusText, Is.Null);
                Assert.That(sceneBindings.GetComponentsInChildren<Text>(true)
                    .Any(text => text.name == "BoardStatusText"), Is.False);
                AssertItemIconBindings(sceneBindings.gameObject, sceneBindings);
                AssertPlayerCurrencyBindings(
                    sceneBindings.gameObject,
                    sceneBindings);
                AssertPlayerHealthSliderBindings(
                    sceneBindings.gameObject,
                    sceneBindings);
                AssertLandingDeltaBindings(
                    sceneBindings.gameObject,
                    sceneBindings.Hud);
                AssertBoardEventPopupBindings(
                    sceneBindings.gameObject,
                    sceneBindings);
                AssertBoardKillFeedBindings(
                    sceneBindings.gameObject,
                    sceneBindings);
                AssertMinigameReadyPresentationBindings(
                    sceneBindings.gameObject,
                    sceneBindings);

                var world = scene.GetRootGameObjects().SelectMany(root =>
                    root.GetComponentsInChildren<BoardWorldEffectsPresenter>(true)).Single();
                Assert.That(world.HasRequiredReferences, Is.True);
                Assert.That(AssetDatabase.GetAssetPath(world.MarkerPrefab),
                    Is.EqualTo("Assets/MazeParty/Prefabs/Board/UI/BoardTileEffectWorld.prefab"));
                Assert.That(AssetDatabase.GetAssetPath(world.MapAppearance), Is.EqualTo(MapPrefabPath));
                foreach (var impact in world.Impacts)
                {
                    Assert.That(impact.prefab.GetComponentsInChildren<Collider>(true), Is.Empty);
                    Assert.That(impact.prefab.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true), Is.Empty);
                    Assert.That(impact.sound.HasClips, Is.True);
                    Assert.That(impact.sound.Loop, Is.False);
                }
                var marker = (GameObject)PrefabUtility.InstantiatePrefab(world.MarkerPrefab.gameObject, scene);
                try
                {
                    Assert.That(marker.GetComponent<BoardTileEffectWorldView>().HasRequiredReferences, Is.True);
                    Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(marker),
                        Is.SameAs(world.MarkerPrefab.gameObject));
                }
                finally { Object.DestroyImmediate(marker); }

                var utility =
                    sceneBindings.Item.GetComponent<BoardUtilityItemView>();
                Assert.That(utility.HasRequiredReferences, Is.True);
                Assert.That(
                    PrefabUtility.GetCorrespondingObjectFromSource(utility),
                    Is.Not.Null);
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
                var bindings = instance.GetComponent<BoardCanvasBindings>();
                Assert.That(view, Is.Not.Null);
                Assert.That(bindings, Is.Not.Null);
                Assert.That(instance.GetComponent<Canvas>(), Is.Null);
                var canvases = new[]
                {
                    bindings.Hud.RootCanvas,
                    bindings.Map.RootCanvas,
                    bindings.Item.RootCanvas
                };
                var raycasters = new[]
                {
                    bindings.Hud.RootRaycaster,
                    bindings.Map.RootRaycaster,
                    bindings.Item.RootRaycaster
                };
                Assert.That(canvases.All(canvas => canvas != null), Is.True);
                Assert.That(
                    raycasters.All(raycaster => raycaster != null),
                    Is.True);
                Canvas.ForceUpdateCanvases();
                Assert.That(
                    new[]
                    {
                        bindings.Hud.transform,
                        bindings.Map.transform,
                        bindings.Item.transform
                    }.All(module => module.localScale == Vector3.one),
                    Is.True,
                    "Instantiated root Canvases must resolve to unit scale.");

                var cameraRoot = new GameObject("Inventory camera mode");
                cameraRoot.transform.SetParent(instance.transform);
                var director = cameraRoot.AddComponent<GameplayCameraDirector>();
                view.Configure(director);
                const System.Reflection.BindingFlags privateInstance =
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var refresh = typeof(BoardFlowView).GetMethod("LateUpdate", privateInstance);
                foreach (var state in new[]
                {
                    (GameplayMode.BoardTopDown, false, false),
                    (GameplayMode.FirstPerson, true, false),
                    (GameplayMode.FirstPerson, false, true),
                    (GameplayMode.BoardTopDown, true, false),
                    (GameplayMode.BoardTopDown, false, false),
                    (GameplayMode.FirstPerson, false, true)
                })
                {
                    typeof(GameplayCameraDirector).GetField("activeMode", privateInstance).SetValue(director, state.Item1);
                    typeof(GameplayCameraDirector).GetField("_isTransitioning", privateInstance).SetValue(director, state.Item2);
                    refresh.Invoke(view, null);
                    Assert.That(bindings.Item.InventoryPanel.activeSelf, Is.EqualTo(state.Item3),
                        state.Item1 + " / transitioning: " + state.Item2);
                    Assert.That(bindings.Item.InventorySlotBackgrounds.All(slot =>
                        slot.gameObject.activeInHierarchy == state.Item3), Is.True);
                    Assert.That(bindings.Map.RootCanvas.enabled, Is.True,
                        "Hiding inventory must keep the map available.");
                }

                view.SetBoardUiVisible(false);
                Assert.That(view.BoardUiVisible, Is.False);
                Assert.That(
                    canvases.All(canvas => !canvas.enabled),
                    Is.True);
                Assert.That(
                    raycasters.All(raycaster => !raycaster.enabled),
                    Is.True);

                view.SetBoardUiVisible(true);
                Assert.That(view.BoardUiVisible, Is.True);
                Assert.That(
                    canvases.All(canvas => canvas.enabled),
                    Is.True);
                Assert.That(
                    raycasters.All(raycaster => raycaster.enabled),
                    Is.True);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        private static void AssertAggregateCanvasContract(
            GameObject aggregate,
            BoardCanvasBindings bindings)
        {
            Assert.That(aggregate.GetComponent<Canvas>(), Is.Null);
            Assert.That(aggregate.GetComponent<CanvasScaler>(), Is.Null);
            Assert.That(aggregate.GetComponent<GraphicRaycaster>(), Is.Null);
            Assert.That(
                aggregate.GetComponent<BoardEventSystemBootstrap>(),
                Is.Null);
            Assert.That(aggregate.GetComponent<Graphic>(), Is.Null);
            Assert.That(aggregate.GetComponent<BoardFlowView>(), Is.Not.Null);
            Assert.That(aggregate.transform.childCount, Is.EqualTo(3));

            var handEmoteWheel =
                bindings.Hud.transform.Find("Hand Emote Wheel");
            Assert.That(handEmoteWheel, Is.Not.Null);
            Assert.That(
                handEmoteWheel.parent,
                Is.SameAs(bindings.Hud.transform));
            Assert.That(
                handEmoteWheel.GetSiblingIndex(),
                Is.EqualTo(bindings.Hud.transform.childCount - 1));
            Assert.That(handEmoteWheel.GetComponent<Canvas>(), Is.Null);
            Assert.That(
                handEmoteWheel.GetComponent<GraphicRaycaster>(),
                Is.Null);
            Assert.That(
                aggregate.GetComponentsInChildren<Canvas>(true),
                Has.Length.EqualTo(3));

            AssertNestedCanvas(
                aggregate,
                "BoardHudCanvas",
                HudPrefabPath,
                bindings.Hud);
            AssertNestedCanvas(
                aggregate,
                "BoardMapCanvas",
                MapPrefabPath,
                bindings.Map);
            AssertNestedCanvas(
                aggregate,
                "BoardItemCanvas",
                ItemPrefabPath,
                bindings.Item);

            var descendantNames = aggregate
                .GetComponentsInChildren<Transform>(true)
                .Select(child => child.name)
                .ToArray();
            Assert.That(descendantNames, Does.Not.Contain("DiceText"));
            Assert.That(
                descendantNames,
                Does.Not.Contain("Board Overview Map"));
        }

        private static void AssertNestedCanvas(
            GameObject aggregate,
            string objectName,
            string prefabPath,
            Component binding)
        {
            Assert.That(
                AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath),
                Is.Not.Null,
                prefabPath);
            var child = aggregate.transform.Cast<Transform>()
                .SingleOrDefault(candidate => candidate.name == objectName);
            Assert.That(child, Is.Not.Null, objectName);
            Assert.That(binding, Is.Not.Null, objectName);
            Assert.That(binding.gameObject, Is.SameAs(child.gameObject));
            Assert.That(child.GetComponent<Canvas>(), Is.Not.Null, objectName);
            Assert.That(
                child.GetComponent<CanvasScaler>(),
                Is.Not.Null,
                objectName);
            Assert.That(
                child.GetComponent<GraphicRaycaster>(),
                Is.Not.Null,
                objectName);
            Assert.That(
                PrefabUtility.GetCorrespondingObjectFromSource(
                    child.gameObject),
                Is.Not.Null,
                objectName);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    child.gameObject),
                Is.EqualTo(prefabPath),
                objectName);
        }

        private static void AssertSplitOwnershipBindings(
            BoardCanvasBindings bindings)
        {
            Assert.That(bindings.Hud, Is.Not.Null);
            Assert.That(bindings.Map, Is.Not.Null);
            Assert.That(bindings.Item, Is.Not.Null);
            Assert.That(bindings.Hud.HasRequiredReferences, Is.True);
            Assert.That(bindings.Map.HasRequiredReferences, Is.True);
            Assert.That(bindings.Item.HasRequiredReferences, Is.True);
            Assert.That(bindings.Item.InventorySlotBackgrounds.All(slot =>
                slot.transform.IsChildOf(bindings.Item.InventoryPanel.transform)), Is.True,
                "The serialized inventory root must own all slots in prefab and scene instances.");

            var badges =
                bindings.Hud.GetComponent<BoardPlayerStatusBadges>();
            Assert.That(badges, Is.Not.Null);
            Assert.That(badges.HasRequiredReferences, Is.True);
            Assert.That(
                bindings.Map.GetComponent<BoardMapView>(),
                Is.SameAs(bindings.Map.MapView));
            Assert.That(
                bindings.Item.GetComponent<BoardUtilityItemView>(),
                Is.Not.Null);

            Assert.That(bindings.MovesText.name, Is.EqualTo("MovesText"));
            Assert.That(
                bindings.MovesText.transform.IsChildOf(
                    bindings.Map.transform),
                Is.True);
            Assert.That(bindings.AmmoText.name, Is.EqualTo("BoardAmmoText"));
            Assert.That(
                bindings.AmmoText.transform.IsChildOf(
                    bindings.Item.transform),
                Is.True);

            var prompt = bindings.Item.InteractionPrompt;
            Assert.That(prompt, Is.Not.Null);
            Assert.That(bindings.InteractionPrompt, Is.SameAs(prompt));
            Assert.That(prompt.name, Is.EqualTo("BoardInteractionPrompt"));
            Assert.That(
                prompt.transform.IsChildOf(bindings.Item.transform),
                Is.True);
            Assert.That(prompt.gameObject.activeSelf, Is.False);
            Assert.That(prompt.raycastTarget, Is.False);
        }




        [Test]
        public void MinigameControlGuides_CoverEveryRegisteredMinigame()
        {
            Assert.That(
                BoardFlowView.GetMinigameControlGuide(
                    ScheduledMinigameId.Skip),
                Is.Empty);
            foreach (var game in MinigameCatalog.RegisteredMinigames)
            {
                var rows = BoardFlowView.GetMinigameControlGuide(game.Id);
                Assert.That(rows, Is.Not.Empty, game.DisplayName);
                Assert.That(rows.Count, Is.LessThanOrEqualTo(4), game.DisplayName);
                Assert.That(
                    rows.All(row => row.Contains("<sprite name=\"")),
                    Is.True,
                    game.DisplayName);
            }
        }

        [Test]
        public void MinigameDescriptions_CoverEveryRegisteredGameAndLanguage()
        {
            var tableAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                LocalizationTablePath);
            Assert.That(tableAsset, Is.Not.Null);
            var table = StringTable.Parse(tableAsset.text);
            var previousLanguage = GameText.Language;
            try
            {
                Assert.That(
                    MinigameCatalog.GetDescription(ScheduledMinigameId.Skip),
                    Is.Empty);
                foreach (var game in MinigameCatalog.RegisteredMinigames)
                {
                    var source = MinigameCatalog.GetDescriptionSource(game.Id);
                    Assert.That(source, Is.Not.Empty, game.DisplayName);
                    foreach (var language in new[]
                             {
                                 GameLanguage.Korean,
                                 GameLanguage.Japanese,
                                 GameLanguage.ChineseSimplified
                             })
                    {
                        Assert.That(table.TryGet(source, language,
                            out var translation), Is.True, game.DisplayName);
                        GameText.SetLanguage(language);
                        Assert.That(MinigameCatalog.GetDescription(game.Id),
                            Is.EqualTo(translation), game.DisplayName);
                    }
                }
            }
            finally
            {
                GameText.SetLanguage(previousLanguage);
            }
        }

        [Test]
        public void MinigameControlGuides_TranslateDescriptionsAndKeepInputSprites()
        {
            var previousLanguage = GameText.Language;
            try
            {
                GameText.SetLanguage(GameLanguage.English);
                var english = MinigameCatalog.RegisteredMinigames.ToDictionary(
                    game => game.Id,
                    game => BoardFlowView.GetMinigameControlGuide(game.Id)
                        .ToArray());
                var tableAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                    LocalizationTablePath);
                Assert.That(tableAsset, Is.Not.Null);
                var table = StringTable.Parse(tableAsset.text);

                foreach (var language in new[]
                         {
                             GameLanguage.Korean,
                             GameLanguage.Japanese,
                             GameLanguage.ChineseSimplified
                         })
                {
                    GameText.SetLanguage(language);
                    foreach (var game in MinigameCatalog.RegisteredMinigames)
                    {
                        var translated = BoardFlowView.GetMinigameControlGuide(
                            game.Id);
                        var sources = english[game.Id];
                        Assert.That(translated.Count, Is.EqualTo(sources.Length));
                        for (var row = 0; row < sources.Length; row++)
                        {
                            var split = sources[row].LastIndexOf(
                                ">  ", System.StringComparison.Ordinal);
                            Assert.That(split, Is.GreaterThanOrEqualTo(0));
                            var iconMarkup = sources[row].Substring(0, split + 3);
                            var label = sources[row].Substring(split + 3);
                            Assert.That(table.TryGet(label, language,
                                out var expected), Is.True, label);
                            Assert.That(translated[row],
                                Is.EqualTo(iconMarkup + expected),
                                game.DisplayName);
                        }
                    }
                }
            }
            finally
            {
                GameText.SetLanguage(previousLanguage);
            }
        }

        [Test]
        public void ReadyControls_UseLocalizedHeadingAndMatchingFonts()
        {
            var ready = AssetDatabase.LoadAssetAtPath<GameObject>(
                ReadyPanelModulePath);
            Assert.That(ready, Is.Not.Null);
            var title = ready.GetComponentsInChildren<Transform>(true)
                .FirstOrDefault(child => child.name == "MinigameControlsTitle");
            Assert.That(title, Is.Not.Null);
            var localized = title.GetComponent<LocalizedText>();
            Assert.That(localized, Is.Not.Null);
            Assert.That(localized.SourceText, Is.EqualTo("CONTROLS"));

            var controlFonts = ready.GetComponent<MinigameReadyControlFonts>();
            Assert.That(controlFonts, Is.Not.Null);
            Assert.That(controlFonts.HasRequiredReferences, Is.True);
            Assert.That(AssetDatabase.GetAssetPath(
                controlFonts.GetFont(GameLanguage.Korean).sourceFontFile),
                Is.EqualTo(GameFonts.EnglishKoreanAssetPath));
            Assert.That(AssetDatabase.GetAssetPath(
                controlFonts.GetFont(GameLanguage.Japanese).sourceFontFile),
                Is.EqualTo(GameFonts.JapaneseAssetPath));
            Assert.That(AssetDatabase.GetAssetPath(
                controlFonts.GetFont(GameLanguage.ChineseSimplified)
                    .sourceFontFile),
                Is.EqualTo(GameFonts.ChineseSimplifiedAssetPath));
            Assert.That(controlFonts.GetFont(GameLanguage.Korean)
                .HasCharacter('조', true, true), Is.True);
            Assert.That(controlFonts.GetFont(GameLanguage.Japanese)
                .HasCharacter('作', true, true), Is.True);
            Assert.That(controlFonts.GetFont(GameLanguage.ChineseSimplified)
                .HasCharacter('说', true, true), Is.True);

            var previousLanguage = GameText.Language;
            var instance = Object.Instantiate(ready);
            try
            {
                var instanceFonts =
                    instance.GetComponent<MinigameReadyControlFonts>();
                var row = instance.GetComponentsInChildren<TMP_Text>(true)
                    .First(child => child.name == "MinigameControlRow0");
                var inputSprites = row.spriteAsset;
                foreach (var language in new[]
                         {
                             GameLanguage.English,
                             GameLanguage.Korean,
                             GameLanguage.Japanese,
                             GameLanguage.ChineseSimplified
                         })
                {
                    GameText.SetLanguage(language);
                    instanceFonts.Apply();
                    Assert.That(row.font,
                        Is.SameAs(instanceFonts.GetFont(language)));
                    Assert.That(row.spriteAsset, Is.SameAs(inputSprites));
                }
            }
            finally
            {
                GameText.SetLanguage(previousLanguage);
                Object.DestroyImmediate(instance);
            }
        }

        [TestCase(BoardFlowState.MinigameIntroReady, false, false, true)]
        [TestCase(BoardFlowState.MinigameLoading, false, false, true)]
        [TestCase(BoardFlowState.MinigamePlaying, false, false, false)]
        [TestCase(BoardFlowState.MinigameIntroReady, true, false, false)]
        [TestCase(BoardFlowState.MinigameIntroReady, false, true, false)]
        public void MinigameReadyPresentation_OnlyRunsWhilePanelIsVisible(
            BoardFlowState state,
            bool globallyPaused,
            bool reconnectPaused,
            bool expected)
        {
            Assert.That(
                BoardFlowView.ShouldShowMinigameReadyPresentation(
                    state,
                    globallyPaused,
                    reconnectPaused),
                Is.EqualTo(expected));
        }

        private static void AssertMinigameReadyPresentationBindings(
            GameObject root,
            BoardCanvasBindings bindings)
        {
            var panel = bindings.MinigameReadyPanel;
            Assert.That(panel, Is.Not.Null);
            Assert.That(bindings.MinigameDescription, Is.Not.Null);
            Assert.That(bindings.MinigameDescription.transform.IsChildOf(
                panel.transform), Is.True);
            Assert.That(bindings.MinigamePreviewPlaceholder, Is.Not.Null);
            Assert.That(bindings.MinigamePreviewImage, Is.Not.Null);
            Assert.That(bindings.MinigamePreviewPlayer, Is.Not.Null);
            Assert.That(bindings.MinigameControlRows, Has.Length.EqualTo(4));

            var descendants = panel.GetComponentsInChildren<Transform>(true);
            Assert.That(descendants.Any(candidate =>
                candidate.name == "MinigameRuleImage"), Is.False);
            Assert.That(descendants.Any(candidate =>
                candidate.name == "MinigameRulePlaceholderText"), Is.False);

            var playerStates = bindings.MinigameReadyPlayerStates;
            Assert.That(playerStates, Has.Length.EqualTo(4));
            for (var slot = 0; slot < playerStates.Length; slot++)
            {
                Assert.That(playerStates[slot].transform.IsChildOf(
                    panel.transform), Is.True);
            }

            var inputSprites = AssetDatabase.LoadAssetAtPath<TMP_SpriteAsset>(
                InputSpriteAssetPath);
            Assert.That(inputSprites, Is.Not.Null, InputSpriteAssetPath);
            for (var row = 0; row < bindings.MinigameControlRows.Length; row++)
            {
                var control = bindings.MinigameControlRows[row];
                Assert.That(control, Is.Not.Null, "control row " + row);
                Assert.That(control.transform.IsChildOf(panel.transform), Is.True);
                Assert.That(control.spriteAsset, Is.SameAs(inputSprites));
                Assert.That(control.fontStyle, Is.EqualTo(FontStyles.Normal));
                Assert.That(control.fontWeight, Is.EqualTo(FontWeight.Regular));
            }

            var preview = bindings.MinigamePreviewPlayer;
            Assert.That(preview.playOnAwake, Is.False);
            Assert.That(preview.isLooping, Is.True);
            Assert.That(preview.waitForFirstFrame, Is.True);
            Assert.That(preview.source, Is.EqualTo(VideoSource.VideoClip));
            Assert.That(preview.renderMode, Is.EqualTo(VideoRenderMode.APIOnly));
            Assert.That(preview.audioOutputMode,
                Is.EqualTo(VideoAudioOutputMode.None));
            Assert.That(preview.clip, Is.Null);
            Assert.That(bindings.MinigamePreviewImage.texture, Is.Null);
            Assert.That(bindings.MinigamePreviewImage.enabled, Is.False);
            Assert.That(bindings.MinigamePreviewImage.transform.IsChildOf(
                panel.transform), Is.True);

            Assert.That(
                bindings.GetMinigamePreviewClip(ScheduledMinigameId.Skip),
                Is.Null);
            Assert.That(
                bindings.MinigamePreviewClipSlotCount,
                Is.GreaterThan((int)ScheduledMinigameId.CliffBarrage));

            Assert.That(
                panel.GetComponentsInChildren<Text>(true).All(text =>
                    text.fontStyle == FontStyle.Normal),
                Is.True,
                "Every legacy Text in the ready panel must use Normal style.");
            Assert.That(
                panel.GetComponentsInChildren<TMP_Text>(true).All(text =>
                    text.fontStyle == FontStyles.Normal &&
                    text.fontWeight == FontWeight.Regular),
                Is.True,
                "Every TMP text in the ready panel must use Normal style.");

            Assert.That(root.GetComponentsInChildren<Transform>(true)
                .Count(candidate => candidate.name == "MinigamePreviewVideo"),
                Is.EqualTo(1));
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
            var eventFonts = panel.GetComponent<BoardEventPopupFonts>();
            Assert.That(eventFonts, Is.Not.Null);
            Assert.That(eventFonts.HasRequiredReferences, Is.True);
            var eventView = panel.GetComponent<BoardEventPanelView>();
            Assert.That(eventView, Is.Not.Null);
            Assert.That(eventView.HasRequiredReferences, Is.True);
            Assert.That(bindings.Hud.EventPanelView, Is.SameAs(eventView));
            Assert.That(message.spriteAsset, Is.Not.Null);
            Assert.That(message.spriteAsset.GetSpriteIndexFromName("gold"), Is.GreaterThanOrEqualTo(0));
            Assert.That(message.spriteAsset.GetSpriteIndexFromName("key"), Is.GreaterThanOrEqualTo(0));
            Assert.That(AssetDatabase.GetAssetPath(message.spriteAsset),
                Is.EqualTo("Assets/MazeParty/Fonts/BoardEventResources.asset"));

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

        private static void AssertBoardKillFeedBindings(
            GameObject root,
            BoardCanvasBindings bindings)
        {
            var panel = bindings.BoardKillFeedPanel;
            var message = bindings.BoardKillFeedMessage;
            Assert.That(panel, Is.Not.Null);
            Assert.That(message, Is.Not.Null);
            Assert.That(panel.name, Is.EqualTo("BoardKillFeedPanel"));
            Assert.That(message.name, Is.EqualTo("BoardKillFeedMessage"));
            Assert.That(message.transform.IsChildOf(panel.transform), Is.True);
            Assert.That(panel.activeSelf, Is.False,
                "The server-owned kill feed must start hidden.");
            Assert.That(message.supportRichText, Is.True);
            Assert.That(message.raycastTarget, Is.False);
            var view = panel.GetComponent<BoardKillFeedView>();
            Assert.That(view, Is.Not.Null);
            Assert.That(view.HasRequiredReferences, Is.True);
            Assert.That(view.BeforeItem, Is.SameAs(message));
            Assert.That(view.ItemIcon.transform.IsChildOf(panel.transform), Is.True);
            Assert.That(view.ItemName.transform.IsChildOf(panel.transform), Is.True);
            Assert.That(view.AfterItem.transform.IsChildOf(panel.transform), Is.True);
            Assert.That(view.ItemIcon.raycastTarget, Is.False);
            Assert.That(view.ItemName.raycastTarget, Is.False);
            Assert.That(view.AfterItem.raycastTarget, Is.False);

            var background = panel.GetComponent<Image>();
            Assert.That(background, Is.Not.Null);
            Assert.That(background.raycastTarget, Is.False,
                "The automatic kill feed must not intercept player input.");
            Assert.That(panel.GetComponentsInChildren<Button>(true), Is.Empty,
                "The server-owned kill feed is not user-dismissible.");

            Assert.That(
                PrefabUtility.GetCorrespondingObjectFromSource(panel),
                Is.Not.Null);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(panel),
                Is.EqualTo(KillFeedModulePath));

            var transforms = root.GetComponentsInChildren<Transform>(true);
            Assert.That(transforms.Count(candidate =>
                candidate.name == "BoardKillFeedPanel"), Is.EqualTo(1));
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

        private static void AssertLandingDeltaBindings(
            GameObject root,
            BoardHudCanvasBindings hud)
        {
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.PlayerStatFeedback, Has.Length.EqualTo(4));
            foreach (var feedback in hud.PlayerStatFeedback)
            {
                Assert.That(feedback, Is.Not.Null);
                Assert.That(feedback.HasRequiredReferences, Is.True);
                Assert.That(feedback.transform.IsChildOf(root.transform), Is.True);
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(feedback) ||
                    PrefabUtility.GetCorrespondingObjectFromSource(feedback) != null, Is.True);
                var data = new SerializedObject(feedback);
                foreach (var property in new[] { "health", "healthTrail" })
                {
                    var slider = data.FindProperty(property).objectReferenceValue as Slider;
                    Assert.That(slider, Is.Not.Null, property);
                    Assert.That(slider.interactable, Is.False, property);
                    var emitter = slider.GetComponent<UiSoundEmitter>();
                    Assert.That(emitter, Is.Not.Null, property);
                    Assert.That(emitter.HoverKey, Is.Empty, property);
                    Assert.That(emitter.ClickKey, Is.Empty, property);
                }
            }
            var groups = new[]
            {
                hud.PlayerHealthDeltaTexts,
                hud.PlayerKeyDeltaTexts,
                hud.PlayerGoldDeltaTexts
            };
            var names = new[]
            {
                "PlayerHealthDelta",
                "PlayerKeyDelta",
                "PlayerGoldDelta"
            };
            for (var kind = 0; kind < groups.Length; kind++)
            {
                Assert.That(groups[kind],
                    Has.Length.EqualTo(MultiplayerConstants.MaxPlayers));
                for (var slot = 0; slot < MultiplayerConstants.MaxPlayers; slot++)
                {
                    var label = groups[kind][slot];
                    Assert.That(label, Is.Not.Null);
                    Assert.That(label.name, Is.EqualTo(names[kind] + slot));
                    Assert.That(label.transform.IsChildOf(root.transform), Is.True);
                    Assert.That(label.raycastTarget, Is.False);
                    Assert.That(
                        PrefabUtility.IsPartOfPrefabAsset(label) ||
                        PrefabUtility.GetCorrespondingObjectFromSource(label) != null,
                        Is.True,
                        label.name);
                }
            }
        }

        [Test]
        public void BoardKillFeed_FormatsLocalizedColoredNamesSafely()
        {
            var tableAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(
                LocalizationTablePath);
            Assert.That(tableAsset, Is.Not.Null, LocalizationTablePath);
            GameText.UseTableForTests(StringTable.Parse(tableAsset.text));
            try
            {
                GameText.SetLanguage(GameLanguage.Korean);
                var korean = BoardFlowView.FormatBoardKillFeedMessage(
                    "A<color=red>", Color.red,
                    "B&>", Color.blue,
                    PrototypeItemId.Pistol);
                Assert.That(korean.BeforeItem, Is.EqualTo(
                    "<color=#FF0000>A＜color=red＞</color>이(가) " +
                    "<color=#0000FF>B＆＞</color>을(를) "));
                Assert.That(korean.ItemName, Is.EqualTo(GameText.T("Pistol")));
                Assert.That(korean.AfterItem, Is.EqualTo("으로 처치."));
                var punch = BoardFlowView.FormatBoardKillFeedMessage(
                    "A", Color.red, "B", Color.blue,
                    PrototypeItemId.None);
                Assert.That(punch.ItemName, Is.EqualTo("주먹"));
                Assert.That(punch.AfterItem, Is.EqualTo("으로 처치."));
                var mine = BoardFlowView.FormatBoardKillFeedMessage(
                    "A", Color.red, "B", Color.blue,
                    PrototypeItemId.Mine);
                Assert.That(mine.AfterItem, Is.EqualTo("로 처치."));

                GameText.SetLanguage(GameLanguage.English);
                var english = BoardFlowView.FormatBoardKillFeedMessage(
                    "A", Color.red, "B", Color.blue,
                    PrototypeItemId.Grenade);
                Assert.That(english.BeforeItem,
                    Does.EndWith(" eliminated <color=#0000FF>B</color> with "));
                Assert.That(english.ItemName, Is.EqualTo("Grenade"));
                Assert.That(english.AfterItem, Is.EqualTo("."));

                GameText.SetLanguage(GameLanguage.Japanese);
                var japanese = BoardFlowView.FormatBoardKillFeedMessage(
                    "A", Color.red, "B", Color.blue,
                    PrototypeItemId.Mine);
                Assert.That(japanese.BeforeItem, Does.EndWith("を"));
                Assert.That(japanese.ItemName, Is.EqualTo(GameText.T("Mine")));
                Assert.That(japanese.AfterItem, Is.EqualTo("で撃破。"));

                GameText.SetLanguage(GameLanguage.ChineseSimplified);
                var chinese = BoardFlowView.FormatBoardKillFeedMessage(
                    "A", Color.red, "B", Color.blue,
                    PrototypeItemId.Sniper);
                Assert.That(chinese.BeforeItem, Does.EndWith("用"));
                Assert.That(chinese.ItemName, Is.EqualTo(GameText.T("Sniper")));
                Assert.That(chinese.AfterItem,
                    Does.StartWith("击败了<color=#0000FF>B</color>"));
            }
            finally
            {
                GameText.ResetForTests();
            }
        }

        [Test]
        public void BoardKillFeed_OrdersSimultaneousVictimsByRankAndDisplaysTwoSeconds()
        {
            Assert.That(BoardFlowView.BoardKillFeedEntrySeconds, Is.EqualTo(2f));
            var entries = new List<BoardFlowView.BoardKillFeedEntry>
            {
                new BoardFlowView.BoardKillFeedEntry(
                    "killer", Color.white, "low", Color.white,
                    PrototypeItemId.Mine, 2,
                    new PlayerRankingStats(0, 100, 0), 10f),
                new BoardFlowView.BoardKillFeedEntry(
                    "killer", Color.white, "high", Color.white,
                    PrototypeItemId.Mine, 1,
                    new PlayerRankingStats(1, 0, 0), 10.02f),
                new BoardFlowView.BoardKillFeedEntry(
                    "killer", Color.white, "later", Color.white,
                    PrototypeItemId.Mine, 0,
                    new PlayerRankingStats(9, 999, 9), 10.2f)
            };

            Assert.That(BoardFlowView.SelectBoardKillIndex(entries, 10.1f),
                Is.EqualTo(1));
            entries.RemoveAt(1);
            Assert.That(BoardFlowView.SelectBoardKillIndex(entries, 10.1f),
                Is.EqualTo(0));
            entries.Insert(1, new BoardFlowView.BoardKillFeedEntry(
                "killer", Color.white, "tie", Color.white,
                PrototypeItemId.Mine, 1,
                new PlayerRankingStats(0, 100, 0), 10.04f));
            Assert.That(BoardFlowView.SelectBoardKillIndex(entries, 10.1f),
                Is.EqualTo(1));
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

        [Test]
        public void BoardEventPopup_HidesBeforeTheWorldTransferShots()
        {
            var cases = new[]
            {
                (BoardResourceTransferPhase.None, true),
                (BoardResourceTransferPhase.Result, true),
                (BoardResourceTransferPhase.Source, false),
                (BoardResourceTransferPhase.Destination, false),
                (BoardResourceTransferPhase.Complete, false)
            };

            foreach (var testCase in cases)
            {
                Assert.That(
                    BoardFlowView.ShouldShowBoardEventPopup(
                        BoardFlowState.LandingEffectResolve,
                        false,
                        BoardLandingEffectType.SpecialEvent,
                        "P1 gives P2 one key.",
                        testCase.Item1),
                    Is.EqualTo(testCase.Item2),
                    testCase.Item1.ToString());
            }
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

        private static void AssertReticleBindings(
            BoardCanvasBindings bindings)
        {
            Assert.That(bindings.Reticle, Is.Not.Null);
            Assert.That(bindings.ReticleImage, Is.Not.Null);
            Assert.That(
                bindings.Reticle.transform.IsChildOf(
                    bindings.Hud.transform),
                Is.True);
            Assert.That(
                bindings.ReticleImage.gameObject,
                Is.SameAs(bindings.Reticle));
            Assert.That(
                AssetDatabase.GetAssetPath(bindings.ReticleImage.sprite),
                Is.EqualTo(
                    "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Add.png"));
            Assert.That(bindings.ReticleImage.raycastTarget, Is.False);
            Assert.That(bindings.Reticle.activeSelf, Is.False);
        }
    }
}
