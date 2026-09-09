using System;
using System.Linq;
using System.Reflection;
using MazeParty.Gameplay;
using MazeParty.Gameplay.BoardFlowTestbed;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardFlowLocalSimulatorTests
    {
        private const BindingFlags PrivateInstance =
            BindingFlags.Instance | BindingFlags.NonPublic;
        private const string D12VisualPrefabPath =
            "Assets/MazeParty/Art/Dice/D12/Prefabs/D12WorldDieVisual.prefab";
        private const string TestbedScenePath =
            "Assets/MazeParty/Dev/BoardFlowTestbed/BoardFlowTestbed.unity";

        private GameObject _simulatorObject;
        private GameObject _worldDie;
        private GameObject _tileObject;

        [TearDown]
        public void TearDown()
        {
            DestroyImmediately(_worldDie);
            DestroyImmediately(_tileObject);
            DestroyImmediately(_simulatorObject);
            _worldDie = null;
            _tileObject = null;
            _simulatorObject = null;
        }

        [Test]
        public void EnsureLocalWorldDie_InstantiatesTrackedD12PrefabAndRuntimeMarkers()
        {
            var prefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(
                    D12VisualPrefabPath);
            Assert.That(prefab, Is.Not.Null);

            var simulator = CreateSimulator();
            Invoke(simulator, "EnsureLocalWorldDie");
            _worldDie = GetField<GameObject>(simulator, "_worldDie");

            Assert.That(_worldDie, Is.Not.Null);
            Assert.That(_worldDie.activeSelf, Is.False);
            Assert.That(_worldDie.GetComponent<NetworkWorldDie>(), Is.Null);
            Assert.That(
                _worldDie.GetComponent<MeshFilter>().sharedMesh,
                Is.SameAs(prefab.GetComponent<MeshFilter>().sharedMesh));
            Assert.That(
                _worldDie.GetComponent<MeshRenderer>().sharedMaterial,
                Is.SameAs(prefab.GetComponent<MeshRenderer>().sharedMaterial));

            var collider = _worldDie.GetComponent<MeshCollider>();
            Assert.That(collider, Is.Not.Null);
            Assert.That(collider.convex, Is.True);
            Assert.That(
                collider.sharedMesh,
                Is.SameAs(prefab.GetComponent<MeshCollider>().sharedMesh));

            var markers =
                _worldDie.GetComponentsInChildren<WorldDieFaceMarker>(true);
            Assert.That(markers, Has.Length.EqualTo(WorldDieD12Layout.FaceCount));
            Assert.That(
                markers.Select(marker => marker.Value).OrderBy(value => value),
                Is.EqualTo(Enumerable.Range(1, WorldDieD12Layout.FaceCount)));
            foreach (var marker in markers)
            {
                Assert.That(
                    WorldDieD12Layout.TryGetLocalNormal(
                        marker.Value,
                        out var expectedNormal),
                    Is.True);
                Assert.That(
                    Vector3.Dot(marker.LocalNormal, expectedNormal),
                    Is.GreaterThan(0.99999f));
                Assert.That(
                    marker.transform.localPosition.magnitude,
                    Is.EqualTo(WorldDieD12Layout.FaceMarkerRadius)
                        .Within(0.00001f));
            }

            var properties = new MaterialPropertyBlock();
            _worldDie.GetComponent<MeshRenderer>().GetPropertyBlock(properties);
            Assert.That(
                Vector4.Distance(
                    properties.GetColor("_BaseColor"),
                    new Color(0.95f, 0.25f, 0.25f)),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void TestbedScene_SerializesTrackedD12PrefabReference()
        {
            var scene = SceneManager.GetSceneByPath(TestbedScenePath);
            var wasLoaded = scene.IsValid() && scene.isLoaded;
            if (!wasLoaded)
            {
                scene = EditorSceneManager.OpenScene(
                    TestbedScenePath,
                    OpenSceneMode.Additive);
            }

            try
            {
                var simulator = scene.GetRootGameObjects()
                    .SelectMany(root =>
                        root.GetComponentsInChildren<
                            BoardFlowLocalSimulator>(true))
                    .Single();
                var serializedSimulator =
                    new SerializedObject(simulator);
                Assert.That(
                    serializedSimulator
                        .FindProperty("worldDieVisualPrefab")
                        .objectReferenceValue,
                    Is.SameAs(
                        AssetDatabase.LoadAssetAtPath<GameObject>(
                            D12VisualPrefabPath)));
                Assert.That(
                    serializedSimulator.FindProperty(
                        "worldDieResultVisibleSeconds"),
                    Is.Null,
                    "The regenerated testbed must not retain the legacy result timer.");
            }
            finally
            {
                if (!wasLoaded && scene.IsValid() && scene.isLoaded)
                {
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [Test]
        public void LocalRoll_DefersMovementUntilLandingAndUsesTileSurfaceHeight()
        {
            var simulator = CreateSimulator();
            Invoke(simulator, "EnsureLocalWorldDie");
            _worldDie = GetField<GameObject>(simulator, "_worldDie");
            var body = GetField<Rigidbody>(simulator, "_worldDieBody");
            var result = GetField<TextMesh>(simulator, "_worldDieResult");

            var tile = CreateTile();
            SetField(simulator, "_worldDieTile", tile);
            _worldDie.transform.SetPositionAndRotation(
                new Vector3(0f, 1.5f, 0f),
                Quaternion.Euler(17f, 83f, 241f));
            _worldDie.SetActive(true);
            body.detectCollisions = true;
            Physics.SyncTransforms();

            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Log,
                "[Board Flow Testbed] Rolling the local D12. " +
                "Movement commits after its one-second landing.");
            var began = (bool)Invoke(
                simulator,
                "BeginLocalWorldDieRoll",
                new Ray(new Vector3(0f, 1.5f, -2f), Vector3.forward));
            Assert.That(began, Is.True);
            var pendingFace = GetField<int>(simulator, "_pendingWorldDieFace");
            Assert.That(pendingFace, Is.InRange(1, 12));
            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Log,
                "[Board Flow Testbed] Rolled " + pendingFace +
                ". Blue walls are passable; black walls physically block entry.");
            Assert.That(GetField<int>(simulator, "_roll"), Is.Zero);
            Assert.That(GetField<int>(simulator, "_remainingMoves"), Is.Zero);
            Assert.That(result.text, Is.EqualTo("?"));

            SetField(
                simulator,
                "_worldDieRollStartedAt",
                Time.unscaledTimeAsDouble -
                WorldDieRollPresentationPolicy.PhysicalTumbleSeconds -
                0.01d);
            Invoke(simulator, "UpdateLocalWorldDieRollPresentation");
            Assert.That(GetField<int>(simulator, "_roll"), Is.Zero);
            Assert.That(
                GetField<WorldDieRollPresentationPhase>(
                    simulator,
                    "_worldDieRollPresentationPhase"),
                Is.EqualTo(WorldDieRollPresentationPhase.Landing));

            var landingTarget =
                GetField<Vector3>(
                    simulator,
                    "_worldDieLandingTargetPosition");
            var selectedMarker =
                _worldDie.GetComponentsInChildren<WorldDieFaceMarker>(true)
                    .Single(marker => marker.Value == pendingFace);
            var supportDistance = Mathf.Abs(Vector3.Dot(
                selectedMarker.transform.position -
                _worldDie.transform.position,
                selectedMarker.transform.up.normalized));
            var tileCollider = _tileObject.GetComponent<Collider>();
            var surfacePoint = tileCollider.ClosestPoint(
                landingTarget + Vector3.up * BoardTile.RoomSize);
            var expectedHeight =
                WorldDieRollPresentationPolicy.GetLandingCenterHeight(
                    surfacePoint.y,
                    supportDistance,
                    0.01f);
            Assert.That(
                landingTarget.y,
                Is.EqualTo(expectedHeight).Within(0.0001f));

            SetField(
                simulator,
                "_worldDieRollStartedAt",
                Time.unscaledTimeAsDouble -
                WorldDieRollPresentationPolicy.TotalSeconds -
                0.01d);
            Invoke(simulator, "UpdateLocalWorldDieRollPresentation");

            Assert.That(GetField<int>(simulator, "_roll"), Is.EqualTo(pendingFace));
            Assert.That(
                GetField<int>(simulator, "_remainingMoves"),
                Is.EqualTo(pendingFace));
            Assert.That(result.text, Is.EqualTo(pendingFace.ToString()));
            Assert.That(
                GetField<double>(simulator, "_worldDieHideDeadline"),
                Is.GreaterThanOrEqualTo(
                    Time.unscaledTimeAsDouble +
                    WorldDieResultPresentationPolicy.DefaultVisibleSeconds -
                    0.1d));
            UnityEngine.TestTools.LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Pause_PreservesDeadlinesAndRestoresTumblingVelocity()
        {
            var simulator = CreateSimulator();
            Invoke(simulator, "EnsureLocalWorldDie");
            _worldDie = GetField<GameObject>(simulator, "_worldDie");
            var body = GetField<Rigidbody>(simulator, "_worldDieBody");
            _worldDie.SetActive(true);
            SetField(
                simulator,
                "_worldDieRollPresentationPhase",
                WorldDieRollPresentationPhase.Tumbling);
            SetField(simulator, "_worldDieRollStartedAt", 10d);
            SetField(simulator, "_worldDieHideDeadline", 12d);
            SetField(simulator, "_worldDieNudgeDeadline", 11.5d);
            SetField(simulator, "_worldDieNudgeBelowThresholdSince", -1d);

            body.isKinematic = false;
            body.linearVelocity = new Vector3(1f, 2f, 3f);
            body.angularVelocity = new Vector3(4f, 5f, 6f);
            Invoke(simulator, "SetLocalWorldDiePhysicsSuspended", true);
            Assert.That(body.isKinematic, Is.True);

            Invoke(
                simulator,
                "PreserveLocalWorldDieTimersDuringPause",
                true,
                2.5d);
            Assert.That(
                GetField<double>(simulator, "_worldDieRollStartedAt"),
                Is.EqualTo(12.5d));
            Assert.That(
                GetField<double>(simulator, "_worldDieHideDeadline"),
                Is.EqualTo(14.5d));
            Assert.That(
                GetField<double>(simulator, "_worldDieNudgeDeadline"),
                Is.EqualTo(14d));
            Assert.That(
                GetField<double>(simulator, "_worldDieNudgeBelowThresholdSince"),
                Is.EqualTo(-1d));

            Invoke(simulator, "SetLocalWorldDiePhysicsSuspended", false);
            Assert.That(body.isKinematic, Is.False);
            Assert.That(
                Vector3.Distance(
                    body.linearVelocity,
                    new Vector3(1f, 2f, 3f)),
                Is.LessThan(0.0001f));
            Assert.That(
                Vector3.Distance(
                    body.angularVelocity,
                    new Vector3(4f, 5f, 6f)),
                Is.LessThan(0.0001f));
        }

        [Test]
        public void DiceHud_HidesSettledNumberWhenOneSecondPublicWindowEnds()
        {
            var simulator = CreateSimulator();
            Invoke(simulator, "EnsureLocalWorldDie");
            _worldDie = GetField<GameObject>(simulator, "_worldDie");
            _worldDie.SetActive(true);
            SetField(simulator, "_roll", 12);
            SetField(
                simulator,
                "_worldDieRollPresentationPhase",
                WorldDieRollPresentationPhase.None);
            SetField(
                simulator,
                "_worldDieHideDeadline",
                Time.unscaledTimeAsDouble +
                WorldDieResultPresentationPolicy.DefaultVisibleSeconds);

            Assert.That(
                Invoke(simulator, "ResolveLocalWorldDieHudLabel"),
                Is.EqualTo("DICE  12"));

            SetField(
                simulator,
                "_worldDieHideDeadline",
                0d);
            Assert.That(
                Invoke(simulator, "ResolveLocalWorldDieHudLabel"),
                Is.EqualTo("DICE  ROLL COMPLETE"));

            Invoke(simulator, "UpdateLocalWorldDieLifetime");
            Assert.That(_worldDie.activeSelf, Is.False);
            Assert.That(
                Invoke(simulator, "ResolveLocalWorldDieHudLabel"),
                Is.EqualTo("DICE  ROLL COMPLETE"));
        }

        private BoardFlowLocalSimulator CreateSimulator()
        {
            _simulatorObject =
                new GameObject("BoardFlow Local Simulator Test");
            return _simulatorObject.AddComponent<BoardFlowLocalSimulator>();
        }

        private BoardTile CreateTile()
        {
            _tileObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _tileObject.name = "Local Die Landing Tile";
            _tileObject.transform.position = Vector3.zero;
            _tileObject.transform.localScale =
                new Vector3(7.72f, 0.2f, 7.72f);
            var tile = _tileObject.AddComponent<BoardTile>();
            tile.Configure(Vector2Int.zero, BoardTileType.Normal);
            Physics.SyncTransforms();
            return tile;
        }

        private static object Invoke(
            object target,
            string methodName,
            params object[] arguments)
        {
            var method = target.GetType().GetMethod(
                methodName,
                PrivateInstance);
            Assert.That(method, Is.Not.Null, methodName);
            try
            {
                return method.Invoke(target, arguments);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException ?? exception;
            }
        }

        private static T GetField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null, fieldName);
            return (T)field.GetValue(target);
        }

        private static void SetField<T>(
            object target,
            string fieldName,
            T value)
        {
            var field = target.GetType().GetField(
                fieldName,
                PrivateInstance);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }

        private static void DestroyImmediately(UnityEngine.Object target)
        {
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target);
            }
        }
    }
}
