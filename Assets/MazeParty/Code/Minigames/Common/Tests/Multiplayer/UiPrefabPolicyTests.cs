using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    /// <summary>
    /// Project-wide guardrails that keep player-visible Canvas UI editable as
    /// prefab assets instead of allowing procedural runtime UI to return.
    /// </summary>
    public sealed class UiPrefabPolicyTests
    {
        private const string OnlineBootstrapScenePath =
            "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity";
        private const string LobbyCanvasPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab";

        private static readonly string[] PlayerFacingUiPrefabSearchRoots =
        {
            "Assets/MazeParty/Prefabs/Multiplayer/UI",
            "Assets/MazeParty/Prefabs/Board/UI",
            "Assets/MazeParty/Prefabs/Minigames"
        };

        private static readonly string[] RequiredPrefabPaths =
        {
            "Assets/MazeParty/Prefabs/Board/UI/CeremonyStatisticsCanvas.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/OnlineLoadingCanvas.prefab",
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/UI/ArenaCombatHealthBar.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/MatchSettingsPanel.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/MatchMinigameRow.prefab",
            "Assets/MazeParty/Prefabs/Minigames/ShellGame/UI/ShellGameHud.prefab",
            "Assets/MazeParty/Prefabs/Minigames/OddOneOut/UI/OddOneOutHud.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigamePlayerMarkers.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/DevelopmentDiagnostics.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/BoardCameraCloudCanvas.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/BoardTileEffectWorld.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/BoardLocalPlayerMarker.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/BoardItemCanvas.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/AwardCeremonyCanvas.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/BoardHitFlash.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameResultCanvas.prefab",
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/UI/ArenaCombatHitFlash.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameTimerDial.prefab",
            "Assets/MazeParty/Prefabs/Minigames/WrongWay/UI/WrongWayHud.prefab",
            "Assets/MazeParty/Prefabs/Minigames/RedLightGreenLight/UI/RedLightGreenLightHud.prefab",
            "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/UI/SequenceMemoryHud.prefab",
            "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/MinigameScheduleTower.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/GameMenuCanvas.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab",
            "Assets/MazeParty/Prefabs/Multiplayer/UI/GameMenuCanvas.prefab"
        };

        private static readonly string[] RequiredBindingTypeNames =
        {
            "MazeParty.Multiplayer.CeremonyStatisticsBindings",
            "MazeParty.Multiplayer.OnlineLoadingView",
            "MazeParty.Multiplayer.ArenaCombatHealthBarView",
            "MazeParty.Multiplayer.MatchSettingsPanelBindings",
            "MazeParty.Multiplayer.MatchMinigameRowBindings",
            "MazeParty.Multiplayer.ShellGameHudBindings",
            "MazeParty.Multiplayer.OddOneOutHudBindings",
            "MazeParty.Multiplayer.MinigameLocalPlayerHighlight",
            "MazeParty.Multiplayer.DevelopmentDiagnosticsPanel",
            "MazeParty.Gameplay.BoardCameraCloudView",
            "MazeParty.Multiplayer.BoardTileEffectWorldView",
            "MazeParty.Multiplayer.BoardLocalPlayerMarkerView",
            "MazeParty.Multiplayer.BoardCanvasBindings",
            "MazeParty.Multiplayer.BoardHudCanvasBindings",
            "MazeParty.Multiplayer.BoardMapCanvasBindings",
            "MazeParty.Multiplayer.BoardItemCanvasBindings",
            "MazeParty.Multiplayer.AwardCeremonyCanvasBindings",
            "MazeParty.Multiplayer.ArenaCombatHitFlashView",
            "MazeParty.Multiplayer.MinigameCommonHudView",
            "MazeParty.Multiplayer.MinigameResultCanvasBindings",
            "MazeParty.Multiplayer.ArenaCombatHitFlashView",
            "MazeParty.Multiplayer.OnlineLobbyView",
            "MazeParty.Multiplayer.MinigameTimerDial",
            "MazeParty.Multiplayer.WrongWayHudBindings",
            "MazeParty.Multiplayer.RedLightGreenLightHudBindings",
            "MazeParty.Multiplayer.SequenceMemoryHudBindings",
            "MazeParty.Multiplayer.BouncingBallsHudBindings",
            "MazeParty.Multiplayer.MinigameScheduleTowerView",
            "MazeParty.Multiplayer.LobbyHatView",
            "MazeParty.Multiplayer.LobbyExpressionView",
            "MazeParty.Multiplayer.HandEmoteWheelView",
            "MazeParty.Multiplayer.HandEmoteWheelView",
            "MazeParty.Multiplayer.GameMenuBindings",
            "MazeParty.Multiplayer.SessionRecoveryView",
            "MazeParty.Multiplayer.ReconnectWaitView"
        };

        private static readonly string[] NestedBoardUiModulePaths =
        {
            "Assets/MazeParty/Prefabs/Board/UI/Modules/ReconnectOverlay.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/Modules/MinigameReadyPanel.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/Modules/BoardEventPopupPanel.prefab",
            "Assets/MazeParty/Prefabs/Board/UI/Modules/BoardKillFeedPanel.prefab"
        };

        private static readonly SceneUiContract[] SceneContracts =
        {
            new SceneUiContract("Assets/MazeParty/Scenes/Minigames/ShellGame/ShellGame.unity",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab",
                "Assets/MazeParty/Prefabs/Minigames/ShellGame/UI/ShellGameHud.prefab",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab"),
            new SceneUiContract("Assets/MazeParty/Scenes/Minigames/Flight/Flight.unity",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab"),
            new SceneUiContract("Assets/MazeParty/Scenes/Minigames/Race2/Race2.unity",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab"),
            new SceneUiContract("Assets/MazeParty/Scenes/Minigames/OddOneOut/OddOneOut.unity",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab",
                "Assets/MazeParty/Prefabs/Minigames/OddOneOut/UI/OddOneOutHud.prefab",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity",
                "Assets/MazeParty/Prefabs/Multiplayer/UI/OnlineLoadingCanvas.prefab",
                "Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab",
                "Assets/MazeParty/Prefabs/Board/UI/MinigameScheduleTower.prefab",
                "Assets/MazeParty/Prefabs/Multiplayer/UI/GameMenuCanvas.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Board/Board.unity",
                "Assets/MazeParty/Prefabs/Board/UI/CeremonyStatisticsCanvas.prefab",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigamePlayerMarkers.prefab",
                "Assets/MazeParty/Prefabs/Board/UI/BoardCameraCloudCanvas.prefab",
                "Assets/MazeParty/Prefabs/Board/UI/BoardLocalPlayerMarker.prefab",
                "Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab",
                "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab",
                "Assets/MazeParty/Prefabs/Board/UI/BoardItemCanvas.prefab",
                "Assets/MazeParty/Prefabs/Board/UI/AwardCeremonyCanvas.prefab",
                "Assets/MazeParty/Prefabs/Board/UI/BoardHitFlash.prefab",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameResultCanvas.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/Minefield/Minefield.unity"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/WrongWay/WrongWay.unity",
                "Assets/MazeParty/Prefabs/Minigames/WrongWay/UI/WrongWayHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/RedLightGreenLight/RedLightGreenLight.unity",
                "Assets/MazeParty/Prefabs/Minigames/RedLightGreenLight/UI/RedLightGreenLightHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/StableFooting/StableFooting.unity"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/BalloonBlow/BalloonBlow.unity"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/GiftGrab/GiftGrab.unity",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/TerritoryPaint/TerritoryPaint.unity",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/TagChase/TagChase.unity",
                new string[0]),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/Race/Race.unity",
                new string[0]),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/SequenceMemory/SequenceMemory.unity",
                "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/UI/SequenceMemoryHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/BouncingBalls/BouncingBalls.unity",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/ArenaCombat/ArenaCombat.unity",
                "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/UI/ArenaCombatHitFlash.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minigames/CliffBarrage/CliffBarrage.unity",
                new string[0]),
            new SceneUiContract("Assets/MazeParty/Scenes/Minigames/BombPassing/BombPassing.unity"),
            new SceneUiContract("Assets/MazeParty/Scenes/Minigames/SnowySpin/SnowySpin.unity"),
            new SceneUiContract("Assets/MazeParty/Scenes/Minigames/CoinCollect/CoinCollect.unity",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab")
        };

        private static readonly Regex[] ForbiddenRuntimeUiPatterns =
        {
            new Regex(@"\bOnGUI\s*\(", RegexOptions.Compiled),
            new Regex(@"\b(?:GUI|GUILayout)\s*\.", RegexOptions.Compiled),
            new Regex(
                @"AddComponent\s*<\s*(?:(?:UnityEngine(?:\.UI)?|TMPro)\s*\.\s*)?(?:RectTransform|Canvas|CanvasScaler|GraphicRaycaster|CanvasGroup|Text|Image|RawImage|Button|Toggle|Slider|Scrollbar|Dropdown|InputField|LayoutGroup|HorizontalLayoutGroup|VerticalLayoutGroup|GridLayoutGroup|ContentSizeFitter|AspectRatioFitter|Mask|RectMask2D|TextMeshProUGUI|TMP_Text|TMP_InputField|TMP_Dropdown)\s*>",
                RegexOptions.Compiled),
            new Regex(
                @"AddComponent\s*\(\s*typeof\s*\(\s*(?:(?:UnityEngine(?:\.UI)?|TMPro)\s*\.\s*)?(?:RectTransform|Canvas|CanvasScaler|GraphicRaycaster|CanvasGroup|Text|Image|RawImage|Button|Toggle|Slider|Scrollbar|Dropdown|InputField|LayoutGroup|HorizontalLayoutGroup|VerticalLayoutGroup|GridLayoutGroup|ContentSizeFitter|AspectRatioFitter|Mask|RectMask2D|TextMeshProUGUI|TMP_Text|TMP_InputField|TMP_Dropdown)\s*\)\s*\)",
                RegexOptions.Compiled),
            new Regex(
                @"new\s+GameObject\s*\([^;{}]*?\btypeof\s*\(\s*(?:(?:UnityEngine(?:\.UI)?|TMPro)\s*\.\s*)?(?:RectTransform|Canvas|CanvasScaler|GraphicRaycaster|CanvasGroup|Text|Image|RawImage|Button|Toggle|Slider|Scrollbar|Dropdown|InputField|LayoutGroup|HorizontalLayoutGroup|VerticalLayoutGroup|GridLayoutGroup|ContentSizeFitter|AspectRatioFitter|Mask|RectMask2D|TextMeshProUGUI|TMP_Text|TMP_InputField|TMP_Dropdown)\s*\)",
                RegexOptions.Compiled | RegexOptions.Singleline)
        };

        private static readonly Regex EditorWindowDeclarationPattern =
            new Regex(
                @"\bclass\s+\w+[^{};]*:\s*(?:UnityEditor\s*\.\s*)?EditorWindow\b",
                RegexOptions.Compiled);

        [Test]
        public void RegisteredUiAssets_AreCompletePrefabsWithValidBindings()
        {
            Assert.That(
                RequiredBindingTypeNames,
                Has.Length.EqualTo(RequiredPrefabPaths.Length));

            for (var index = 0;
                 index < RequiredPrefabPaths.Length;
                 index++)
            {
                var path = RequiredPrefabPaths[index];
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                Assert.That(
                    PrefabUtility.GetPrefabAssetType(prefab),
                    Is.EqualTo(PrefabAssetType.Regular).Or.EqualTo(
                        PrefabAssetType.Variant),
                    path);
                foreach (var transform in
                         prefab.GetComponentsInChildren<Transform>(true))
                {
                    Assert.That(
                        GameObjectUtility
                            .GetMonoBehavioursWithMissingScriptCount(
                                transform.gameObject),
                        Is.Zero,
                        path + " :: " + transform.name);
                }

                var bindingTypeName = RequiredBindingTypeNames[index];
                var binding = prefab
                    .GetComponentsInChildren<MonoBehaviour>(true)
                    .SingleOrDefault(component =>
                        component != null &&
                        component.GetType().FullName == bindingTypeName);
                Assert.That(binding, Is.Not.Null, path + " :: " + bindingTypeName);

                var requiredReferences = binding.GetType().GetProperty(
                    "HasRequiredReferences");
                Assert.That(
                    requiredReferences,
                    Is.Not.Null,
                    bindingTypeName + ".HasRequiredReferences");
                Assert.That(
                    requiredReferences.GetValue(binding),
                    Is.EqualTo(true),
                    path + " has an incomplete serialized binding contract.");
                if (binding is OnlineLobbyView lobby)
                    AssertLobbyEmoteHint(lobby, false);
            }
        }

        private static void AssertLobbyEmoteHint(OnlineLobbyView lobby, bool sceneInstance)
        {
            var actions = (GameObject)new SerializedObject(lobby)
                .FindProperty("sessionActionsRoot").objectReferenceValue;
            var hint = actions.transform.Find("Emote Hint");
            Assert.That(hint, Is.Not.Null, "Emote guidance must belong to the bound lobby-only container.");
            Assert.That(hint.GetComponentsInChildren<Selectable>(true), Is.Empty);
            Assert.That(hint.GetComponentsInChildren<Image>(true).All(image => image.sprite != null), Is.True,
                "Every emote-hint image must reference an imported sprite.");
            Assert.That(hint.GetComponentsInChildren<Graphic>(true).All(graphic => !graphic.raycastTarget), Is.True,
                "The informational emote hint must not intercept pointer input.");
            if (sceneInstance)
            {
                Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(hint), Is.Not.Null);
                Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(hint.gameObject),
                    Is.EqualTo(LobbyCanvasPrefabPath));
            }
        }

        [Test]
        public void WardrobeCatalog_HasSavedPreviewsAndValidFixedEmoteFaces()
        {
            var catalog = MazeParty.Gameplay.PlayerExpressionCatalog.Instance;
            Assert.That(catalog, Is.Not.Null);
            Assert.That(catalog.NoHatPreview, Is.Not.Null);
            foreach (var hat in catalog.Hats)
            {
                Assert.That(hat.Prefab, Is.Not.Null);
                Assert.That(hat.Preview, Is.Not.Null, hat.Name);
                Assert.That(AssetDatabase.GetAssetPath(hat.Preview), Does.StartWith("Assets/MazeParty/Art/Player/HatPreviews/"));
            }
            Assert.That(catalog.Gestures.Length, Is.EqualTo(MazeParty.Gameplay.HandEmoteRules.GestureCount));
            foreach (var gesture in catalog.Gestures)
            {
                Assert.That(gesture.ExpressionId, Is.LessThan(catalog.Faces.Length), gesture.Name);
                Assert.That(catalog.Faces[gesture.ExpressionId].Sprite, Is.Not.Null, gesture.Name);
            }
        }

        [Test]
        public void HitFlashPrefabs_DoNotBlockPointerInput()
        {
            foreach (var path in new[]
                     {
                         "Assets/MazeParty/Prefabs/Board/UI/BoardHitFlash.prefab",
                         "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/UI/ArenaCombatHitFlash.prefab"
                     })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                Assert.That(prefab.GetComponent<Canvas>(), Is.Not.Null, path);
                Assert.That(
                    prefab.GetComponentsInChildren<GraphicRaycaster>(true),
                    Is.Empty,
                    path);
                var graphics = prefab.GetComponentsInChildren<Graphic>(true);
                Assert.That(graphics, Is.Not.Empty, path);
                Assert.That(
                    graphics.All(graphic => !graphic.raycastTarget),
                    Is.True,
                    path);
                Assert.That(
                    prefab.GetComponentsInChildren<CanvasGroup>(true)
                        .All(group => !group.blocksRaycasts),
                    Is.True,
                    path);
            }
        }

        [Test]
        public void PlayerFacingCanvasText_UsesNormalStyleExceptAuthoredEmphasis()
        {
            var prefabPaths = AssetDatabase.FindAssets(
                    "t:Prefab",
                    PlayerFacingUiPrefabSearchRoots)
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(IsPlayerFacingUiPrefabPath)
                .Distinct()
                .OrderBy(path => path)
                .ToArray();
            Assert.That(prefabPaths, Is.Not.Empty);

            var violations = new List<string>();
            foreach (var path in prefabPaths)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);

                foreach (var text in
                         prefab.GetComponentsInChildren<Text>(true))
                {
                    var marker = text.GetComponentInParent<BoardLocalPlayerMarkerView>(true);
                    if (marker != null && marker.IsFixedLabel(text))
                    {
                        Assert.That(text.text, marker.Label == text
                            ? Is.EqualTo("ME") : Is.EqualTo("P1").Or.EqualTo("P2").Or.EqualTo("P3").Or.EqualTo("P4"));
                        Assert.That(text.fontStyle, Is.EqualTo(FontStyle.Bold));
                        Assert.That(text.color, Is.EqualTo(Color.black));
                        Assert.That(text.GetComponent<Outline>().effectColor, Is.EqualTo(Color.white));
                        Assert.That(text.GetComponent<LocalizedText>(), Is.Null,
                            "ME and P1-P4 are intentionally identical in every language.");
                        continue;
                    }
                    if (text.name == "MovesText" &&
                        (path == "Assets/MazeParty/Prefabs/Board/UI/BoardCanvas.prefab" ||
                         path == "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab"))
                    {
                        Assert.That(text.fontStyle, Is.EqualTo(FontStyle.Bold),
                            path + " :: MovesText is an intentionally bold number.");
                        continue;
                    }

                    var hud = text.GetComponentInParent<BoardHudCanvasBindings>(true);
                    if (hud != null &&
                        (hud.TurnText == text || hud.PhaseTimerText == text))
                    {
                        Assert.That(text.fontStyle, Is.EqualTo(FontStyle.Bold),
                            path + " :: Board turn and time are intentionally bold.");
                        continue;
                    }

                    if (text.fontStyle != FontStyle.Normal)
                    {
                        violations.Add(
                            FormatFontViolation(
                                path,
                                prefab.transform,
                                text.transform,
                                "Text.fontStyle=" + text.fontStyle));
                    }
                }

                foreach (var text in
                         prefab.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (!(text is TextMeshProUGUI))
                    {
                        continue;
                    }

                    if (text.fontStyle != FontStyles.Normal ||
                        text.fontWeight != FontWeight.Regular)
                    {
                        violations.Add(
                            FormatFontViolation(
                                path,
                                prefab.transform,
                                text.transform,
                                "TMP fontStyle=" + text.fontStyle +
                                ", fontWeight=" + text.fontWeight));
                    }
                }
            }

            Assert.That(
                violations,
                Is.Empty,
                "Player-facing Canvas text must use Normal/Regular style except explicitly authored emphasis:\n" +
                string.Join("\n", violations));
        }

        [TestCase("Assets/MazeParty/Prefabs/Multiplayer/UI/LobbyCanvas.prefab")]
        [TestCase("Assets/MazeParty/Prefabs/Board/UI/BoardHudCanvas.prefab")]
        public void HandEmoteWheel_StaticInstructionsRemainLocalized(
            string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, prefabPath);
            var panel = prefab.GetComponentsInChildren<Transform>(true)
                .Single(transform => transform.name == "Hand Emote Wheel");
            foreach (var expected in new[]
                     {
                         new
                         {
                             Name = "Title",
                             Source = "HAND EMOTES"
                         },
                         new
                         {
                             Name = "Help",
                             Source = "HOLD T + DRAG  /  RELEASE TO USE\n" +
                                      "CENTER / ESC / RMB: CANCEL"
                         }
                     })
            {
                var label = panel.Find(expected.Name);
                Assert.That(label, Is.Not.Null,
                    prefabPath + " :: Hand Emote Wheel/" + expected.Name);
                var text = label.GetComponent<Text>();
                var localized = label.GetComponent<LocalizedText>();
                Assert.That(text, Is.Not.Null);
                Assert.That(localized, Is.Not.Null,
                    expected.Name + " must retain its LocalizedText binding.");
                Assert.That(text.text, Is.EqualTo(expected.Source));
                Assert.That(localized.SourceText, Is.EqualTo(expected.Source));
            }
        }

        [Test]
        public void SceneOverridePolicy_AllowsBoundControlOutputButRejectsAuthoredDesignChanges()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/MazeParty/Prefabs/Multiplayer/UI/GameMenuCanvas.prefab"), scene);
                var bindings = root.GetComponent<GameMenuBindings>();
                var slider = bindings.MasterSlider;
                var scroll = bindings.LanguageDropdown.template.GetComponent<ScrollRect>();
                var scrollbar = scroll.verticalScrollbar;
                var handle = slider.handleRect;
                var fill = slider.fillRect;

                void Check(Component target, string propertyPath, bool allowed)
                {
                    var serialized = new SerializedObject(target);
                    var property = serialized.FindProperty(propertyPath);
                    var original = property.floatValue;
                    property.floatValue = original + .12345f;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    try
                    {
                        var modification = new PropertyModification
                        {
                            target = PrefabUtility.GetCorrespondingObjectFromSource(target),
                            propertyPath = propertyPath,
                            value = property.floatValue.ToString("R", CultureInfo.InvariantCulture)
                        };
                        Assert.That(IsAllowedSceneOverride(root, modification), Is.EqualTo(allowed),
                            target.name + " :: " + propertyPath + " :: active=" + target.gameObject.activeInHierarchy);
                    }
                    finally
                    {
                        property.floatValue = original;
                        serialized.ApplyModifiedPropertiesWithoutUndo();
                    }
                }

                foreach (var open in new[] { false, true })
                {
                    bindings.MenuRoot.SetActive(open);
                    bindings.LanguageDropdown.template.gameObject.SetActive(open);
                    Canvas.ForceUpdateCanvases();
                    Check(handle, "m_AnchorMax.x", true);
                    Check(fill, "m_AnchorMax.y", true);
                    Check(scrollbar.handleRect, "m_AnchorMin.y", true);
                    Check(scroll.viewport, "m_SizeDelta.x", true);
                    Check(scroll.viewport, "m_AnchorMax.y", true);
                    Check(handle, "m_SizeDelta.x", false);
                    Check(fill, "m_Pivot.x", false);
                    Check(scrollbar.handleRect, "m_LocalScale.x", false);
                    Check(scroll.viewport, "m_Pivot.y", false);
                    Check(handle.GetComponent<Image>(), "m_Color.r", false);
                }

                bindings.MenuRoot.SetActive(false);
                slider.enabled = false;
                Check(handle, "m_AnchorMax.x", false);
                slider.enabled = true;
                slider.handleRect = null;
                Check(handle, "m_AnchorMax.x", false);
                scrollbar.handleRect = null;
                Check(scrollbar.transform.Find("Sliding Area/Handle"), "m_AnchorMin.y", false);
                scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.Permanent;
                Check(scroll.viewport, "m_SizeDelta.x", false);
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void TmpRenderChildren_RequireAnAuthoredOwnerAndTemporaryRendererIdentity()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = (GameObject)PrefabUtility.InstantiatePrefab(
                    AssetDatabase.LoadAssetAtPath<GameObject>(
                        "Assets/MazeParty/Prefabs/Board/UI/Modules/MinigameReadyPanel.prefab"), scene);
                var label = root.GetComponentsInChildren<TextMeshProUGUI>(true)
                    .First(text => text.font != null && text.fontSharedMaterial != null);
                var subMesh = TMP_SubMeshUI.AddSubTextObject(label,
                    new MaterialReference(1, label.font, null, label.fontSharedMaterial, 1f));
                foreach (var component in new Component[]
                         { subMesh, subMesh.rectTransform, subMesh.GetComponent<LayoutElement>() })
                    Assert.That(IsVisualCanvasUiComponent(component), Is.False,
                        "TMP render internals must not be treated as separately authored UI.");

                var flags = subMesh.gameObject.hideFlags;
                subMesh.gameObject.hideFlags = HideFlags.None;
                Assert.That(IsVisualCanvasUiComponent(subMesh.rectTransform), Is.True,
                    "A saved child still needs a prefab source.");
                subMesh.gameObject.hideFlags = flags;
                subMesh.transform.SetParent(root.transform, false);
                Assert.That(IsVisualCanvasUiComponent(subMesh.rectTransform), Is.True,
                    "An unowned renderer is not a TMP-generated child of authored text.");

                var unboundUi = new GameObject("Unbound UI", typeof(RectTransform), typeof(Image));
                unboundUi.transform.SetParent(label.transform, false);
                unboundUi.hideFlags = HideFlags.DontSave;
                Assert.That(IsVisualCanvasUiComponent(unboundUi.transform), Is.True);
                Assert.That(IsVisualCanvasUiComponent(unboundUi.GetComponent<Image>()), Is.True,
                    "DontSave alone must not exempt procedural UI.");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void GeneratedScenes_UseOnlyRegisteredCanvasPrefabInstances()
        {
            foreach (var contract in SceneContracts)
            {
                Assert.That(
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(contract.Path),
                    Is.Not.Null,
                    contract.Path);

                var scene = SceneManager.GetSceneByPath(contract.Path);
                var openedForTest = !scene.IsValid() || !scene.isLoaded;
                if (openedForTest)
                {
                    scene = EditorSceneManager.OpenScene(
                        contract.Path,
                        OpenSceneMode.Additive);
                }

                try
                {
                    var canvases = scene.GetRootGameObjects()
                        .SelectMany(root =>
                            root.GetComponentsInChildren<Canvas>(true))
                        .ToArray();
                    Assert.That(
                        canvases.Length,
                        contract.AllowedPrefabPaths.Count == 0
                            ? Is.Zero : Is.GreaterThan(0),
                        contract.Path + " has unexpected Canvas count.");

                    var actualPaths = canvases
                        .Select(canvas =>
                            PrefabUtility
                                .GetPrefabAssetPathOfNearestInstanceRoot(
                                    canvas.gameObject))
                        .ToArray();
                    Assert.That(
                        actualPaths.All(path =>
                            contract.AllowedPrefabPaths.Contains(path)),
                        Is.True,
                        contract.Path + " contains a non-prefab or " +
                        "unexpected Canvas: " +
                        string.Join(", ", actualPaths));
                    foreach (var expectedPath in contract.AllowedPrefabPaths)
                    {
                        Assert.That(
                            actualPaths,
                            Does.Contain(expectedPath),
                            contract.Path);
                    }

                    if (contract.Path == OnlineBootstrapScenePath)
                    {
                        var lobby = scene.GetRootGameObjects()
                            .SelectMany(root => root.GetComponentsInChildren<OnlineLobbyView>(true)).Single();
                        Assert.That(lobby.HasRequiredReferences, Is.True);
                        AssertLobbyEmoteHint(lobby, true);
                        var steamInvite = (Button)new SerializedObject(lobby).FindProperty("steamInviteButton").objectReferenceValue;
                        Assert.That(steamInvite, Is.Not.Null);
                        Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(steamInvite), Is.Not.Null,
                            "Steam invitations must use the button authored in the lobby prefab.");
                        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(steamInvite.gameObject),
                            Is.EqualTo(LobbyCanvasPrefabPath));
                        var loading = scene.GetRootGameObjects()
                            .SelectMany(root => root.GetComponentsInChildren<OnlineLoadingView>(true)).Single();
                        var controller = scene.GetRootGameObjects()
                            .SelectMany(root => root.GetComponentsInChildren<OnlineSessionController>(true)).Single();
                        Assert.That(loading.HasRequiredReferences, Is.True);
                        Assert.That(new SerializedObject(controller).FindProperty("loadingView").objectReferenceValue,
                            Is.SameAs(loading), "The persistent controller must own the authored loading view.");
                        var settings = scene.GetRootGameObjects()
                            .SelectMany(root => root.GetComponentsInChildren<MatchSettingsPanelBindings>(true)).Single();
                        Assert.That(settings.HasRequiredReferences, Is.True);
                        Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(settings.gameObject),
                            Is.EqualTo("Assets/MazeParty/Prefabs/Multiplayer/UI/MatchSettingsPanel.prefab"));
                        var settingsRows = settings.GetComponentsInChildren<MatchMinigameRowBindings>(true);
                        Assert.That(settingsRows.Select(row => (int)row.Game).OrderBy(id => id),
                            Is.EqualTo(Enumerable.Range(1, 20)));
                        foreach (var row in settingsRows)
                        {
                            Assert.That(row.HasRequiredReferences, Is.True);
                            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(row.gameObject),
                                Is.EqualTo("Assets/MazeParty/Prefabs/Multiplayer/UI/MatchMinigameRow.prefab"));
                        }
                        var tower = scene.GetRootGameObjects()
                            .SelectMany(root => root.GetComponentsInChildren<MinigameScheduleTowerView>(true)).Single();
                        Assert.That(tower.HasRequiredReferences, Is.True);
                        var scroll = tower.GetComponentInChildren<ScrollRect>(true);
                        Assert.That(scroll.vertical && !scroll.horizontal, Is.True);
                        Assert.That(scroll.verticalScrollbar, Is.Null);
                        Assert.That(scroll.horizontalScrollbar, Is.Null);
                    }

                    var hitFlashPath = contract.Path switch
                    {
                        "Assets/MazeParty/Scenes/Board/Board.unity" =>
                            "Assets/MazeParty/Prefabs/Board/UI/BoardHitFlash.prefab",
                        "Assets/MazeParty/Scenes/Minigames/ArenaCombat/ArenaCombat.unity" =>
                            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/UI/ArenaCombatHitFlash.prefab",
                        _ => null
                    };
                    if (hitFlashPath != null)
                    {
                        Assert.That(
                            scene.GetRootGameObjects().Count(root =>
                                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) ==
                                hitFlashPath),
                            Is.EqualTo(1),
                            contract.Path + " must have one standalone hit flash.");
                    }

                    foreach (var component in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<MonoBehaviour>(true)))
                    {
                        if (component is MazeParty.Gameplay.GameplayCameraDirector director)
                        {
                            Assert.That(director.CloudTransition, Is.Not.Null);
                            Assert.That(director.CloudTransition.HasRequiredReferences, Is.True);
                            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                                director.CloudTransition.gameObject), Is.EqualTo(
                                "Assets/MazeParty/Prefabs/Board/UI/BoardCameraCloudCanvas.prefab"));
                        }
                        if (component is GiftGrabNetworkView giftView)
                        {
                            Assert.That(giftView.GiftHud, Is.Not.Null);
                            Assert.That(giftView.GiftHud.HasRequiredReferences, Is.True);
                            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                                giftView.GiftHud.gameObject), Is.EqualTo(
                                "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab"));
                        }
                        if (component is BoardFlowCameraPresenter presenter)
                        {
                            Assert.That(presenter.LocalPlayerMarker, Is.Not.Null);
                            Assert.That(presenter.LocalPlayerMarker.HasRequiredReferences, Is.True);
                            Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                                presenter.LocalPlayerMarker.gameObject), Is.EqualTo(
                                "Assets/MazeParty/Prefabs/Board/UI/BoardLocalPlayerMarker.prefab"));
                        }
                        if (component is HandEmoteWheelView wheel)
                        { Assert.That(wheel.HasRequiredReferences, Is.True); Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(wheel), Is.Not.Null); }
                        if (component is LobbyExpressionView faces)
                        { Assert.That(faces.HasRequiredReferences, Is.True); Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(faces), Is.Not.Null); }
                        if (component is LobbyHatView hats)
                        { Assert.That(hats.HasRequiredReferences, Is.True); Assert.That(PrefabUtility.GetCorrespondingObjectFromSource(hats), Is.Not.Null); }
                    }

                    var visualUiComponents = scene.GetRootGameObjects()
                        .SelectMany(root =>
                            root.GetComponentsInChildren<Component>(true))
                        .Where(IsVisualCanvasUiComponent)
                        .ToArray();
                    var visualPrefabRoots = new HashSet<GameObject>();
                    foreach (var component in visualUiComponents)
                    {
                        var source = PrefabUtility
                            .GetCorrespondingObjectFromSource(component);
                        Assert.That(
                            source,
                            Is.Not.Null,
                            contract.Path + " :: " + component.name +
                            " (" + component.GetType().Name +
                            ") is a scene-local UI override.");

                        var instanceRoot = PrefabUtility
                            .GetNearestPrefabInstanceRoot(
                                component.gameObject);
                        Assert.That(
                            instanceRoot,
                            Is.Not.Null,
                            contract.Path + " :: " + component.name);
                        visualPrefabRoots.Add(
                            PrefabUtility
                                .GetOutermostPrefabInstanceRoot(
                                    component.gameObject) ??
                            instanceRoot);
                        var visualSourcePath = PrefabUtility
                            .GetPrefabAssetPathOfNearestInstanceRoot(
                                component.gameObject);
                        Assert.That(
                            RequiredPrefabPaths.Contains(visualSourcePath) ||
                            NestedBoardUiModulePaths.Contains(visualSourcePath),
                            Is.True,
                            contract.Path + " :: " + component.name);
                    }


                    var visualOverrideViolations = new List<string>();
                    foreach (var instanceRoot in visualPrefabRoots)
                    {
                        var modifications = PrefabUtility
                            .GetPropertyModifications(instanceRoot);
                        if (modifications == null)
                        {
                            continue;
                        }

                        foreach (var modification in modifications)
                        {
                            if (IsAllowedSceneOverride(
                                    instanceRoot,
                                    modification))
                            {
                                continue;
                            }

                            var target = modification.target;
                            visualOverrideViolations.Add(
                                instanceRoot.name + " :: " +
                                (target != null
                                    ? target.name + " (" +
                                      target.GetType().Name + ")"
                                    : "<missing target>") +
                                " :: " + modification.propertyPath);
                        }
                    }

                    Assert.That(
                        visualOverrideViolations,
                        Is.Empty,
                        contract.Path +
                        " contains prefab design overrides. Change the UI " +
                        "prefab instead of its scene instance.\n" +
                        string.Join("\n", visualOverrideViolations));
                }
                finally
                {
                    if (openedForTest)
                    {
                        EditorSceneManager.CloseScene(scene, true);
                    }
                }
            }
        }

        [Test]
        public void ActiveMinigameHuds_UseOnlyOneSharedTimerDial()
        {
            const string timerPath =
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameTimerDial.prefab";
            const string commonPath =
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/MinigameCommonHud.prefab";
            var hudPaths = new[]
            {
                "Assets/MazeParty/Prefabs/Minigames/WrongWay/UI/WrongWayHud.prefab",
                "Assets/MazeParty/Prefabs/Minigames/RedLightGreenLight/UI/RedLightGreenLightHud.prefab",
                "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/UI/SequenceMemoryHud.prefab",
                "Assets/MazeParty/Prefabs/Minigames/Common/UI/BouncingBallsHud.prefab"
            };

            var common = AssetDatabase.LoadAssetAtPath<GameObject>(commonPath);
            Assert.That(common, Is.Not.Null, commonPath);
            var timer = common.GetComponentInChildren<MinigameTimerDial>(true);
            Assert.That(timer, Is.Not.Null, commonPath);
            Assert.That(timer.HasRequiredReferences, Is.True, commonPath);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    timer.gameObject),
                Is.EqualTo(timerPath),
                commonPath);

            foreach (var hudPath in hudPaths)
            {
                var prefab =
                    AssetDatabase.LoadAssetAtPath<GameObject>(hudPath);
                Assert.That(prefab, Is.Not.Null, hudPath);
                Assert.That(
                    prefab.GetComponentsInChildren<MinigameTimerDial>(true),
                    Is.Empty,
                    hudPath);
            }
        }

        [Test]
        public void RuntimeSources_DoNotProcedurallyConstructCanvasUi()
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var scanRoots = new[]
            {
                "Assets/MazeParty/Code/Multiplayer",
                "Assets/MazeParty/Code/Board",
                "Assets/MazeParty/Code/Minigames",
                "Assets/MazeParty/Runtime",
                "Assets/MazeParty/Gameplay/Runtime"
            };
            var violations = new List<string>();
            var scannedFileCount = 0;

            foreach (var relativeRoot in scanRoots)
            {
                var absoluteRoot = Path.Combine(projectRoot, relativeRoot);
                if (!Directory.Exists(absoluteRoot))
                {
                    continue;
                }
                foreach (var file in Directory.GetFiles(
                             absoluteRoot,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    var normalizedPath = file.Replace('\\', '/');
                    if (normalizedPath.Contains("/Editor/") ||
                        normalizedPath.Contains("/Tests/"))
                    {
                        continue;
                    }
                    var source = MaskCommentsAndLiterals(
                        File.ReadAllText(file));
                    if (EditorWindowDeclarationPattern.IsMatch(source))
                    {
                        continue;
                    }
                    scannedFileCount++;

                    foreach (var pattern in ForbiddenRuntimeUiPatterns)
                    {
                        var match = pattern.Match(source);
                        if (match.Success)
                        {
                            var line = 1;
                            for (var index = 0;
                                 index < match.Index;
                                 index++)
                            {
                                if (source[index] == '\n')
                                {
                                    line++;
                                }
                            }

                            violations.Add(
                                file.Substring(projectRoot.Length + 1) +
                                ":" + line + " matches " + pattern);
                        }
                    }
                }
            }

            Assert.That(
                scannedFileCount,
                Is.GreaterThan(0),
                "No runtime source files were found in the old or " +
                "reorganized Code folders.");
            Assert.That(
                violations,
                Is.Empty,
                "Player-visible runtime UI must come from prefabs.\n" +
                string.Join("\n", violations));
        }

        private static bool IsAllowedSceneOverride(
            GameObject instanceRoot,
            PropertyModification modification)
        {
            if (modification == null || modification.target == null)
            {
                return false;
            }

            var sourceRoot = PrefabUtility
                .GetCorrespondingObjectFromSource(instanceRoot);
            if (sourceRoot == null)
            {
                return false;
            }

            if (modification.target == sourceRoot)
            {
                if (modification.propertyPath == "m_Name")
                {
                    return true;
                }

                return modification.propertyPath == "m_IsActive" &&
                       instanceRoot.scene.path ==
                       OnlineBootstrapScenePath &&
                       PrefabUtility
                           .GetPrefabAssetPathOfNearestInstanceRoot(
                               instanceRoot) == LobbyCanvasPrefabPath;
            }

            if (modification.target == sourceRoot.transform)
            {
                if (IsAllowedInstanceRootTransformOverride(
                        sourceRoot.transform,
                        modification.propertyPath))
                {
                    return true;
                }

                // Unity serializes every root RectTransform field as a prefab
                // instance modification even for a freshly-instantiated Canvas.
                // Its raw modification value can also be a canonical zero while
                // the resolved scene property still inherits the prefab value.
                // Compare the resolved objects so real design divergence fails.
                return sourceRoot.transform is RectTransform &&
                       MatchesPrefabSourceValue(
                           modification,
                           instanceRoot.transform);
            }

            if (modification.target is Canvas &&
                modification.objectReference != null &&
                modification.propertyPath == "m_Camera")
            {
                // A world-space Canvas may need the scene's output Camera.
                return true;
            }

            if (modification.target is RectTransform sourceRectTransform)
            {
                var instanceRectTransform = instanceRoot
                    .GetComponentsInChildren<RectTransform>(true)
                    .FirstOrDefault(candidate =>
                        PrefabUtility.GetCorrespondingObjectFromSource(
                            candidate) == sourceRectTransform);
                if (instanceRectTransform == null)
                {
                    return false;
                }

                // LayoutGroup and ContentSizeFitter write transient prefab
                // modifications while their scene is active in the Editor.
                // Their authored settings remain on the prefab, so these
                // driven RectTransform values are not scene design overrides.
                return IsLayoutControlled(instanceRectTransform) ||
                       IsControlDrivenRectProperty(
                           instanceRectTransform, modification.propertyPath) ||
                       MatchesPrefabSourceValue(
                           modification,
                           instanceRectTransform);
            }

            if (modification.target is Component component)
            {
                if (IsVisualCanvasUiComponent(component))
                {
                    return false;
                }

                // Scene-only behavior components may live on a prefab instance
                // (for example the board testbed simulator). A component that
                // belongs to the prefab may override only an object reference
                // to gameplay state outside that UI instance. This keeps Color,
                // copy, layout and internal binding changes prefab-authored.
                if (!EditorUtility.IsPersistent(component))
                {
                    return true;
                }

                return IsExternalSceneObjectReference(
                    instanceRoot,
                    modification);
            }

            return false;
        }

        private static bool IsLayoutControlled(RectTransform transform)
        {
            // Selectable controls and ScrollRect drive only specific fields.
            // Their other RectTransform fields remain authored design.
            return (transform.drivenByObject is ILayoutController &&
                    !(transform.drivenByObject is ScrollRect)) ||
                   transform.GetComponent<ContentSizeFitter>() != null ||
                   (transform.parent != null &&
                    transform.parent.GetComponent<LayoutGroup>() != null);
        }

        private static bool IsControlDrivenRectProperty(
            RectTransform transform, string propertyPath)
        {
            var anchors = propertyPath == "m_AnchorMin.x" ||
                          propertyPath == "m_AnchorMin.y" ||
                          propertyPath == "m_AnchorMax.x" ||
                          propertyPath == "m_AnchorMax.y";
            if (anchors)
            {
                // Hidden controls clear their DrivenRectTransformTracker. Use
                // the actual serialized bindings, never object names, to retain
                // the same policy while a menu or dropdown is closed.
                var slider = transform.GetComponentInParent<Slider>(true);
                if (slider != null && slider.enabled &&
                    (slider.handleRect == transform || slider.fillRect == transform))
                    return true;
                var scrollbar = transform.GetComponentInParent<Scrollbar>(true);
                if (scrollbar != null && scrollbar.enabled &&
                    scrollbar.handleRect == transform)
                    return true;
            }

            var scroll = transform.GetComponentInParent<ScrollRect>(true);
            if (scroll == null || !scroll.enabled || scroll.viewport != transform ||
                transform.parent != scroll.transform)
                return false;
            var horizontal = scroll.horizontalScrollbar;
            var vertical = scroll.verticalScrollbar;
            // Match uGUI's AutoHideAndExpandViewport ownership conditions.
            if ((horizontal != null && horizontal.transform.parent != scroll.transform) ||
                (vertical != null && vertical.transform.parent != scroll.transform))
                return false;
            var expands = (horizontal != null && scroll.horizontalScrollbarVisibility ==
                           ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport) ||
                          (vertical != null && scroll.verticalScrollbarVisibility ==
                           ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport);
            return expands && (anchors ||
                propertyPath == "m_SizeDelta.x" || propertyPath == "m_SizeDelta.y" ||
                propertyPath == "m_AnchoredPosition.x" || propertyPath == "m_AnchoredPosition.y");
        }

        private static bool IsExternalSceneObjectReference(
            GameObject instanceRoot,
            PropertyModification modification)
        {
            var serializedTarget = new SerializedObject(modification.target);
            var property = serializedTarget.FindProperty(
                modification.propertyPath);
            if (property == null ||
                property.propertyType !=
                    SerializedPropertyType.ObjectReference ||
                modification.objectReference == null ||
                EditorUtility.IsPersistent(modification.objectReference))
            {
                return false;
            }

            GameObject referencedObject = null;
            if (modification.objectReference is GameObject gameObject)
            {
                referencedObject = gameObject;
            }
            else if (modification.objectReference is Component component)
            {
                referencedObject = component.gameObject;
            }

            return referencedObject != null &&
                   referencedObject.scene.IsValid() &&
                   !referencedObject.transform.IsChildOf(
                       instanceRoot.transform);
        }

        private static bool MatchesPrefabSourceValue(
            PropertyModification modification,
            UnityEngine.Object instanceTarget)
        {
            var serializedSource = new SerializedObject(modification.target);
            var serializedInstance = new SerializedObject(instanceTarget);
            var sourceProperty = serializedSource.FindProperty(
                modification.propertyPath);
            var instanceProperty = serializedInstance.FindProperty(
                modification.propertyPath);
            if (sourceProperty == null ||
                instanceProperty == null ||
                sourceProperty.propertyType != instanceProperty.propertyType)
            {
                return false;
            }

            // Screen-space Canvas drives some resolved RectTransform values
            // (for example size and pivot), while Unity writes their inherited
            // prefab values into the raw PropertyModification. Root scale has
            // the inverse quirk in current Unity: its raw value is zero while
            // the resolved value correctly inherits one. Either representation
            // is sufficient only when it exactly matches the prefab source.
            if (MatchesRecordedValue(sourceProperty, modification))
            {
                return true;
            }

            switch (sourceProperty.propertyType)
            {
                case SerializedPropertyType.Float:
                    return Math.Abs(
                               sourceProperty.doubleValue -
                               instanceProperty.doubleValue) <=
                           0.000001d;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    return sourceProperty.longValue ==
                           instanceProperty.longValue;
                case SerializedPropertyType.Boolean:
                    return sourceProperty.boolValue ==
                           instanceProperty.boolValue;
                case SerializedPropertyType.String:
                    return sourceProperty.stringValue ==
                           instanceProperty.stringValue;
                case SerializedPropertyType.ObjectReference:
                    return sourceProperty.objectReferenceValue ==
                           instanceProperty.objectReferenceValue;
                default:
                    return false;
            }
        }

        private static bool MatchesRecordedValue(
            SerializedProperty sourceProperty,
            PropertyModification modification)
        {
            switch (sourceProperty.propertyType)
            {
                case SerializedPropertyType.Float:
                    return double.TryParse(
                               modification.value,
                               NumberStyles.Float,
                               CultureInfo.InvariantCulture,
                               out var floatValue) &&
                           Math.Abs(
                               sourceProperty.doubleValue - floatValue) <=
                           0.000001d;
                case SerializedPropertyType.Integer:
                case SerializedPropertyType.Enum:
                case SerializedPropertyType.LayerMask:
                case SerializedPropertyType.Character:
                    return long.TryParse(
                               modification.value,
                               NumberStyles.Integer,
                               CultureInfo.InvariantCulture,
                               out var integerValue) &&
                           sourceProperty.longValue == integerValue;
                case SerializedPropertyType.Boolean:
                    return modification.value ==
                           (sourceProperty.boolValue ? "1" : "0");
                case SerializedPropertyType.String:
                    return sourceProperty.stringValue == modification.value;
                case SerializedPropertyType.ObjectReference:
                    return sourceProperty.objectReferenceValue ==
                           modification.objectReference;
                default:
                    return false;
            }
        }

        private static bool IsAllowedInstanceRootTransformOverride(
            Transform sourceRootTransform,
            string propertyPath)
        {
            // The scene owns only the prefab instance's parent and sibling
            // order. A RectTransform's anchors, size, pivot, position, and
            // scale remain prefab-authored visual design.
            if (propertyPath == "m_Father" ||
                propertyPath == "m_RootOrder")
            {
                return true;
            }

            if (sourceRootTransform is RectTransform)
            {
                return false;
            }

            // A non-UI Transform root may be positioned as a whole in its
            // scene without changing the visual layout of Canvas children.
            return propertyPath.StartsWith(
                       "m_LocalPosition.",
                       StringComparison.Ordinal) ||
                   propertyPath.StartsWith(
                       "m_LocalRotation.",
                       StringComparison.Ordinal) ||
                   propertyPath.StartsWith(
                       "m_LocalScale.",
                       StringComparison.Ordinal) ||
                   propertyPath.StartsWith(
                       "m_LocalEulerAnglesHint.",
                       StringComparison.Ordinal);
        }

        private static bool IsPlayerFacingUiPrefabPath(string path)
        {
            return path.StartsWith(
                       "Assets/MazeParty/Prefabs/Multiplayer/UI/",
                       StringComparison.Ordinal) ||
                   path.StartsWith(
                       "Assets/MazeParty/Prefabs/Board/UI/",
                       StringComparison.Ordinal) ||
                   (path.StartsWith(
                        "Assets/MazeParty/Prefabs/Minigames/",
                        StringComparison.Ordinal) &&
                    path.IndexOf("/UI/", StringComparison.Ordinal) >= 0);
        }

        private static string FormatFontViolation(
            string assetPath,
            Transform prefabRoot,
            Transform textTransform,
            string details)
        {
            var transformPath = AnimationUtility.CalculateTransformPath(
                textTransform,
                prefabRoot);
            if (string.IsNullOrEmpty(transformPath))
            {
                transformPath = prefabRoot.name;
            }
            else
            {
                transformPath = prefabRoot.name + "/" + transformPath;
            }

            return assetPath + " :: " + transformPath + " :: " + details;
        }

        private static string MaskCommentsAndLiterals(string source)
        {
            var result = new StringBuilder(source.Length);
            for (var index = 0; index < source.Length;)
            {
                if (source[index] == '/' &&
                    index + 1 < source.Length &&
                    source[index + 1] == '/')
                {
                    result.Append("  ");
                    index += 2;
                    while (index < source.Length &&
                           source[index] != '\r' &&
                           source[index] != '\n')
                    {
                        result.Append(' ');
                        index++;
                    }
                    continue;
                }

                if (source[index] == '/' &&
                    index + 1 < source.Length &&
                    source[index + 1] == '*')
                {
                    result.Append("  ");
                    index += 2;
                    while (index < source.Length)
                    {
                        if (source[index] == '*' &&
                            index + 1 < source.Length &&
                            source[index + 1] == '/')
                        {
                            result.Append("  ");
                            index += 2;
                            break;
                        }

                        AppendMaskedCharacter(result, source[index]);
                        index++;
                    }
                    continue;
                }

                if (source[index] == '"')
                {
                    var verbatim = index > 0 && source[index - 1] == '@';
                    result.Append(' ');
                    index++;
                    while (index < source.Length)
                    {
                        if (verbatim &&
                            source[index] == '"' &&
                            index + 1 < source.Length &&
                            source[index + 1] == '"')
                        {
                            result.Append("  ");
                            index += 2;
                            continue;
                        }

                        if (source[index] == '"')
                        {
                            result.Append(' ');
                            index++;
                            break;
                        }

                        if (!verbatim &&
                            source[index] == '\\' &&
                            index + 1 < source.Length)
                        {
                            result.Append("  ");
                            index += 2;
                            continue;
                        }

                        AppendMaskedCharacter(result, source[index]);
                        index++;
                    }
                    continue;
                }

                if (source[index] == '\'')
                {
                    result.Append(' ');
                    index++;
                    while (index < source.Length)
                    {
                        if (source[index] == '\\' &&
                            index + 1 < source.Length)
                        {
                            result.Append("  ");
                            index += 2;
                            continue;
                        }

                        var terminates = source[index] == '\'';
                        AppendMaskedCharacter(result, source[index]);
                        index++;
                        if (terminates)
                        {
                            break;
                        }
                    }
                    continue;
                }

                result.Append(source[index]);
                index++;
            }

            return result.ToString();
        }

        private static void AppendMaskedCharacter(
            StringBuilder result,
            char value)
        {
            result.Append(value == '\r' || value == '\n' ? value : ' ');
        }

        private sealed class SceneUiContract
        {
            public SceneUiContract(
                string path,
                params string[] allowedPrefabPaths)
            {
                Path = path;
                AllowedPrefabPaths = allowedPrefabPaths;
            }

            public string Path { get; }
            public IReadOnlyCollection<string> AllowedPrefabPaths { get; }
        }

        private static bool IsVisualCanvasUiComponent(Component component)
        {
            // TMP creates a transient child renderer for sprite/fallback-font
            // material slots. The owning text remains covered by the policy.
            if (component is RectTransform || component is LayoutElement || component is TMP_SubMeshUI)
            {
                var renderer = component.GetComponent<TMP_SubMeshUI>();
                var owner = renderer != null ? renderer.textComponent : null;
                if (owner != null && renderer.transform.parent == owner.transform &&
                    (renderer.gameObject.hideFlags & HideFlags.DontSave) == HideFlags.DontSave &&
                    PrefabUtility.GetCorrespondingObjectFromSource(owner) != null)
                    return false;
            }

            return component is RectTransform ||
                   component is Canvas ||
                   component is CanvasScaler ||
                   component is CanvasGroup ||
                   component is Graphic ||
                   component is Selectable ||
                   component is LayoutGroup ||
                   component is LayoutElement ||
                   component is ContentSizeFitter ||
                   component is AspectRatioFitter ||
                   component is Mask ||
                   component is RectMask2D ||
                   component is Shadow;
        }
    }
}
