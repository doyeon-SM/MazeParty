using System.Linq;
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
        private const string SimpleFistHandPrefabPath =
            "Assets/MazeParty/Prefabs/Multiplayer/Player/SimpleFistHand.prefab";
        private const string SimpleHandsSourceRoot =
            "Assets/Ignore/SimpleHands/";
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
            Assert.That(bindings.BodyTintRenderers, Is.Not.Empty);
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
                prefab.GetComponentsInChildren<Canvas>(true),
                Is.Empty,
                "The shared marker must remain an authored world-space prefab.");
        }

        [Test]
        public void PlayerPresentationPrefab_UsesAuthoredSimpleFistHands()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                PlayerPresentationPrefabPath);
            Assert.That(prefab, Is.Not.Null, PlayerPresentationPrefabPath);
            var bindings = prefab.GetComponent<
                PlayerAvatarPresentationBindings>();
            Assert.That(bindings, Is.Not.Null);

            var fistPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                SimpleFistHandPrefabPath);
            Assert.That(fistPrefab, Is.Not.Null, SimpleFistHandPrefabPath);
            Assert.That(
                PrefabUtility.GetPrefabAssetType(fistPrefab),
                Is.EqualTo(PrefabAssetType.Variant),
                "The authored fist must remain a reusable WhiteHand prefab variant.");
            var fistRenderer = fistPrefab.GetComponentInChildren<
                SkinnedMeshRenderer>(true);
            Assert.That(fistRenderer, Is.Not.Null);
            var originalRenderer = PrefabUtility
                .GetCorrespondingObjectFromOriginalSource(fistRenderer);
            Assert.That(originalRenderer, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(originalRenderer),
                Does.StartWith(SimpleHandsSourceRoot),
                "The tracked fist variant must keep its SimpleHands source chain.");

            var handAnchors = new[]
            {
                bindings.LeftHandAnchor,
                bindings.RightHandAnchor,
                bindings.FirstPersonLeftHand,
                bindings.FirstPersonRightHand
            };
            foreach (var anchor in handAnchors)
            {
                Assert.That(anchor, Is.Not.Null);
                var renderers = anchor.GetComponentsInChildren<
                    SkinnedMeshRenderer>(true);
                Assert.That(renderers, Is.Not.Empty, anchor.name);
                Assert.That(
                    anchor.GetComponentsInChildren<MeshFilter>(true)
                        .Any(filter => filter.sharedMesh != null &&
                                       filter.sharedMesh.name == "Sphere"),
                    Is.False,
                    anchor.name + " must not retain its legacy sphere visual.");

                foreach (var renderer in renderers)
                {
                    Assert.That(
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                            renderer.gameObject),
                        Is.EqualTo(SimpleFistHandPrefabPath),
                        anchor.name + " must use the shared fist prefab.");
                    Assert.That(
                        bindings.BodyTintRenderers,
                        Does.Contain(renderer),
                        anchor.name + " must follow the player's body tint.");
                }
            }

            AssertFingerChainCurl(
                fistPrefab,
                "Thumb",
                20f,
                "Thumb",
                "Thumb2");
            AssertFingerChainCurl(
                fistPrefab,
                "Index",
                45f,
                "IndexFinger",
                "Index2",
                "Index3");
            AssertFingerChainCurl(
                fistPrefab,
                "Middle",
                45f,
                "MiddleFinger",
                "Middle2",
                "Middle3");
            AssertFingerChainCurl(
                fistPrefab,
                "Ring",
                45f,
                "RingFinger",
                "Ring2",
                "Ring3");
            AssertFingerChainCurl(
                fistPrefab,
                "Little",
                45f,
                "LittleFinger",
                "Little2",
                "Little3");
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

        private static void AssertFingerChainCurl(
            GameObject fistPrefab,
            string fingerName,
            float minimumTotalAngle,
            params string[] boneNames)
        {
            var transforms = fistPrefab.GetComponentsInChildren<Transform>(true);
            var totalAngle = 0f;
            foreach (var boneName in boneNames)
            {
                var bone = transforms.SingleOrDefault(
                    transform => transform.name == boneName);
                Assert.That(
                    bone,
                    Is.Not.Null,
                    fingerName + " bone is missing: " + boneName);
                var source = PrefabUtility
                    .GetCorrespondingObjectFromOriginalSource(bone);
                Assert.That(
                    source,
                    Is.Not.Null,
                    boneName + " has lost its SimpleHands source.");
                totalAngle += Quaternion.Angle(
                    source.localRotation,
                    bone.localRotation);
            }

            Assert.That(
                totalAngle,
                Is.GreaterThanOrEqualTo(minimumTotalAngle),
                fingerName +
                " must remain meaningfully curled from the source open-hand pose.");
        }
    }
}
