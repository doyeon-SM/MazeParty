using System;
using MazeParty.Gameplay;
using MazeParty.Gameplay.BoardFlowTestbed;
using MazeParty.Multiplayer;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.Video;

namespace MazeParty.Editor
{
    /// <summary>
    /// One-time extraction of the authored monolithic BoardCanvas into three
    /// independently-authored Canvas prefabs. Once extracted, setup only
    /// validates and repairs aggregate references; it never rebuilds module
    /// layout or visual values.
    /// </summary>
    public static class BoardCanvasSplitProjectSetup
    {
        public const string AggregatePrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab";
        public const string HudPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab";
        public const string MapPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab";
        public const string ItemPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/BoardItemCanvas.prefab";

        private static readonly string[] BoardScenePaths =
        {
            "Assets/MazeParty/Scenes/Board/Board.unity",
            "Assets/MazeParty/Scenes/Board/Dev/BoardFlowTestbed.unity"
        };

        private static readonly string[] HudChildren =
        {
            "Header Panel",
            "Player State Panel",
            "MinigameReadyPanel",
            "BoardReticle",
            BoardEventPopupProjectSetup.ModuleObjectName,
            BoardKillFeedProjectSetup.ModuleObjectName,
            "ReconnectOverlay",
            "Hand Emote Wheel"
        };

        private static readonly string[] MapChildren =
        {
            "Board Action Minimap",
            "Board Full Map",
            "MovesText"
        };

        private static readonly string[] ItemChildren =
        {
            "Inventory Panel",
            "BoardAmmoText",
            "ItemSelectionPanel",
            "ItemShopPanel",
            "Position Swap Targets",
            "Utility Item Status",
            "Position Swap Notice"
        };

        [MenuItem("MazeParty/UI/Split Board Canvas Into HUD Map Item")]
        public static void SplitBoardCanvas()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException(
                    "Board Canvas extraction requires Edit Mode.");
            }

