using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
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
        private static readonly string[] RequiredPrefabPaths =
        {
            "Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab",
            "Assets/MazeParty/UI/Prefabs/LobbyCanvas.prefab",
            "Assets/MazeParty/UI/Prefabs/MinefieldHud.prefab",
            "Assets/MazeParty/UI/Prefabs/WrongWayHud.prefab",
            "Assets/MazeParty/UI/Prefabs/RedLightGreenLightHud.prefab",
            "Assets/MazeParty/UI/Prefabs/StableFootingHud.prefab",
            "Assets/MazeParty/UI/Prefabs/BalloonBlowHud.prefab",
            "Assets/MazeParty/UI/Prefabs/BalloonBlowStationLabel.prefab",
            "Assets/MazeParty/UI/Prefabs/GiftGrabHud.prefab",
            "Assets/MazeParty/UI/Prefabs/GiftGrabBaseLabel.prefab",
            "Assets/MazeParty/UI/Prefabs/MinigameScheduleTower.prefab",
            "Assets/MazeParty/UI/Prefabs/Dev/GameplayTestbedCanvas.prefab",
            "Assets/MazeParty/UI/Prefabs/Dev/MinigameSoloHud.prefab",
            "Assets/MazeParty/UI/Prefabs/Dev/BoardFlowTestTools.prefab"
        };

        private static readonly string[] RequiredBindingTypeNames =
        {
            "MazeParty.Multiplayer.BoardCanvasBindings",
            "MazeParty.Multiplayer.OnlineLobbyView",
            "MazeParty.Multiplayer.MinefieldHudBindings",
            "MazeParty.Multiplayer.WrongWayHudBindings",
            "MazeParty.Multiplayer.RedLightGreenLightHudBindings",
            "MazeParty.Multiplayer.StableFootingHudBindings",
            "MazeParty.Multiplayer.BalloonBlowHudBindings",
            "MazeParty.Multiplayer.BalloonBlowStationLabel",
            "MazeParty.Multiplayer.GiftGrabHudBindings",
            "MazeParty.Multiplayer.GiftGrabBaseLabel",
            "MazeParty.Multiplayer.MinigameScheduleTowerView",
            "MazeParty.Gameplay.Testbed.GameplayTestbedUiBindings",
            "MazeParty.Multiplayer.MinigameSoloHudView",
            "MazeParty.Gameplay.BoardFlowTestbed.BoardFlowTestToolsBindings"
        };

        private static readonly SceneUiContract[] SceneContracts =
        {
            new SceneUiContract(
                "Assets/MazeParty/Scenes/OnlineBootstrap.unity",
                "Assets/MazeParty/UI/Prefabs/LobbyCanvas.prefab",
                "Assets/MazeParty/UI/Prefabs/MinigameScheduleTower.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Board.unity",
                "Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Dev/BoardFlowTestbed/BoardFlowTestbed.unity",
                "Assets/MazeParty/UI/Prefabs/BoardCanvas.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Dev/GameplayTestbed/GameplayTestbed.unity",
                "Assets/MazeParty/UI/Prefabs/Dev/GameplayTestbedCanvas.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/Minefield.unity",
                "Assets/MazeParty/UI/Prefabs/MinefieldHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/WrongWay.unity",
                "Assets/MazeParty/UI/Prefabs/WrongWayHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/RedLightGreenLight.unity",
                "Assets/MazeParty/UI/Prefabs/RedLightGreenLightHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/StableFooting.unity",
                "Assets/MazeParty/UI/Prefabs/StableFootingHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/BalloonBlow.unity",
                "Assets/MazeParty/UI/Prefabs/BalloonBlowHud.prefab"),
            new SceneUiContract(
                "Assets/MazeParty/Scenes/GiftGrab.unity",
                "Assets/MazeParty/UI/Prefabs/GiftGrabHud.prefab")
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
                        canvases,
                        Is.Not.Empty,
                        contract.Path + " must contain prefab Canvas UI.");

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

                    if (contract.Path ==
                        "Assets/MazeParty/Dev/BoardFlowTestbed/" +
                        "BoardFlowTestbed.unity")
                    {
                        var toolsBinding = scene.GetRootGameObjects()
                            .SelectMany(root =>
                                root.GetComponentsInChildren<MonoBehaviour>(true))
                            .SingleOrDefault(component =>
                                component != null &&
                                component.GetType().FullName ==
                                "MazeParty.Gameplay.BoardFlowTestbed." +
                                "BoardFlowTestToolsBindings");
                        Assert.That(toolsBinding, Is.Not.Null);
                        Assert.That(
                            PrefabUtility
                                .GetPrefabAssetPathOfNearestInstanceRoot(
                                    toolsBinding.gameObject),
                            Is.EqualTo(
                                "Assets/MazeParty/UI/Prefabs/Dev/" +
                                "BoardFlowTestTools.prefab"));
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
                        visualPrefabRoots.Add(instanceRoot);
                        Assert.That(
                            RequiredPrefabPaths,
                            Does.Contain(PrefabUtility
                                .GetPrefabAssetPathOfNearestInstanceRoot(
                                    component.gameObject)),
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
        public void RuntimeSources_DoNotProcedurallyConstructCanvasUi()
        {
            var projectRoot = Directory.GetCurrentDirectory();
            var scanRoots = new[]
            {
                "Assets/MazeParty/Runtime",
                "Assets/MazeParty/Gameplay/Runtime",
                "Assets/MazeParty/Dev/MinigameSoloTest/Scripts",
                "Assets/MazeParty/Dev/GameplayTestbed/Scripts",
                "Assets/MazeParty/Dev/BoardFlowTestbed/Scripts"
            };
            var violations = new List<string>();

            foreach (var relativeRoot in scanRoots)
            {
                var absoluteRoot = Path.Combine(projectRoot, relativeRoot);
                foreach (var file in Directory.GetFiles(
                             absoluteRoot,
                             "*.cs",
                             SearchOption.AllDirectories))
                {
                    var source = MaskCommentsAndLiterals(
                        File.ReadAllText(file));
                    if (EditorWindowDeclarationPattern.IsMatch(source))
                    {
                        continue;
                    }

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
                return modification.propertyPath == "m_Name";
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

                // The network-free board testbed keeps the prefab-owned view
                // and all of its internal child bindings, but disables only
                // that behavior so the local simulator owns presentation.
                if (component is BoardFlowView &&
                    modification.propertyPath == "m_Enabled" &&
                    modification.value == "0" &&
                    instanceRoot.scene.path ==
                    "Assets/MazeParty/Dev/BoardFlowTestbed/" +
                    "BoardFlowTestbed.unity")
                {
                    return true;
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
            return transform.drivenByObject != null ||
                   transform.GetComponent<ContentSizeFitter>() != null ||
                   (transform.parent != null &&
                    transform.parent.GetComponent<LayoutGroup>() != null);
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
