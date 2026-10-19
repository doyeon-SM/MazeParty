using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using MazeParty.Gameplay.Minigames.GiftGrab;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class GiftGrabSceneContractTests
    {
        private const string ScenePath =
            "Assets/MazeParty/Scenes/Minigames/GiftGrab/GiftGrab.unity";

        [Test]
        public void GiftVariants_UseSharedEventIdentityAndCompleteAuthoredReferences()
        {
            foreach (var path in new[] {
                "Assets/MazeParty/Prefabs/Minigames/GiftGrab/Gift.prefab",
                "Assets/MazeParty/Prefabs/Board/World/BoardLandingGift.prefab" })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(prefab, Is.Not.Null, path);
                var first = Object.Instantiate(prefab);
                var second = Object.Instantiate(prefab);
                try
                {
                    var a = first.GetComponent<GiftVisualVariants>();
                    var b = second.GetComponent<GiftVisualVariants>();
                    Assert.That(a.HasRequiredReferences, Is.True, path);
                    Assert.That(b.HasRequiredReferences, Is.True, path);
                    var selected = new System.Collections.Generic.HashSet<int>();
                    for (var gift = 0; gift < 32; gift++)
                    {
                        a.Present(0x123456789ABCDEF0UL, 7, gift);
                        b.Present(1UL, -1, gift + 10);
                        b.Present(0x123456789ABCDEF0UL, 7, gift);
                        var expected = GiftVisualVariants.SelectIndex(0x123456789ABCDEF0UL, 7, gift);
                        selected.Add(expected);
                        Assert.That(a.Variants.Count(value => value.activeSelf), Is.EqualTo(1));
                        Assert.That(b.Variants.Count(value => value.activeSelf), Is.EqualTo(1));
                        Assert.That(a.Variants[expected].activeSelf, Is.True);
                        Assert.That(b.Variants[expected].activeSelf, Is.True,
                            "A late-joining presentation must choose the same model from the shared event.");
                    }
                    Assert.That(selected, Is.EquivalentTo(new[] { 0, 1, 2 }));
                }
                finally { Object.DestroyImmediate(first); Object.DestroyImmediate(second); }
            }
        }

        [Test]
        public void ThrownGiftBoundary_UsesRadiusAdjustedExactContact()
        {
            var contactX = NetworkGiftGrabState.ArenaCenterX +
                           NetworkGiftGrabState.ArenaHalfExtent -
                           NetworkGiftGrabState.GiftCollisionRadius;
            var start = new Vector2(contactX - 1f, 0f);

            Assert.That(
                NetworkGiftGrabState.TryGetGiftArenaBoundaryImpact(
                    start,
                    new Vector2(contactX - 0.001f, 0f),
                    out _),
                Is.False,
                "A gift whose edge has not touched the wall must remain active.");
            Assert.That(
                NetworkGiftGrabState.TryGetGiftArenaBoundaryImpact(
                    start,
                    new Vector2(contactX, 0f),
                    out var exactImpact),
                Is.True,
                "The gift must land when its edge exactly touches the wall.");
            Assert.That(exactImpact.x,
                Is.EqualTo(contactX).Within(0.0001f));
            Assert.That(exactImpact.y, Is.Zero.Within(0.0001f));

            Assert.That(
                NetworkGiftGrabState.TryGetGiftArenaBoundaryImpact(
                    new Vector2(contactX, 0f),
                    new Vector2(contactX - 0.25f, 0f),
                    out _),
                Is.False,
                "A gift moving inward from the wall must not land again.");
        }

        [Test]
        public void ActionPresentationRpc_IsReliablePerEvent()
        {
            var rpc = typeof(NetworkGiftGrabState).GetMethod(
                "PlayActionPresentationRpc",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(rpc, Is.Not.Null);
            Assert.That(rpc.GetCustomAttribute<RpcAttribute>()?.Delivery,
                Is.EqualTo(RpcDelivery.Reliable));
            var parameters = rpc.GetParameters();
            Assert.That(parameters, Has.Length.EqualTo(4));
            Assert.That(parameters[0].ParameterType, Is.EqualTo(typeof(byte)));
            Assert.That(parameters[1].ParameterType, Is.EqualTo(typeof(byte)));
            Assert.That(parameters[2].ParameterType, Is.EqualTo(typeof(byte)));
            Assert.That(parameters[3].ParameterType,
                Is.EqualTo(typeof(Vector2)));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ServerSimulation_ActionLockStopsMovementThroughExactBoundary(
            bool throwRecovery)
        {
            var round = new GiftGrabRoundState(1);
            var thrower = throwRecovery ? 0 : 1;
            Assert.That(round.ResolvePickup(thrower, 0, 0d).WasPickedUp,
                Is.True);
            Assert.That(round.ResolveThrow(thrower, 0d).WasThrown, Is.True);
            if (!throwRecovery)
            {
                Assert.That(round.ResolveThrownHit(0, 0, 0d).WasHit,
                    Is.True);
            }

            var root = new GameObject("GiftGrab Action Lock Boundary Test");
            try
            {
                var state = root.AddComponent<NetworkGiftGrabState>();
                NetworkCountdownTestAccess.SetPrivateField(
                    state,
                    "_roundState",
                    round);

                var positions = GetPrivateField<Vector2[]>(
                    state,
                    "_playerPositions");
                positions[0] = new Vector2(
                    NetworkGiftGrabState.ArenaCenterX,
                    0f);
                positions[1] = new Vector2(
                    NetworkGiftGrabState.ArenaCenterX + 6f,
                    6f);
                positions[2] = new Vector2(
                    NetworkGiftGrabState.ArenaCenterX - 6f,
                    6f);
                positions[3] = new Vector2(
                    NetworkGiftGrabState.ArenaCenterX + 6f,
                    -6f);
                var start = positions[0];
                if (throwRecovery)
                {
                    // Keep the projectile away from all players so this
                    // test isolates the thrower's movement deadline.
                    GetPrivateField<Vector2[]>(state, "_giftPositions")[0] =
                        new Vector2(
                            NetworkGiftGrabState.ArenaCenterX + 2f,
                            2f);
                    GetPrivateField<double[]>(state, "_thrownEndsAt")[0] =
                        1d;
                }

                GetPrivateField<Vector2[]>(state, "_serverInputs")[0] =
                    Vector2.right;
                var initialized = GetPrivateField<bool[]>(
                    state,
                    "_giftPositionInitialized");
                for (var index = 0; index < initialized.Length; index++)
                {
                    initialized[index] = true;
                }

                LogAssert.Expect(
                    LogType.Warning,
                    "NetworkVariable is written to, but doesn't know its " +
                    "NetworkBehaviour yet. Are you modifying a " +
                    "NetworkVariable before the NetworkObject is spawned?");
                NetworkCountdownTestAccess.InvokePrivate(
                    state,
                    "SimulateToOnServer",
                    0.499d);
                Assert.That(positions[0], Is.EqualTo(start));

                NetworkCountdownTestAccess.InvokePrivate(
                    state,
                    "SimulateToOnServer",
                    throwRecovery
                        ? GiftGrabRules.ThrowRecoverySeconds
                        : GiftGrabRules.ThrowStunSeconds);
                Assert.That(
                    positions[0],
                    Is.EqualTo(start),
                    "The segment ending exactly at the action boundary " +
                    "must remain locked.");

                NetworkCountdownTestAccess.InvokePrivate(
                    state,
                    "SimulateToOnServer",
                    (throwRecovery
                        ? GiftGrabRules.ThrowRecoverySeconds
                        : GiftGrabRules.ThrowStunSeconds) + 0.01d);
                Assert.That(
                    positions[0].x,
                    Is.EqualTo(
                        start.x + GiftGrabRules.NormalMoveSpeed * 0.01f)
                        .Within(0.0001f));
                Assert.That(positions[0].y,
                    Is.EqualTo(start.y).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Scene_PreservesArenaAndPhysicalGiftContract()
        {
            var scene = SceneManager.GetSceneByPath(ScenePath);
            var openedForTest = !scene.IsValid() || !scene.isLoaded;
            if (openedForTest)
            {
                scene = EditorSceneManager.OpenScene(
                    ScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var roots = scene.GetRootGameObjects();
                var state = roots
                    .SelectMany(root => root.GetComponentsInChildren<
                        NetworkGiftGrabState>(true))
                    .SingleOrDefault();
                Assert.That(state, Is.Not.Null);
                var networkObject = state.GetComponent<NetworkObject>();
                Assert.That(networkObject, Is.Not.Null);
                Assert.That(networkObject.InScenePlaced, Is.True);
                Assert.That(networkObject.PrefabIdHash, Is.Not.EqualTo(0u));

                var view = state.GetComponent<GiftGrabNetworkView>();
                Assert.That(view, Is.Not.Null);
                var serialized = new SerializedObject(view);
                foreach (var propertyName in new[]
                         {
                             "state", "sharedCamera", "playerRoot",
                             "giftRoot", "pushVfxAnchor", "throwVfxAnchor",
                             "dropVfxAnchor", "arenaPresentation",
                              "cueAudioSource"
                         })
                {
                    var property = serialized.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.objectReferenceValue,
                        Is.Not.Null,
                        propertyName);
                }
                foreach (var propertyName in new[]
                         {
                              "playerAnchors", "baseAnchors"
                         })
                {
                    var property = serialized.FindProperty(propertyName);
                    Assert.That(property, Is.Not.Null, propertyName);
                    Assert.That(
                        property.arraySize,
                        Is.EqualTo(GiftGrabRules.PlayerCount),
                        propertyName);
                    for (var index = 0; index < property.arraySize; index++)
                    {
                        Assert.That(
                            property.GetArrayElementAtIndex(index)
                                .objectReferenceValue,
                            Is.Not.Null,
                            propertyName + "[" + index + "]");
                    }
                }

                var playerAnchors = FindDescendant(
                    state.transform,
                    "Player Anchors");
                var baseAnchors = FindDescendant(
                    state.transform,
                    "Base Anchors");
                var giftAnchors = FindDescendant(
                    state.transform,
                    "Gift Anchors");
                Assert.That(playerAnchors, Is.Not.Null);
                Assert.That(baseAnchors, Is.Not.Null);
                Assert.That(giftAnchors, Is.Not.Null);
                Assert.That(
                    playerAnchors.childCount,
                    Is.EqualTo(GiftGrabRules.PlayerCount));
                Assert.That(
                    baseAnchors.childCount,
                    Is.EqualTo(GiftGrabRules.PlayerCount));
                Assert.That(
                    giftAnchors.childCount,
                    Is.EqualTo(GiftGrabRules.TotalGiftCount));
                for (var slot = 0; slot < GiftGrabRules.PlayerCount; slot++)
                {
                    var baseAnchor = baseAnchors.Find(
                        "Base Anchor " + (slot + 1));
                    var playerAnchor = playerAnchors.Find(
                        "Player Anchor " + (slot + 1));
                    Assert.That(baseAnchor, Is.Not.Null);
                    Assert.That(playerAnchor, Is.Not.Null);
                    var expectedBase = NetworkGiftGrabState.GetBaseCenter(slot);
                    var expectedStart =
                        NetworkGiftGrabState.GetPlayerStartPosition(slot);
                    Assert.That(baseAnchor.position.x,
                        Is.EqualTo(expectedBase.x).Within(0.001f));
                    Assert.That(baseAnchor.position.z,
                        Is.EqualTo(expectedBase.y).Within(0.001f));
                    Assert.That(playerAnchor.position.x,
                        Is.EqualTo(expectedStart.x).Within(0.001f));
                    Assert.That(playerAnchor.position.z,
                        Is.EqualTo(expectedStart.y).Within(0.001f));
                }
                for (var giftId = 0;
                     giftId < GiftGrabRules.TotalGiftCount;
                     giftId++)
                {
                    var gift = giftAnchors.Find(
                        "Gift Anchor " + giftId.ToString("00"));
                    Assert.That(gift, Is.Not.Null, giftId.ToString());
                    Assert.That(gift.Find("Gift Visual"), Is.Not.Null);
                    var variants = gift.Find("Gift Visual").GetComponent<GiftVisualVariants>();
                    Assert.That(variants, Is.Not.Null);
                    Assert.That(variants.HasRequiredReferences, Is.True);
                    Assert.That(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                        variants.gameObject), Is.EqualTo(
                        "Assets/MazeParty/Prefabs/Minigames/GiftGrab/Gift.prefab"));
                }

                Assert.That(
                    state.GetComponentsInChildren<Canvas>(true),
                    Is.Empty,
                    "Stored gifts are shown as physical gifts, not a score Canvas.");
                Assert.That(FindDescendant(state.transform,
                    "Player Label Anchors"), Is.Null);
                Assert.That(FindDescendant(state.transform,
                    "Base Label Anchors"), Is.Null);
                Assert.That(FindDescendant(state.transform,
                    "Deposited Gift Display Anchors"), Is.Null);

                var cameras = state.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null &&
                        component.GetType().FullName ==
                        "Unity.Cinemachine.CinemachineCamera")
                    .ToArray();
                Assert.That(cameras, Has.Length.EqualTo(1));
                Assert.That(cameras[0].name, Is.EqualTo(
                    "CM_GiftGrabShared"));
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<Camera>(true)),
                    Is.Empty,
                    "The additive scene must reuse Board's output Camera.");
                Assert.That(
                    roots.SelectMany(root =>
                        root.GetComponentsInChildren<AudioListener>(true)),
                    Is.Empty);
                Assert.That(
                    FindDescendant(state.transform, "Art Replacement Anchors"),
                    Is.Not.Null);
                Assert.That(
                    FindDescendant(state.transform, "VFX Replacement Anchors"),
                    Is.Not.Null);
            }
            finally
            {
                if (openedForTest && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static Transform FindDescendant(
            Transform root,
            string childName)
        {
            if (root == null)
            {
                return null;
            }
            if (root.name == childName)
            {
                return root;
            }
            for (var index = 0; index < root.childCount; index++)
            {
                var result = FindDescendant(root.GetChild(index), childName);
                if (result != null)
                {
                    return result;
                }
            }
            return null;
        }

        private static T GetPrivateField<T>(
            object target,
            string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(target);
        }
    }
}