            EnsureInstalled();
            RepairSceneInstanceScales();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log(
                "Board UI is authored as independent HUD, map, and item " +
                "Canvas prefabs under the non-visual BoardCanvas aggregate.");
        }

        public static GameObject EnsureInstalled()
        {
            var aggregate = AssetDatabase.LoadAssetAtPath<GameObject>(
                AggregatePrefabPath);
            if (aggregate == null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab is required before the three-Canvas " +
                    "extraction can run.");
            }

            if (IsComplete(aggregate))
            {
                Validate(aggregate);
                return aggregate;
            }

            var alreadySplit = aggregate.GetComponent<Canvas>() == null;
            EnsureModulePrefab(HudPrefabPath, CanvasKind.Hud, alreadySplit);
            EnsureModulePrefab(MapPrefabPath, CanvasKind.Map, alreadySplit);
            EnsureModulePrefab(ItemPrefabPath, CanvasKind.Item, alreadySplit);
            RebuildAggregateReferences();

            aggregate = AssetDatabase.LoadAssetAtPath<GameObject>(
                AggregatePrefabPath);
            Validate(aggregate);
            return aggregate;
        }

        public static void Validate(GameObject aggregate)
        {
            if (aggregate == null || aggregate.GetComponent<Canvas>() != null ||
                aggregate.GetComponent<CanvasScaler>() != null ||
                aggregate.GetComponent<GraphicRaycaster>() != null ||
                aggregate.GetComponent<BoardEventSystemBootstrap>() != null)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab must remain a non-visual aggregate.");
            }

            var bindings = aggregate.GetComponent<BoardCanvasBindings>();
            var view = aggregate.GetComponent<BoardFlowView>();
            if (bindings == null || !bindings.HasRequiredReferences ||
                view == null || !view.HasRequiredUiReferences ||
                view.UiBindings != bindings)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab is missing its three-Canvas aggregate " +
                    "binding contract.");
            }

            ValidateNestedSource(bindings.Hud.gameObject, HudPrefabPath);
            ValidateNestedSource(bindings.Map.gameObject, MapPrefabPath);
            ValidateNestedSource(bindings.Item.gameObject, ItemPrefabPath);
            if (FindDescendant(aggregate.transform, "Board Overview Map") != null ||
                FindDescendant(aggregate.transform, "DiceText") != null)
            {
                throw new InvalidOperationException(
                    "Board Overview Map and DiceText must not exist in the " +
                    "split Board UI.");
            }

            var canvases = aggregate.GetComponentsInChildren<Canvas>(true);
            if (canvases.Length != 3)
            {
                throw new InvalidOperationException(
                    "BoardCanvas.prefab must contain exactly three Canvas " +
                    "components (HUD, map, and item).");
            }
        }

        internal static void ConfigureAggregate(GameObject aggregate)
        {
            if (aggregate == null)
            {
                throw new ArgumentNullException(nameof(aggregate));
            }

            var hud = aggregate.GetComponentInChildren<BoardHudCanvasBindings>(true);
            var map = aggregate.GetComponentInChildren<BoardMapCanvasBindings>(true);
            var item = aggregate.GetComponentInChildren<BoardItemCanvasBindings>(true);
            var bindings = aggregate.GetComponent<BoardCanvasBindings>() ??
                           aggregate.AddComponent<BoardCanvasBindings>();
            var view = aggregate.GetComponent<BoardFlowView>() ??
                       aggregate.AddComponent<BoardFlowView>();
            bindings.Configure(hud, map, item);
            view.ConfigureUiBindings(bindings);
            ConfigureItemChoiceOwners(item, view);
            EditorUtility.SetDirty(bindings);
            EditorUtility.SetDirty(view);
        }

        private static bool IsComplete(GameObject aggregate)
        {
            var bindings = aggregate != null
                ? aggregate.GetComponent<BoardCanvasBindings>()
                : null;
            return aggregate != null &&
                   aggregate.GetComponent<Canvas>() == null &&
                   aggregate.GetComponentsInChildren<Canvas>(true).Length == 3 &&
                   bindings != null &&
                   bindings.HasRequiredReferences &&
                   AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath) != null &&
                   AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath) != null &&
                   AssetDatabase.LoadAssetAtPath<GameObject>(ItemPrefabPath) != null;
        }

        private static void EnsureModulePrefab(
            string targetPath,
            CanvasKind kind,
            bool aggregateAlreadySplit)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (existing != null)
            {
                ValidateModule(existing, kind, targetPath);
                return;
            }

            if (aggregateAlreadySplit)
            {
                throw new InvalidOperationException(
                    targetPath + " is missing. The aggregate is already split, " +
                    "so setup will not recreate and overwrite authored UI.");
            }

            if (!AssetDatabase.CopyAsset(AggregatePrefabPath, targetPath))
            {
                throw new InvalidOperationException(
                    "Could not copy the authored BoardCanvas into " + targetPath + ".");
            }

            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceUpdate);
            var contents = PrefabUtility.LoadPrefabContents(targetPath);
            try
            {
                ConfigureModule(contents, kind);
                if (PrefabUtility.SaveAsPrefabAsset(contents, targetPath) == null)
                {
                    throw new InvalidOperationException(
                        "Could not save the extracted Canvas prefab: " + targetPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            ValidateModule(
                AssetDatabase.LoadAssetAtPath<GameObject>(targetPath),
                kind,
                targetPath);
        }

        private static void ConfigureModule(GameObject root, CanvasKind kind)
        {
            root.name = kind == CanvasKind.Hud
                ? "BoardHudCanvas"
                : kind == CanvasKind.Map
                    ? "BoardMapCanvas"
                    : "BoardItemCanvas";
            root.transform.localScale = Vector3.one;
            root.layer = LayerMask.NameToLayer("UI");

            var keep = kind == CanvasKind.Hud
                ? HudChildren
                : kind == CanvasKind.Map
                    ? MapChildren
                    : ItemChildren;
            PruneDirectChildren(root.transform, keep);

            RemoveComponent<BoardFlowView>(root);
            RemoveComponent<BoardCanvasBindings>(root);
            if (kind != CanvasKind.Hud)
            {
                RemoveComponent<BoardEventSystemBootstrap>(root);
                RemoveComponent<BoardPlayerStatusBadges>(root);
                RemoveComponent<HandEmoteWheelView>(root);
            }
            if (kind != CanvasKind.Map)
            {
                RemoveComponent<BoardMapView>(root);
                RemoveComponent<BoardMinimapView>(root);
            }
            if (kind != CanvasKind.Item)
            {
                RemoveComponent<BoardUtilityItemView>(root);
            }

            var canvas = root.GetComponent<Canvas>() ?? root.AddComponent<Canvas>();
            var scaler = root.GetComponent<CanvasScaler>() ??
                         root.AddComponent<CanvasScaler>();
            var raycaster = root.GetComponent<GraphicRaycaster>() ??
                            root.AddComponent<GraphicRaycaster>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = kind == CanvasKind.Hud
                ? 40
                : kind == CanvasKind.Item ? 30 : 20;
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            _ = raycaster;

            if (root.GetComponent<LocalizedFontScope>() == null)
            {
                root.AddComponent<LocalizedFontScope>();
            }

            RemoveNestedCanvases(root);

            switch (kind)
            {
                case CanvasKind.Hud:
                    ConfigureHud(root);
                    break;
                case CanvasKind.Map:
                    ConfigureMap(root);
                    break;
                case CanvasKind.Item:
                    ConfigureItem(root);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        private static void ConfigureHud(GameObject root)
        {
            var binding = root.GetComponent<BoardHudCanvasBindings>() ??
                          root.AddComponent<BoardHudCanvasBindings>();
            var playerRows = new Text[MultiplayerConstants.MaxPlayers];
            var playerCards = new Image[MultiplayerConstants.MaxPlayers];
            var healthSliders = new Slider[MultiplayerConstants.MaxPlayers];
            var healthFills = new Image[MultiplayerConstants.MaxPlayers];
            var healthTexts = new Text[MultiplayerConstants.MaxPlayers];
            var keyIcons = new RawImage[MultiplayerConstants.MaxPlayers];
            var keyTexts = new Text[MultiplayerConstants.MaxPlayers];
            var goldIcons = new RawImage[MultiplayerConstants.MaxPlayers];
            var goldTexts = new Text[MultiplayerConstants.MaxPlayers];
            var actionIcons = new Text[MultiplayerConstants.MaxPlayers];
            var rankTexts = new Text[MultiplayerConstants.MaxPlayers];
            var readyStates = new Text[MultiplayerConstants.MaxPlayers];
            for (var index = 0; index < MultiplayerConstants.MaxPlayers; index++)
            {
                playerRows[index] = Require<Text>(root, "PlayerState" + index);
                playerCards[index] = Require<Image>(root, "PlayerCard" + index);
                healthSliders[index] = Require<Slider>(root, "PlayerHealthBar" + index);
                healthFills[index] = Require<Image>(root, "PlayerHealthFill" + index);
                healthTexts[index] = Require<Text>(root, "PlayerHealthText" + index);
                keyIcons[index] = Require<RawImage>(root, "PlayerKeyIcon" + index);
                keyTexts[index] = Require<Text>(root, "PlayerKeyText" + index);
                goldIcons[index] = Require<RawImage>(root, "PlayerGoldIcon" + index);
                goldTexts[index] = Require<Text>(root, "PlayerGoldText" + index);
                actionIcons[index] = Require<Text>(root, "PlayerActionIcon" + index);
                rankTexts[index] = Require<Text>(root, "PlayerRank" + index);
                readyStates[index] = Require<Text>(
                    root,
                    "MinigameReadyPlayerState" + index);
            }

            var controls = new TMP_Text[4];
            for (var index = 0; index < controls.Length; index++)
            {
                controls[index] = Require<TMP_Text>(
                    root,
                    "MinigameControlRow" + index);
            }

            var readyButton = Require<Button>(root, "ReadyButton");
            var readyLabel = readyButton.GetComponentInChildren<Text>(true);
            binding.Configure(new BoardHudCanvasBindings.References
            {
                RootCanvas = root.GetComponent<Canvas>(),
                RootRaycaster = root.GetComponent<GraphicRaycaster>(),
                MinigameReadyPanel = RequireObject(root, "MinigameReadyPanel"),
                ReconnectOverlay = RequireObject(root, "ReconnectOverlay"),
                Reticle = RequireObject(root, "BoardReticle"),
                BoardEventPopupPanel = RequireObject(
                    root,
                    BoardEventPopupProjectSetup.ModuleObjectName),
                BoardKillFeedPanel = RequireObject(
                    root,
                    BoardKillFeedProjectSetup.ModuleObjectName),
                ReticleImage = Require<Image>(root, "BoardReticle"),
                TurnText = Require<Text>(root, "TurnText"),
                PhaseText = Require<Text>(root, "PhaseText"),
                PhaseTimerText = Require<Text>(root, "PhaseTimerText"),
                StatusText = null,
                ReconnectText = Require<Text>(root, "ReconnectText"),
                BoardEventPopupMessage = Require<Text>(
                    root,
                    BoardEventPopupProjectSetup.MessageObjectName),
                BoardKillFeedMessage = Require<Text>(
                    root,
                    BoardKillFeedProjectSetup.MessageObjectName),
                MinigameReadyTitle = Require<Text>(root, "Ready Title"),
                MinigameDescription = Require<Text>(
                    root,
                    "MinigameDescriptionText"),
                MinigameReadyStatus = Require<Text>(root, "MinigameReadyStatus"),
                MinigameReadyPlayerStates = readyStates,
                MinigamePreviewPlaceholder = Require<Text>(
                    root,
                    "MinigamePreviewPlaceholder"),
                MinigameControlRows = controls,
                ReadyButtonLabel = readyLabel,
                MinigamePreviewImage = Require<RawImage>(
                    root,
                    "MinigamePreviewVideo"),
                MinigamePreviewPlayer = Require<VideoPlayer>(
                    root,
                    "MinigamePreviewVideo"),
                ReadyButton = readyButton,
                PlayerRows = playerRows,
                PlayerCards = playerCards,
                PlayerHealthSliders = healthSliders,
                PlayerHealthFills = healthFills,
                PlayerHealthTexts = healthTexts,
                PlayerKeyIcons = keyIcons,
                PlayerKeyTexts = keyTexts,
                PlayerGoldIcons = goldIcons,
                PlayerGoldTexts = goldTexts,
                PlayerActionIcons = actionIcons,
                PlayerRankTexts = rankTexts
            });

            var serialized = new SerializedObject(binding);
            var clips = serialized.FindProperty("minigamePreviewClips");
            if (clips != null &&
                clips.arraySize <
                BoardHudCanvasBindings.RequiredMinigamePreviewClipSlotCount)
            {
                clips.arraySize =
                    BoardHudCanvasBindings.RequiredMinigamePreviewClipSlotCount;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static void ConfigureMap(GameObject root)
        {
            var moves = Require<Text>(root, "MovesText");
            var movesRect = moves.rectTransform;
            movesRect.SetParent(root.transform, false);
            movesRect.anchorMin = movesRect.anchorMax = new Vector2(0.5f, 1f);
            movesRect.pivot = new Vector2(0.5f, 1f);
            movesRect.anchoredPosition = new Vector2(0f, -132f);
            movesRect.sizeDelta = new Vector2(220f, 60f);
            moves.alignment = TextAnchor.UpperCenter;
            moves.text = string.Empty;
            moves.raycastTarget = false;
            moves.gameObject.SetActive(false);

            var mapView = root.GetComponent<BoardMapView>();
            if (mapView == null || !mapView.HasRequiredReferences)
            {
                throw new InvalidOperationException(
                    "BoardMapCanvas requires its authored BoardMapView contract.");
            }

            var binding = root.GetComponent<BoardMapCanvasBindings>() ??
                          root.AddComponent<BoardMapCanvasBindings>();
            binding.Configure(
                root.GetComponent<Canvas>(),
                root.GetComponent<GraphicRaycaster>(),
                mapView,
                moves);
        }

        private static void ConfigureItem(GameObject root)
        {
            var inventory = RequireObject(root, "Inventory Panel");
            var ammo = Require<Text>(root, "BoardAmmoText");
            var ammoRect = ammo.rectTransform;
            ammoRect.SetParent(inventory.transform, false);
            ammoRect.anchorMin = ammoRect.anchorMax = Vector2.one;
            ammoRect.pivot = Vector2.one;
            ammoRect.anchoredPosition = new Vector2(-12f, -8f);
            ammoRect.sizeDelta = new Vector2(96f, 48f);
            ammo.alignment = TextAnchor.UpperRight;
            ammo.text = string.Empty;
            ammo.raycastTarget = false;
            ammo.gameObject.SetActive(false);

            var prompt = FindDescendant(root.transform, "BoardInteractionPrompt")
                ?.GetComponent<Text>();
            if (prompt == null)
            {
                var promptObject = new GameObject(
                    "BoardInteractionPrompt",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Text));
                promptObject.layer = LayerMask.NameToLayer("UI");
                promptObject.transform.SetParent(inventory.transform, false);
                var rect = (RectTransform)promptObject.transform;
                rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f, 8f);
                rect.sizeDelta = new Vector2(760f, 42f);
                prompt = promptObject.GetComponent<Text>();
                prompt.font = ammo.font;
                prompt.fontSize = 20;
                prompt.fontStyle = FontStyle.Normal;
                prompt.color = Color.white;
                prompt.alignment = TextAnchor.MiddleCenter;
                prompt.raycastTarget = false;
                prompt.text = string.Empty;
                promptObject.SetActive(false);
            }

            var slotBackgrounds = new Image[GameplayInventory.Capacity];
            var slotIcons = new Image[GameplayInventory.Capacity];
            var choiceButtons = new Button[GameplayInventory.Capacity];
            var choiceLabels = new Text[GameplayInventory.Capacity];
            var choiceHovers = new BoardItemChoiceButton[GameplayInventory.Capacity];
            for (var index = 0; index < GameplayInventory.Capacity; index++)
            {
                slotBackgrounds[index] = Require<Image>(
                    root,
                    "BoardInventorySlot" + index);
                slotIcons[index] = Require<Image>(
                    root,
                    "BoardInventorySlotIcon" + index);
                choiceButtons[index] = Require<Button>(
                    root,
                    "ItemChoiceButton" + index);
                choiceLabels[index] = Require<Text>(
                    root,
                    "ItemChoiceLabel" + index);
                choiceHovers[index] = Require<BoardItemChoiceButton>(
                    root,
                    "ItemChoiceButton" + index);
            }

            var shopButtons = new Button[ItemShopRules.OfferCount];
            var shopIcons = new Image[ItemShopRules.OfferCount];
            var shopLabels = new Text[ItemShopRules.OfferCount];
            var shopHovers = new BoardItemChoiceButton[ItemShopRules.OfferCount];
            for (var index = 0; index < ItemShopRules.OfferCount; index++)
            {
                shopButtons[index] = Require<Button>(root, "ItemShopOffer" + index);
                shopIcons[index] = Require<Image>(
                    root,
                    "ItemShopOfferIcon" + index);
                shopLabels[index] = Require<Text>(
                    root,
                    "ItemShopOfferLabel" + index);
                shopHovers[index] = Require<BoardItemChoiceButton>(
                    root,
                    "ItemShopOffer" + index);
            }

            var binding = root.GetComponent<BoardItemCanvasBindings>() ??
                          root.AddComponent<BoardItemCanvasBindings>();
            binding.Configure(new BoardItemCanvasBindings.References
            {
                RootCanvas = root.GetComponent<Canvas>(),
                RootRaycaster = root.GetComponent<GraphicRaycaster>(),
                ItemSelectionPanel = RequireObject(root, "ItemSelectionPanel"),
                ItemShopPanel = RequireObject(root, "ItemShopPanel"),
                AmmoText = ammo,
                TooltipText = Require<Text>(root, "BoardTooltipText"),
                InteractionPrompt = prompt,
                ItemShopTitle = Require<Text>(root, "ItemShopTitle"),
                ItemShopTooltip = Require<Text>(root, "ItemShopTooltip"),
                ItemShopStatus = Require<Text>(root, "ItemShopStatus"),
                NoItemButton = Require<Button>(root, "NoItemButton"),
                ItemShopCloseButton = Require<Button>(
                    root,
                    "ItemShopCloseButton"),
                InventorySlotBackgrounds = slotBackgrounds,
                InventorySlotIcons = slotIcons,
                ItemChoiceButtons = choiceButtons,
                ItemChoiceLabels = choiceLabels,
                ItemChoiceHovers = choiceHovers,
                ShopOfferButtons = shopButtons,
                ShopOfferIcons = shopIcons,
                ShopOfferLabels = shopLabels,
                ShopOfferHovers = shopHovers
            });
        }

        private static void RebuildAggregateReferences()
        {
            var hudPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HudPrefabPath);
            var mapPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MapPrefabPath);
            var itemPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ItemPrefabPath);
            var contents = PrefabUtility.LoadPrefabContents(AggregatePrefabPath);
            try
            {
                for (var index = contents.transform.childCount - 1;
                     index >= 0;
                     index--)
                {
                    UnityEngine.Object.DestroyImmediate(
                        contents.transform.GetChild(index).gameObject);
                }

                RemoveComponent<GraphicRaycaster>(contents);
                RemoveComponent<CanvasScaler>(contents);
                RemoveComponent<Canvas>(contents);
                RemoveComponent<BoardEventSystemBootstrap>(contents);
                RemoveComponent<BoardMapView>(contents);
                RemoveComponent<BoardPlayerStatusBadges>(contents);
                RemoveComponent<BoardMinimapView>(contents);
                RemoveComponent<BoardUtilityItemView>(contents);
                RemoveComponent<LocalizedFontScope>(contents);
                RemoveComponent<HandEmoteWheelView>(contents);

                contents.name = "BoardCanvas";
                contents.transform.localScale = Vector3.one;
                var hud = InstantiateNested<BoardHudCanvasBindings>(
                    hudPrefab,
                    contents.transform,
                    "BoardHudCanvas");
                var map = InstantiateNested<BoardMapCanvasBindings>(
                    mapPrefab,
                    contents.transform,
                    "BoardMapCanvas");
                var item = InstantiateNested<BoardItemCanvasBindings>(
                    itemPrefab,
                    contents.transform,
                    "BoardItemCanvas");

                var bindings = contents.GetComponent<BoardCanvasBindings>() ??
                               contents.AddComponent<BoardCanvasBindings>();
                var view = contents.GetComponent<BoardFlowView>() ??
                           contents.AddComponent<BoardFlowView>();
                bindings.Configure(hud, map, item);
                view.ConfigureUiBindings(bindings);
                ConfigureItemChoiceOwners(item, view);

                if (PrefabUtility.SaveAsPrefabAsset(
                        contents,
                        AggregatePrefabPath) == null)
                {
                    throw new InvalidOperationException(
                        "Failed to save the non-visual BoardCanvas aggregate.");
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static void RepairSceneInstanceScales()
        {
            for (var pathIndex = 0;
                 pathIndex < BoardScenePaths.Length;
                 pathIndex++)
            {
                var path = BoardScenePaths[pathIndex];
                var scene = SceneManager.GetSceneByPath(path);
                var openedForMigration = !scene.IsValid() || !scene.isLoaded;
                if (openedForMigration)
                {
                    scene = EditorSceneManager.OpenScene(
                        path,
                        OpenSceneMode.Additive);
                }

                var changed = false;
                try
                {
                    var roots = scene.GetRootGameObjects();
                    for (var rootIndex = 0;
                         rootIndex < roots.Length;
                         rootIndex++)
                    {
                        var candidates = roots[rootIndex]
                            .GetComponentsInChildren<BoardCanvasBindings>(true);
                        for (var candidateIndex = 0;
                             candidateIndex < candidates.Length;
                             candidateIndex++)
                        {
                            var candidate = candidates[candidateIndex];
                            changed |= RepairScenePrefabScale(
                                candidate.transform,
                                AggregatePrefabPath);
                        }
                    }

                    if (path == BoardScenePaths[1])
                    {
                        changed |= RepairTestbedItemChoiceBindings(scene);
                    }

                    if (changed)
                    {
                        EditorSceneManager.MarkSceneDirty(scene);
                        if (openedForMigration)
                        {
                            EditorSceneManager.SaveScene(scene);
                        }
                    }
                }
                finally
                {
                    if (openedForMigration)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
        }

        private static bool RepairScenePrefabScale(
            Transform instance,
            string expectedPrefabPath)
        {
            if (instance == null ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    instance.gameObject) != expectedPrefabPath)
            {
                return false;
            }

            var source = PrefabUtility.GetCorrespondingObjectFromSource(
                instance);
            var needsRepair = instance.localScale != Vector3.one;
            var modifications = PrefabUtility.GetPropertyModifications(
                instance.gameObject);
            for (var index = 0;
                 !needsRepair && modifications != null &&
                 index < modifications.Length;
                 index++)
            {
                var modification = modifications[index];
                needsRepair = modification.target == source &&
                              modification.propertyPath.StartsWith(
                                  "m_LocalScale.",
                                  StringComparison.Ordinal) &&
                              modification.value != "1";
            }

            if (!needsRepair)
            {
                return false;
            }

            // A prefab root can retain a stale serialized zero-scale override
            // while its resolved Transform already reports Vector3.one after
            // the source prefab changes. Touch both values so Unity rewrites
            // the override deterministically.
            instance.localScale = Vector3.zero;
            instance.localScale = Vector3.one;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance);
            return true;
        }

        private static bool RepairTestbedItemChoiceBindings(Scene scene)
        {
            BoardFlowLocalSimulator simulator = null;
            BoardCanvasBindings bindings = null;
            var roots = scene.GetRootGameObjects();
            for (var rootIndex = 0;
                 rootIndex < roots.Length &&
                 (simulator == null || bindings == null);
                 rootIndex++)
            {
                simulator ??= roots[rootIndex]
                    .GetComponentInChildren<BoardFlowLocalSimulator>(true);
                bindings ??= roots[rootIndex]
                    .GetComponentInChildren<BoardCanvasBindings>(true);
            }

            if (simulator == null || bindings?.Item == null)
            {
                return false;
            }

            var changed = false;
            var choices = bindings.Item.ItemChoiceButtons;
            for (var index = 0; index < choices.Length; index++)
            {
                var hover = choices[index]
                    .GetComponent<BoardFlowLocalItemChoiceButton>();
                if (hover == null)
                {
                    hover = choices[index].gameObject
                        .AddComponent<BoardFlowLocalItemChoiceButton>();
                    changed = true;
                }
                hover.Configure(simulator, index);
                EditorUtility.SetDirty(hover);
            }

            var offers = bindings.Item.ShopOfferButtons;
            for (var index = 0; index < offers.Length; index++)
            {
                var hover = offers[index]
                    .GetComponent<BoardFlowLocalItemChoiceButton>();
                if (hover == null)
                {
                    hover = offers[index].gameObject
                        .AddComponent<BoardFlowLocalItemChoiceButton>();
                    changed = true;
                }
                hover.ConfigureShop(simulator, index);
                EditorUtility.SetDirty(hover);
            }

            return changed;
        }

        private static void ConfigureItemChoiceOwners(
            BoardItemCanvasBindings item,
            BoardFlowView view)
        {
            if (item == null || view == null)
            {
                throw new InvalidOperationException(
                    "Item choice ownership requires Item bindings and BoardFlowView.");
            }

            for (var index = 0; index < item.ItemChoiceHovers.Length; index++)
            {
                item.ItemChoiceHovers[index].Configure(view, index);
            }
            for (var index = 0; index < item.ShopOfferHovers.Length; index++)
            {
                item.ShopOfferHovers[index].ConfigureShop(view, index);
            }
        }

        private static T InstantiateNested<T>(
            GameObject prefab,
            Transform parent,
            string name)
            where T : Component
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab, parent)
                as GameObject;
            if (instance == null)
            {
                throw new InvalidOperationException(
                    "Could not instantiate nested UI prefab " + name + ".");
            }

            instance.name = name;

            var component = instance.GetComponent<T>();
            if (component == null)
            {
                throw new InvalidOperationException(
                    name + " is missing " + typeof(T).Name + ".");
            }

            return component;
        }

        private static void ValidateModule(
            GameObject root,
            CanvasKind kind,
            string path)
        {
            if (root == null || root.GetComponent<Canvas>() == null ||
                root.GetComponent<CanvasScaler>() == null ||
                root.GetComponent<GraphicRaycaster>() == null ||
                root.GetComponent<LocalizedFontScope>() == null)
            {
                throw new InvalidOperationException(
                    path + " has an incomplete authored Canvas root contract.");
            }

            if (root.GetComponentsInChildren<Canvas>(true).Length != 1)
            {
                throw new InvalidOperationException(
                    path + " must contain exactly one root Canvas.");
            }

            var valid = kind == CanvasKind.Hud
                ? root.GetComponent<BoardHudCanvasBindings>()
                    ?.HasRequiredReferences == true
                : kind == CanvasKind.Map
                    ? root.GetComponent<BoardMapCanvasBindings>()
                        ?.HasRequiredReferences == true
                    : root.GetComponent<BoardItemCanvasBindings>()
                        ?.HasRequiredReferences == true;
            if (!valid)
            {
                throw new InvalidOperationException(
                    path + " has incomplete serialized UI bindings.");
            }

            if (FindDescendant(root.transform, "Board Overview Map") != null ||
                FindDescendant(root.transform, "DiceText") != null)
            {
                throw new InvalidOperationException(
                    path + " still contains deleted legacy UI.");
            }
        }

        private static void ValidateNestedSource(
            GameObject instance,
            string expectedPath)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(instance);
            if (source == null ||
                AssetDatabase.GetAssetPath(source) != expectedPath ||
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance) !=
                expectedPath)
            {
                throw new InvalidOperationException(
                    instance.name + " must remain a connected nested instance of " +
                    expectedPath + ".");
            }
        }

        private static void PruneDirectChildren(
            Transform root,
            string[] namesToKeep)
        {
            for (var index = root.childCount - 1; index >= 0; index--)
            {
                var child = root.GetChild(index);
                var keep = false;
                for (var nameIndex = 0;
                     nameIndex < namesToKeep.Length;
                     nameIndex++)
                {
                    if (child.name == namesToKeep[nameIndex])
                    {
                        keep = true;
                        break;
                    }
                }

                if (!keep)
                {
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                }
            }
        }

        private static void RemoveNestedCanvases(string prefabPath)
        {
            var contents = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                if (!RemoveNestedCanvases(contents))
                {
                    return;
                }

                if (PrefabUtility.SaveAsPrefabAsset(contents, prefabPath) == null)
                {
                    throw new InvalidOperationException(
                        "Could not save the single-Canvas module contract: " +
                        prefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static bool RemoveNestedCanvases(GameObject root)
        {
            var changed = false;
            var canvases = root.GetComponentsInChildren<Canvas>(true);
            for (var index = canvases.Length - 1; index >= 0; index--)
            {
                var canvas = canvases[index];
                if (canvas.gameObject == root)
                {
                    continue;
                }

                var raycaster = canvas.GetComponent<GraphicRaycaster>();
                if (raycaster != null)
                {
                    UnityEngine.Object.DestroyImmediate(raycaster);
                }
                UnityEngine.Object.DestroyImmediate(canvas);
                changed = true;
            }

            return changed;
        }

        private static void RemoveComponent<T>(GameObject root)
            where T : Component
        {
            var component = root.GetComponent<T>();
            if (component != null)
            {
                UnityEngine.Object.DestroyImmediate(component);
            }
        }

        private static T Require<T>(GameObject root, string objectName)
            where T : Component
        {
            var target = FindDescendant(root.transform, objectName);
            var component = target != null ? target.GetComponent<T>() : null;
            if (component == null)
            {
                throw new InvalidOperationException(
                    root.name + " is missing authored " + objectName + " / " +
                    typeof(T).Name + ".");
            }

            return component;
        }

        private static GameObject RequireObject(GameObject root, string objectName)
        {
            var target = FindDescendant(root.transform, objectName);
            if (target == null)
            {
                throw new InvalidOperationException(
                    root.name + " is missing authored UI object " +
                    objectName + ".");
            }

            return target.gameObject;
        }

        private static Transform FindDescendant(Transform root, string name)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == name)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var match = FindDescendant(root.GetChild(index), name);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private enum CanvasKind
        {
            Hud,
            Map,
            Item
        }
    }
}
