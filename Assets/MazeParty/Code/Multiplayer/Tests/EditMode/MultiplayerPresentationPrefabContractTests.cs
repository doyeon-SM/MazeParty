using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MultiplayerPresentationPrefabContractTests
    {
        private const string PlayerPresentationPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/PlayerAvatarPresentation.prefab";
        private const string PlayerWorldIndicatorPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/PlayerWorldIndicator.prefab";
        private const string WaterShieldPrefabPath =
            "Assets/MazeParty/Prefabs/Common/VFX/WaterShield.prefab";
        private const string HostStarSpritePath =
            "Assets/Ignore/Modern UI Pack/Textures/Icon/Common/Star Filled.png";
        private const string NetworkPlayerPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/NetworkPlayer.prefab";
        private const string GrenadeRangeIndicatorPrefabPath =
            "Assets/MazeParty/Prefabs/Board/UI/GrenadeRangeIndicator.prefab";
        private const string LobbyArenaPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/World/LobbyArena.prefab";
        private const string BootstrapScenePath =
            "Assets/MazeParty/Scenes/Multiplayer/OnlineBootstrap.unity";

        [Test]
        public void PlayerPresentationPrefab_HasCompleteAuthoredBindings()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerPresentationPrefabPath);
            Assert.That(prefab, Is.Not.Null, PlayerPresentationPrefabPath);
            var bindings = prefab.GetComponent<
                PlayerAvatarPresentationBindings>();
            Assert.That(bindings, Is.Not.Null);
            Assert.That(bindings.HasRequiredReferences, Is.True);
            Assert.That(bindings.NameplateAnchor.gameObject.activeSelf, Is.True);
            Assert.That(bindings.NameText, Is.Not.Null);
            Assert.That(bindings.LobbyHostIcon, Is.Not.Null);
            Assert.That(
                bindings.LobbyHostIcon.transform.parent,
                Is.SameAs(bindings.NameplateAnchor),
                "The lobby host icon must be authored as part of the world nameplate.");
            Assert.That(
                bindings.LobbyHostIcon.transform.localPosition.x,
                Is.LessThan(bindings.NameText.transform.localPosition.x),
                "The host star must appear to the left of the nickname.");
            Assert.That(bindings.LobbyHostIcon.sprite, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(bindings.LobbyHostIcon.sprite),
                Is.EqualTo(HostStarSpritePath));
            Assert.That(bindings.LobbyHostIcon.gameObject.activeSelf, Is.False);
            Assert.That(bindings.ShieldVfx, Is.Not.Null);
            Assert.That(bindings.ShieldVfx.activeSelf, Is.False);
            Assert.That(bindings.ShieldVfx, Is.Not.SameAs(
                bindings.TopViewHighlight));
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    bindings.ShieldVfx),
                Is.EqualTo(WaterShieldPrefabPath));
            Assert.That(
                bindings.LobbyReadyNameColor.g,
                Is.GreaterThan(bindings.LobbyReadyNameColor.r),
                "The authored ready-name color must read as green.");
            Assert.That(
                bindings.LobbyReadyNameColor.g,
                Is.GreaterThan(bindings.LobbyReadyNameColor.b),
                "The authored ready-name color must read as green.");
            Assert.That(
                bindings.NameText.GetComponent<WorldTextOcclusion>(),
                Is.Not.Null,
                "Opponent nameplates must use normal scene depth occlusion.");
            Assert.That(bindings.BodyTintRenderers, Is.Not.Empty);
            Assert.That(bindings.HandEmoteRotations, Is.Not.Null);
            Assert.That(
                bindings.HandEmoteRotations.SchemaVersion,
                Is.EqualTo(HandEmoteRotationSettings.CurrentSchemaVersion));
            for (var gesture = 1;
                 gesture <= HandEmoteRules.GestureCount;
                 gesture++)
            {
                Assert.That(
                    bindings.TryGetHandEmoteRotation(
                        (HandEmoteId)gesture,
                        out var pose,
                        out _),
                    Is.True,
                    ((HandEmoteId)gesture) +
                    " must have authored hand rotations.");
                Assert.That(pose, Is.Not.Null);
            }
            Assert.That(bindings.HatAnchor.childCount, Is.Zero,
                "Hat models must come from the appearance catalog, not placeholder geometry.");
            Assert.That(
                prefab.GetComponentsInChildren<Transform>(true)
                    .Any(child => child.name.StartsWith("TestHat")),
                Is.False);
            AssertHitZone(bindings.BodyHitbox, PlayerHitRegion.Body);
            AssertHitZone(bindings.HeadHitbox, PlayerHitRegion.Head);
            AssertHitZone(bindings.LeftHandHitbox, PlayerHitRegion.Hand);
            AssertHitZone(bindings.RightHandHitbox, PlayerHitRegion.Hand);
            Assert.That(
                prefab.GetComponentsInChildren<Transform>(true)
                    .Sum(child => GameObjectUtility
                        .GetMonoBehavioursWithMissingScriptCount(
                            child.gameObject)),
                Is.Zero,
                "The shared presentation must not contain missing scripts.");
            Assert.That(
                prefab.GetComponentsInChildren<NetworkObject>(true),
                Is.Empty,
                "The shared presentation must remain local-only.");
            Assert.That(
                prefab.GetComponentsInChildren<CharacterController>(true),
                Is.Empty);

            var assets = Resources.Load<PlayerAvatarPresentationAssets>(
                PlayerAvatarPresentationAssets.ResourcePath);
            Assert.That(assets, Is.Not.Null);
            Assert.That(assets.HasRequiredReferences, Is.True);
            Assert.That(
                AssetDatabase.GetAssetPath(assets.PresentationPrefab),
                Is.EqualTo(PlayerPresentationPrefabPath));
            Assert.That(
                AssetDatabase.GetAssetPath(assets.WorldIndicatorPrefab),
                Is.EqualTo(PlayerWorldIndicatorPrefabPath));
        }

        [Test]
        public void PlayerWorldIndicatorPrefab_HasAuthoredNameAndStartMarker()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerWorldIndicatorPrefabPath);
            Assert.That(prefab, Is.Not.Null, PlayerWorldIndicatorPrefabPath);
            var indicator = prefab.GetComponent<PlayerWorldIndicator>();
            Assert.That(indicator, Is.Not.Null);
            Assert.That(indicator.HasRequiredReferences, Is.True);
            Assert.That(indicator.NameplateAnchor, Is.Not.Null);
            Assert.That(indicator.NameText, Is.Not.Null);
            Assert.That(indicator.LocalStartHighlight, Is.Not.Null);
            Assert.That(indicator.LocalStartHighlight.activeSelf, Is.False);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    indicator.LocalStartHighlight),
                Is.EqualTo(WaterShieldPrefabPath),
                "Special minigame representations must use the shared 3D shield marker.");
            Assert.That(
                prefab.GetComponentsInChildren<Canvas>(true),
                Is.Empty,
                "The shared marker must remain an authored world-space prefab.");
        }

        [Test]
        public void PlayerAvatarVisual_InstantiatesOneSharedPresentation()
        {
            var root = new GameObject("Player Presentation Contract Root");
            try
            {
                var visual = root.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                visual.EnsureBuilt();
                Assert.That(visual.Bindings, Is.Not.Null);
                Assert.That(visual.Bindings.HasRequiredReferences, Is.True);
                Assert.That(
                    root.GetComponentsInChildren<
                        PlayerAvatarPresentationBindings>(true),
                    Has.Length.EqualTo(1));
                var assets = Resources.Load<PlayerAvatarPresentationAssets>(
                    PlayerAvatarPresentationAssets.ResourcePath);
                Assert.That(assets, Is.Not.Null);
                Assert.That(
                    visual.Bindings.gameObject.name,
                    Is.EqualTo(assets.PresentationPrefab.gameObject.name));

                var nameRenderer = visual.Bindings.NameText
                    .GetComponent<Renderer>();
                Assert.That(nameRenderer, Is.Not.Null);
                var hostIcon = visual.Bindings.LobbyHostIcon;
                var defaultNameColor = visual.Bindings.NameText.color;
                var identityObjectCount = root
                    .GetComponentsInChildren<Transform>(true)
                    .Length;

                visual.SetLobbyIdentityState(true, false);
                Assert.That(
                    visual.Bindings.NameText.color,
                    Is.EqualTo(visual.Bindings.LobbyReadyNameColor));
                Assert.That(hostIcon.gameObject.activeSelf, Is.False);

                visual.SetLobbyIdentityState(false, true);
                Assert.That(
                    visual.Bindings.NameText.color,
                    Is.EqualTo(defaultNameColor));
                Assert.That(hostIcon.gameObject.activeSelf, Is.True);

                visual.SetLobbyIdentityState(true, true);
                Assert.That(
                    visual.Bindings.NameText.color,
                    Is.EqualTo(visual.Bindings.LobbyReadyNameColor));
                Assert.That(hostIcon.gameObject.activeSelf, Is.True);
                Assert.That(
                    root.GetComponentsInChildren<Transform>(true).Length,
                    Is.EqualTo(identityObjectCount),
                    "Lobby identity changes must not create runtime presentation objects.");

                visual.SetNameplateOccluded(true);
                Assert.That(nameRenderer.forceRenderingOff, Is.True);
                Assert.That(hostIcon.forceRenderingOff, Is.True);
                Assert.That(visual.Bindings.WorldModel.gameObject.activeSelf, Is.True);
                visual.SetNameplateOccluded(false);
                Assert.That(nameRenderer.forceRenderingOff, Is.False);
                Assert.That(hostIcon.forceRenderingOff, Is.False);

                visual.SetLobbyIdentityState(false, false);
                Assert.That(
                    visual.Bindings.NameText.color,
                    Is.EqualTo(defaultNameColor));
                Assert.That(hostIcon.gameObject.activeSelf, Is.False,
                    "Leaving the lobby must clear the host marker.");

                var pistol = PrototypeItemCatalog.Get(PrototypeItemId.Pistol);
                visual.SetEquippedItem(PrototypeItemId.Pistol);
                Assert.That(visual.Bindings.LeftHandAnchor.gameObject.activeSelf, Is.False);
                Assert.That(visual.Bindings.RightHandAnchor.gameObject.activeSelf, Is.False);
                Assert.That(visual.Bindings.WorldItemRoot.gameObject.activeSelf, Is.True);
                Assert.That(
                    visual.Bindings.WorldItemRoot.Cast<Transform>()
                        .Single(child => child.gameObject.activeSelf).name,
                    Does.StartWith(pistol.HeldPrefab.name));
                visual.SetEquippedItem(PrototypeItemId.None);
                Assert.That(visual.Bindings.LeftHandAnchor.gameObject.activeSelf, Is.True);
                Assert.That(visual.Bindings.RightHandAnchor.gameObject.activeSelf, Is.True);
                Assert.That(visual.Bindings.WorldItemRoot.gameObject.activeSelf, Is.False);

                visual.SetEquippedItem(PrototypeItemId.DoubleDice);
                Assert.That(visual.Bindings.LeftHandAnchor.gameObject.activeSelf, Is.True);
                Assert.That(visual.Bindings.RightHandAnchor.gameObject.activeSelf, Is.True);
                Assert.That(visual.Bindings.WorldItemRoot.gameObject.activeSelf, Is.False);
                visual.SetEquippedItem(PrototypeItemId.None);

                var catalog = PlayerExpressionCatalog.Instance;
                Assert.That(catalog, Is.Not.Null);
                var authoredPresentation = assets.PresentationPrefab;
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        authoredPresentation.LeftHandAnchor.GetChild(0).gameObject),
                    Is.EqualTo(
                        "Assets/MazeParty/Prefabs/Multiplayer/Player/SimpleFistHand.prefab"));
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        authoredPresentation.RightHandAnchor.GetChild(0).gameObject),
                    Is.EqualTo(
                        "Assets/MazeParty/Prefabs/Multiplayer/Player/SimpleFistHand.prefab"));

                visual.ApplyAppearance(0, 0, 0, 2);
                visual.SetHandGesture((byte)HandEmoteId.Greeting, 5);
                Assert.That(
                    visual.Bindings.FaceSprite.sprite,
                    Is.SameAs(catalog.Faces[5].Sprite));
                Assert.That(visual.Bindings.LeftHandAnchor.gameObject.activeSelf, Is.True);
                Assert.That(visual.Bindings.RightHandAnchor.gameObject.activeSelf, Is.True);
                Assert.That(visual.Bindings.WorldGestureRoot.gameObject.activeSelf, Is.False,
                    "Emotes must animate the player's normal hands instead of legacy gesture prefabs.");
                visual.SetHandGesture((byte)HandEmoteId.EyesCover, 7);
                Assert.That(
                    visual.Bindings.FaceSprite.sprite,
                    Is.SameAs(catalog.Faces[2].Sprite),
                    "Eyes-cover must preserve the wardrobe base face.");
                visual.SetHandGesture(0, 2);
                Assert.That(
                    visual.Bindings.FaceSprite.sprite,
                    Is.SameAs(catalog.Faces[2].Sprite));

                var worldLeftIndex = FindHandBone(
                    visual.Bindings.LeftHandAnchor,
                    "IndexFinger");
                var worldRightIndex = FindHandBone(
                    visual.Bindings.RightHandAnchor,
                    "IndexFinger");
                var firstLeftIndex = FindHandBone(
                    visual.Bindings.FirstPersonLeftHand,
                    "IndexFinger");
                var firstRightIndex = FindHandBone(
                    visual.Bindings.FirstPersonRightHand,
                    "IndexFinger");
                var worldLeftClosed = worldLeftIndex.localRotation;
                var worldRightClosed = worldRightIndex.localRotation;
                var firstLeftClosed = firstLeftIndex.localRotation;
                var firstRightClosed = firstRightIndex.localRotation;
                var updatePose = typeof(PlayerAvatarVisual).GetMethod(
                    "UpdatePose",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var gestureStartedAt = typeof(PlayerAvatarVisual).GetField(
                    "_gestureStartedAt",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(updatePose, Is.Not.Null);
                Assert.That(gestureStartedAt, Is.Not.Null);

                foreach (var oneHandGesture in new[]
                         {
                             HandEmoteId.Greeting,
                             HandEmoteId.Salute
                         })
                {
                    visual.SetHandGesture((byte)oneHandGesture, 5);
                    gestureStartedAt.SetValue(
                        visual,
                        Time.time - (float)HandEmoteRules.Duration * 0.06f);
                    updatePose.Invoke(visual, new object[] { false });
                    var stableFirstPersonPosition =
                        visual.Bindings.FirstPersonRightHand.localPosition;
                    var stableFirstPersonRotation =
                        visual.Bindings.FirstPersonRightHand.localRotation;
                    gestureStartedAt.SetValue(
                        visual,
                        Time.time - (float)HandEmoteRules.Duration * 0.06f);
                    updatePose.Invoke(visual, new object[] { false });

                    Assert.That(
                        Vector3.Distance(
                            stableFirstPersonPosition,
                            visual.Bindings.FirstPersonRightHand.localPosition),
                        Is.LessThan(0.0001f),
                        oneHandGesture +
                        " must be evaluated from the cached root pose each frame.");
                    Assert.That(
                        Quaternion.Angle(
                            stableFirstPersonRotation,
                            visual.Bindings.FirstPersonRightHand.localRotation),
                        Is.LessThan(0.01f),
                        oneHandGesture +
                        " must not accumulate root rotation between frames.");

                    Assert.That(
                        Quaternion.Angle(
                            worldLeftClosed,
                            worldLeftIndex.localRotation),
                        Is.LessThan(0.01f),
                        oneHandGesture + " must leave the world left hand closed.");
                    Assert.That(
                        Quaternion.Angle(
                            firstLeftClosed,
                            firstLeftIndex.localRotation),
                        Is.LessThan(0.01f),
                        oneHandGesture + " must leave the first-person left hand closed.");
                    Assert.That(
                        Quaternion.Angle(
                            worldRightClosed,
                            worldRightIndex.localRotation),
                        Is.GreaterThan(1f),
                        oneHandGesture + " must open the world right hand.");
                    Assert.That(
                        Quaternion.Angle(
                            firstRightClosed,
                            firstRightIndex.localRotation),
                        Is.GreaterThan(1f),
                        oneHandGesture + " must open the first-person right hand.");

                    visual.SetHandGesture(0, 2);
                    AssertHandAnchorRestored(
                        visual.Bindings.LeftHandAnchor,
                        authoredPresentation.LeftHandAnchor);
                    AssertHandAnchorRestored(
                        visual.Bindings.RightHandAnchor,
                        authoredPresentation.RightHandAnchor);
                    AssertHandAnchorRestored(
                        visual.Bindings.FirstPersonLeftHand,
                        authoredPresentation.FirstPersonLeftHand);
                    AssertHandAnchorRestored(
                        visual.Bindings.FirstPersonRightHand,
                        authoredPresentation.FirstPersonRightHand);
                    Assert.That(
                        Quaternion.Angle(
                            worldLeftClosed,
                            worldLeftIndex.localRotation),
                        Is.LessThan(0.01f));
                    Assert.That(
                        Quaternion.Angle(
                            worldRightClosed,
                            worldRightIndex.localRotation),
                        Is.LessThan(0.01f));
                    Assert.That(
                        Quaternion.Angle(
                            firstLeftClosed,
                            firstLeftIndex.localRotation),
                        Is.LessThan(0.01f));
                    Assert.That(
                        Quaternion.Angle(
                            firstRightClosed,
                            firstRightIndex.localRotation),
                        Is.LessThan(0.01f));
                }
                Assert.That(
                    visual.Bindings.HatAnchor.childCount,
                    Is.Zero,
                    "Hat models must be instantiated only when selected.");
                visual.ApplyAppearance(0, 0, 2, 1);
                Assert.That(visual.Bindings.HatAnchor.gameObject.activeSelf, Is.True);
                Assert.That(visual.Bindings.HatAnchor.childCount, Is.EqualTo(1));
                var selectedHat = visual.Bindings.HatAnchor.GetChild(0).gameObject;
                Assert.That(selectedHat.name, Is.EqualTo(catalog.Hats[1].Prefab.name));
                Assert.That(
                    visual.Bindings.HatAnchor.Cast<Transform>()
                        .Count(child => child.gameObject.activeSelf),
                    Is.EqualTo(1));
                foreach (var renderer in selectedHat.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    for (var blendShape = 0;
                         blendShape < renderer.sharedMesh.blendShapeCount;
                         blendShape++)
                    {
                        if (renderer.sharedMesh.GetBlendShapeName(blendShape) == "Scale")
                        {
                            Assert.That(renderer.GetBlendShapeWeight(blendShape), Is.Zero);
                        }
                    }
                }
                visual.ApplyAppearance(0, 0, (byte)catalog.Hats.Length,
                    (byte)(catalog.Faces.Length - 1));
                Assert.That(visual.Bindings.HatAnchor.childCount, Is.EqualTo(2));
                Assert.That(
                    visual.Bindings.HatAnchor.Cast<Transform>()
                        .Count(child => child.gameObject.activeSelf),
                    Is.EqualTo(1));
                Assert.That(
                    visual.Bindings.HatAnchor.Cast<Transform>()
                        .Single(child => child.gameObject.activeSelf).name,
                    Is.EqualTo(catalog.Hats[catalog.Hats.Length - 1].Prefab.name));
                visual.ApplyAppearance(0, 0, 0, 2);
                Assert.That(visual.Bindings.HatAnchor.gameObject.activeSelf, Is.False);
                Assert.That(
                    visual.Bindings.HatAnchor.Cast<Transform>()
                        .Any(child => child.gameObject.activeSelf),
                    Is.False);

                var authoredHighlight = visual.Bindings.TopViewHighlight;
                var authoredObjectCount = root
                    .GetComponentsInChildren<Transform>(true)
                    .Length;
                visual.SetTopViewHighlight(true);
                Assert.That(authoredHighlight.activeSelf, Is.True);
                Assert.That(
                    root.GetComponentsInChildren<Transform>(true).Length,
                    Is.EqualTo(authoredObjectCount),
                    "Highlight toggling must not create runtime geometry.");
                visual.SetTopViewHighlight(false);
                Assert.That(authoredHighlight.activeSelf, Is.False);

                var authoredShield = visual.Bindings.ShieldVfx;
                visual.SetBoardProtectionVisible(true);
                Assert.That(authoredShield.activeSelf, Is.True);
                visual.SetLocationHighlightVisible(true);
                visual.SetBoardProtectionVisible(false);
                Assert.That(authoredShield.activeSelf, Is.True,
                    "The round-location request must survive board protection ending.");
                visual.SetHiddenFromViewer(true);
                Assert.That(authoredShield.activeSelf, Is.False,
                    "A hidden avatar must not leave its shield presentation behind.");
                visual.SetHiddenFromViewer(false);
                Assert.That(authoredShield.activeSelf, Is.True,
                    "The active location request must resume with avatar visibility.");
                visual.SetLocationHighlightVisible(false);
                Assert.That(authoredShield.activeSelf, Is.False);
                Assert.That(
                    root.GetComponentsInChildren<Transform>(true).Length,
                    Is.EqualTo(authoredObjectCount),
                    "Shield state changes must not create runtime geometry.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NetworkPlayer_UsesNestedAuthoredPresentationAndEyePivot()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NetworkPlayerPrefabPath);
            Assert.That(prefab, Is.Not.Null, NetworkPlayerPrefabPath);
            var visual = prefab.GetComponent<PlayerAvatarVisual>();
            var avatar = prefab.GetComponent<NetworkPlayerAvatar>();
            Assert.That(visual, Is.Not.Null);
            Assert.That(avatar, Is.Not.Null);
            Assert.That(visual.Bindings, Is.Not.Null);
            Assert.That(visual.Bindings.HasRequiredReferences, Is.True);
            var source = PrefabUtility.GetCorrespondingObjectFromSource(
                visual.Bindings);
            Assert.That(source, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(source),
                Is.EqualTo(PlayerPresentationPrefabPath));

            var eyePivot = prefab.transform.Find("CameraPivot");
            Assert.That(eyePivot, Is.Not.Null);
            var serializedAvatar = new SerializedObject(avatar);
            Assert.That(
                serializedAvatar.FindProperty("eyePivot").objectReferenceValue,
                Is.SameAs(eyePivot));
            Assert.That(prefab.GetComponent<PlayerHitZoneOwner>(), Is.Not.Null);
        }

        [Test]
        public void HandEmoteRotations_ApplyAuthoredWorldAndFirstPersonValues()
        {
            var root = new GameObject("Hand Emote Rotation Contract Root");
            try
            {
                var visual = root.AddComponent<PlayerAvatarVisual>();
                visual.EnsureBuilt();
                Assert.That(visual.Bindings, Is.Not.Null);

                var serialized = new SerializedObject(visual.Bindings);
                var settings = serialized.FindProperty("handEmoteRotations");
                Assert.That(settings, Is.Not.Null);
                settings.FindPropertyRelative("greetingWaveDegrees")
                    .floatValue = 0f;

                var names = new[]
                {
                    "greeting", "salute", "insult", "heart",
                    "surprise", "surrender", "pleading", "eyesCover"
                };
                var expected = new Vector3[names.Length, 4];
                for (var index = 0; index < names.Length; index++)
                {
                    var entry = settings.FindPropertyRelative(names[index]);
                    Assert.That(entry, Is.Not.Null, names[index]);
                    expected[index, 0] = new Vector3(
                        index + 1f,
                        index + 2f,
                        index + 3f);
                    expected[index, 1] = new Vector3(
                        index + 11f,
                        index + 12f,
                        index + 13f);
                    expected[index, 2] = new Vector3(
                        index + 21f,
                        index + 22f,
                        index + 23f);
                    expected[index, 3] = new Vector3(
                        index + 31f,
                        index + 32f,
                        index + 33f);
                    entry.FindPropertyRelative("worldLeftEuler")
                        .vector3Value = expected[index, 0];
                    entry.FindPropertyRelative("worldRightEuler")
                        .vector3Value = expected[index, 1];
                    entry.FindPropertyRelative("firstPersonLeftEuler")
                        .vector3Value = expected[index, 2];
                    entry.FindPropertyRelative("firstPersonRightEuler")
                        .vector3Value = expected[index, 3];
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();

                var updatePose = typeof(PlayerAvatarVisual).GetMethod(
                    "UpdatePose",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var gestureStartedAt = typeof(PlayerAvatarVisual).GetField(
                    "_gestureStartedAt",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(updatePose, Is.Not.Null);
                Assert.That(gestureStartedAt, Is.Not.Null);

                for (var index = 0; index < names.Length; index++)
                {
                    visual.SetHandGesture((byte)(index + 1), 0);
                    gestureStartedAt.SetValue(
                        visual,
                        Time.time - (float)HandEmoteRules.Duration * 0.5f);
                    updatePose.Invoke(visual, new object[] { false });

                    AssertRotation(
                        visual.Bindings.LeftHandAnchor,
                        expected[index, 0]);
                    AssertRotation(
                        visual.Bindings.RightHandAnchor,
                        expected[index, 1]);
                    AssertRotation(
                        visual.Bindings.FirstPersonLeftHand,
                        expected[index, 2]);
                    AssertRotation(
                        visual.Bindings.FirstPersonRightHand,
                        expected[index, 3]);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void HandEmoteRotationMigration_ReentryAndValidationPreserveAuthoredValues()
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerPresentationPrefabPath);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            try
            {
                var bindings = instance.GetComponent<
                    PlayerAvatarPresentationBindings>();
                Assert.That(bindings, Is.Not.Null);
                var serialized = new SerializedObject(bindings);
                var custom = new Vector3(17f, -29f, 43f);
                serialized.FindProperty("handEmoteRotations")
                    .FindPropertyRelative("salute")
                    .FindPropertyRelative("firstPersonRightEuler")
                    .vector3Value = custom;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                Assert.That(
                    bindings.EnsureHandEmoteRotationSettings(),
                    Is.False,
                    "A completed schema must not be initialized again.");
                var onValidate = typeof(PlayerAvatarPresentationBindings)
                    .GetMethod(
                        "OnValidate",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onValidate, Is.Not.Null);
                onValidate.Invoke(bindings, null);

                Assert.That(
                    bindings.TryGetHandEmoteRotation(
                        HandEmoteId.Salute,
                        out var pose,
                        out _),
                    Is.True);
                Assert.That(pose.FirstPersonRightEuler, Is.EqualTo(custom));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void NetworkPlayer_UsesOneBodySizedSolidControllerAndAuthoredTriggerHitZones()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NetworkPlayerPrefabPath);
            Assert.That(prefab, Is.Not.Null, NetworkPlayerPrefabPath);

            var movementControllers = prefab.GetComponentsInChildren<
                CharacterController>(true);
            Assert.That(
                movementControllers,
                Has.Length.EqualTo(1),
                "Player movement must be blocked only by the root body capsule.");
            Assert.That(
                movementControllers[0].transform,
                Is.SameAs(prefab.transform));
            Assert.That(movementControllers[0].enabled, Is.True);
            Assert.That(
                movementControllers[0].radius,
                Is.EqualTo(PlayerAvatarVisual.MovementControllerRadius)
                    .Within(0.0001f));
            Assert.That(
                movementControllers[0].height,
                Is.EqualTo(PlayerAvatarVisual.StandingControllerHeight)
                    .Within(0.0001f));
            Assert.That(
                movementControllers[0].center.y,
                Is.EqualTo(PlayerAvatarVisual.StandingControllerCenterY)
                    .Within(0.0001f));

            var hitZones = prefab.GetComponentsInChildren<PlayerHitZone>(true);
            Assert.That(hitZones, Has.Length.EqualTo(4));
            Assert.That(
                hitZones.Count(zone => zone.Region == PlayerHitRegion.Body),
                Is.EqualTo(1));
            Assert.That(
                hitZones.Count(zone => zone.Region == PlayerHitRegion.Head),
                Is.EqualTo(1));
            Assert.That(
                hitZones.Count(zone => zone.Region == PlayerHitRegion.Hand),
                Is.EqualTo(2));

            var nonControllerColliders = prefab
                .GetComponentsInChildren<Collider>(true)
                .Where(collider => collider != movementControllers[0])
                .ToArray();
            Assert.That(nonControllerColliders, Has.Length.EqualTo(4));
            Assert.That(
                nonControllerColliders.All(collider => collider.isTrigger),
                Is.True,
                "Head, hand, and body hit zones must never block movement.");
            Assert.That(
                nonControllerColliders.All(
                    collider => collider.GetComponent<PlayerHitZone>() != null),
                Is.True,
                "Every non-movement collider must be an authored PlayerHitZone.");
        }

        [Test]
        public void GrenadeRangeIndicator_UsesOwnerBoundAuthoredWorldPrefab()
        {
            var indicatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                GrenadeRangeIndicatorPrefabPath);
            Assert.That(
                indicatorPrefab,
                Is.Not.Null,
                GrenadeRangeIndicatorPrefabPath);
            var authoredIndicator = indicatorPrefab.GetComponent<
                BoardGrenadeRangeIndicator>();
            Assert.That(authoredIndicator, Is.Not.Null);
            Assert.That(authoredIndicator.HasRequiredReferences, Is.True);
            Assert.That(authoredIndicator.IsVisible, Is.False);
            Assert.That(
                indicatorPrefab.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The range ring is presentation only and must not affect physics.");
            Assert.That(
                indicatorPrefab.GetComponentsInChildren<NetworkObject>(true),
                Is.Empty,
                "The owner-only range ring must not be a network object.");

            var playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                NetworkPlayerPrefabPath);
            Assert.That(playerPrefab, Is.Not.Null, NetworkPlayerPrefabPath);
            var nestedIndicator = playerPrefab.GetComponentsInChildren<
                BoardGrenadeRangeIndicator>(true).Single();
            var source = PrefabUtility.GetCorrespondingObjectFromSource(
                nestedIndicator);
            Assert.That(source, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(source),
                Is.EqualTo(GrenadeRangeIndicatorPrefabPath));

            var avatar = playerPrefab.GetComponent<NetworkPlayerAvatar>();
            Assert.That(avatar, Is.Not.Null);
            var serializedAvatar = new SerializedObject(avatar);
            var binding = serializedAvatar.FindProperty(
                "grenadeRangeIndicator");
            Assert.That(binding, Is.Not.Null);
            Assert.That(
                binding.objectReferenceValue,
                Is.SameAs(nestedIndicator));
        }

        [Test]
        public void LobbyArenaPrefabAndBootstrap_HaveAuthoredContracts()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                LobbyArenaPrefabPath);
            Assert.That(prefab, Is.Not.Null, LobbyArenaPrefabPath);
            var prefabArena = prefab.GetComponent<LobbyArena>();
            Assert.That(prefabArena, Is.Not.Null);
            Assert.That(prefabArena.HasRequiredReferences, Is.True);
            Assert.That(prefabArena.Bindings.PresentationRoot, Is.Not.Null);
            Assert.That(
                prefabArena.Bindings.PresentationRoot
                    .GetComponentsInChildren<Renderer>(true),
                Is.Not.Empty,
                "Lobby room visuals must be authored in the prefab.");
            var mazeSources = prefabArena.Bindings.PresentationRoot
                .GetComponentsInChildren<Transform>(true)
                .Select(transform =>
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        transform.gameObject))
                .Where(path => path.StartsWith(
                    "Assets/Ignore/Maze/Prefabs/"))
                .Distinct()
                .ToArray();
            Assert.That(mazeSources, Is.Not.Empty,
                "The authored prison lobby must use nested Maze source prefabs.");
            Assert.That(
                prefabArena.Bindings.PresentationRoot
                    .GetComponentsInChildren<MeshFilter>(true)
                    .Any(filter => filter.sharedMesh != null &&
                                   filter.sharedMesh.name == "Gate_Door_6M"),
                Is.True, "The authored prison entrance must render iron bars.");
            Assert.That(
                prefabArena.SpawnCount,
                Is.EqualTo(MultiplayerConstants.MaxPlayers));
            Assert.That(prefabArena.Bindings.HorizontalBounds.enabled, Is.False);

            var scene = SceneManager.GetSceneByPath(BootstrapScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    BootstrapScenePath,
                    OpenSceneMode.Additive);
            }
            try
            {
                var arenas = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<LobbyArena>(true))
                    .ToArray();
                Assert.That(arenas, Has.Length.EqualTo(1));
                Assert.That(arenas[0].HasRequiredReferences, Is.True);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        arenas[0].gameObject),
                    Is.EqualTo(LobbyArenaPrefabPath));

                Assert.That(
                    scene.GetRootGameObjects().Any(root =>
                        root.name == "South Iron Gate Left"),
                    Is.False,
                    "Lobby decoration roots must not remain visible when " +
                    "the Board scene hides LobbyArena/Presentation.");
                var southGate = arenas[0].Bindings.PresentationRoot
                    .GetComponentsInChildren<Transform>(true)
                    .Where(transform =>
                        transform.name == "South Iron Gate Left")
                    .SingleOrDefault(transform => Vector3.Distance(
                        transform.localPosition,
                        new Vector3(12f, 0f, -7.25f)) < 0.001f);
                Assert.That(southGate, Is.Not.Null);
                Assert.That(
                    PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        southGate.gameObject),
                    Is.EqualTo(
                        "Assets/Ignore/Maze/Prefabs/Wall_Door_6M.prefab"));
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void AssertHitZone(
            Collider collider,
            PlayerHitRegion expectedRegion)
        {
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.isTrigger, Is.True);
            var zone = collider.GetComponent<PlayerHitZone>();
            Assert.That(zone, Is.Not.Null, collider.gameObject.name);
            Assert.That(zone.Region, Is.EqualTo(expectedRegion));
        }

        private static Transform FindHandBone(Transform root, string name)
        {
            return root.GetComponentsInChildren<Transform>(true)
                .Single(transform => transform.name == name);
        }

        private static void AssertHandAnchorRestored(
            Transform actual,
            Transform authored)
        {
            Assert.That(
                Vector3.Distance(actual.localPosition, authored.localPosition),
                Is.LessThan(0.0001f),
                actual.name + " local position was not restored.");
            Assert.That(
                Quaternion.Angle(actual.localRotation, authored.localRotation),
                Is.LessThan(0.01f),
                actual.name + " local rotation was not restored.");
        }

        private static void AssertRotation(
            Transform actual,
            Vector3 expectedEuler)
        {
            Assert.That(
                Quaternion.Angle(
                    actual.localRotation,
                    Quaternion.Euler(expectedEuler)),
                Is.LessThan(0.05f),
                actual.name + " did not use the authored emote rotation.");
        }
    }
}
