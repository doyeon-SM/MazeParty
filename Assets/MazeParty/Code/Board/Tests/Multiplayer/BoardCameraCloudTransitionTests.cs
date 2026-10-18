using System;
using System.Collections;
using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardCameraCloudTransitionTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

        [Test]
        public void BothDirections_HoldPoseUntilRenderedCover_CutUnderClouds_AndFinishAtSharedDeadline()
        {
            foreach (var target in new[] { GameplayMode.BoardTopDown, GameplayMode.FirstPerson })
            foreach (var yaw in new[] { 0f, 179f, 270f })
            {
                var root = new GameObject("Cloud transition contract");
                var cloud = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/MazeParty/Prefabs/Board/UI/BoardCameraCloudCanvas.prefab"));
                try
                {
                    GameObject Child(string name) { var child = new GameObject(name); child.transform.SetParent(root.transform); return child; }
                    var output = Child("Output").AddComponent<Camera>();
                    var first = Child("First").AddComponent<CinemachineCamera>();
                    var top = Child("Top").AddComponent<CinemachineCamera>();
                    var bridge = Child("Bridge").AddComponent<CinemachineCamera>();
                    var eye = Child("Eye").transform;
                    eye.position = new Vector3(4, 2, 3); eye.rotation = Quaternion.Euler(10, yaw, 0);
                    var director = root.AddComponent<GameplayCameraDirector>();
                    director.Configure(output, first, top, null); director.SetLocalPlayerEye(eye);
                    var view = cloud.GetComponent<BoardCameraCloudView>();
                    typeof(GameplayCameraDirector).GetField("cloudTransition", Private).SetValue(director, view);
                    typeof(GameplayCameraDirector).GetField("playerOverheadCamera", Private).SetValue(director, bridge);
                    output.transform.SetPositionAndRotation(new Vector3(0, 5, -3), Quaternion.Euler(23, yaw, 0));
                    var source = output.transform.rotation;
                    var position = output.transform.position;
                    double elapsed = 0;
                    var routine = (IEnumerator)typeof(GameplayCameraDirector).GetMethod("RunBoardTransition", Private)
                        .Invoke(director, new object[] { target, (Func<double>)(() => elapsed) });
                    Assert.That(routine.MoveNext(), Is.True);
                    elapsed = .699;
                    Assert.That(routine.MoveNext(), Is.True);
                    Assert.That(Quaternion.Angle(source, output.transform.rotation), Is.LessThan(.001f));
                    Assert.That(output.transform.position, Is.EqualTo(position));
                    elapsed = .7;
                    Assert.That(routine.MoveNext(), Is.True);
                    Assert.That(view.IsFullyCovered, Is.True);
                    Assert.That(Quaternion.Angle(source, output.transform.rotation), Is.LessThan(.001f),
                        "Full coverage must be presented for a frame before any rotation.");
                    Assert.That(routine.MoveNext(), Is.True);
                    Assert.That(view.IsFullyCovered, Is.True);
                    var destination = target == GameplayMode.BoardTopDown ? Quaternion.Euler(90, 0, 0) : eye.rotation;
                    Assert.That(Quaternion.Angle(destination, output.transform.rotation), Is.LessThan(.01f));
                    elapsed = 1.999;
                    Assert.That(routine.MoveNext(), Is.True);
                    elapsed = 2;
                    Assert.That(routine.MoveNext(), Is.False);
                    Assert.That(cloud.GetComponent<CanvasGroup>().alpha, Is.Zero);
                    Assert.That(cloud.GetComponent<CanvasGroup>().blocksRaycasts, Is.False);
                }
                finally { Object.DestroyImmediate(cloud); Object.DestroyImmediate(root); }
            }
        }

        [Test]
        public void RewardShot_IgnoresAnimatedHeadAndRewardHeight()
        {
            var root = new GameObject("Stable reward camera contract");
            var head = new GameObject("Animated head");
            try
            {
                var director = root.AddComponent<GameplayCameraDirector>();
                director.CalculateBoardResourceEventPose(root.transform, head.transform, 0,
                    out var position, out var rotation, out _);
                head.transform.position += Vector3.up * 2;
                director.CalculateBoardResourceEventPose(root.transform, head.transform, 4,
                    out var moved, out var rotated, out _);
                Assert.That(moved, Is.EqualTo(position));
                Assert.That(Quaternion.Angle(rotation, rotated), Is.LessThan(.001f));
                Assert.That(position.y, Is.LessThanOrEqualTo(PlayerAvatarVisual.StandingEyeHeight));
            }
            finally { Object.DestroyImmediate(head); Object.DestroyImmediate(root); }
        }
    }
}
