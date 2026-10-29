using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed partial class BoardChestAuthorityTests
    {
        [Test]
        public void FullMap_ContinuesObservingVisibleOpponents_ButRemembersOnlyLastSeenWhenHidden()
        {
            var ui = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Board/UI/BoardMapCanvas.prefab"), _root.transform);
            var view = ui.GetComponent<BoardMapView>();
            var camera = Child("Observation camera").AddComponent<Camera>();
            camera.enabled = false; camera.aspect = 1f;
            camera.transform.position = new Vector3(0, 1, -5);
            var director = Child("Camera director").AddComponent<GameplayCameraDirector>();
            typeof(GameplayCameraDirector).GetField("outputCamera", Private).SetValue(director, camera);
            typeof(GameplayCameraDirector).GetField("activeMode", Private).SetValue(director, GameplayMode.FirstPerson);
            typeof(BoardMapView).GetField("_cameraDirector", Private).SetValue(view, director);
            typeof(BoardMapView).GetField("_topology", Private).SetValue(view, _topology);
            typeof(BoardMapView).GetField("_fullMapOpen", Private).SetValue(view, true);
            var opponent = Child("Observed player").AddComponent<NetworkPlayerAvatar>();
            Authority(opponent, true); Set(opponent, "_slot", 1); Set(opponent, "_hasLogicalTile", true);
            Set(opponent, "_logicalTileCoordinate", Vector2Int.right);
            opponent.transform.position = new Vector3(0, 0, 5);
            var cache = (NetworkPlayerAvatar[])typeof(NetworkMatchState).GetField("_avatarLookupCache", Private).GetValue(_match);
            cache[0] = _player; cache[1] = opponent;
            var sample = typeof(BoardMapView).GetMethod("RefreshPlayerKnowledge", Private);
            var suspend = typeof(BoardMapView).GetMethod("SuspendPlayerObservation", Private);
            var knowledge = (BoardPlayerMapKnowledge)typeof(BoardMapView).GetField("_playerKnowledge", Private).GetValue(view);
            Physics.SyncTransforms();
            sample.Invoke(view, new object[] { _match, _player });
            Assert.That(knowledge.IsCurrentlyVisible(1), Is.True, "Opening the full map cannot stop direct observation.");
            Assert.That(knowledge.TryGetLastKnown(1, out var known), Is.True);
            Assert.That(known, Is.EqualTo(Vector2Int.right));
            var wall = Child("Opaque wall").AddComponent<BoxCollider>();
            wall.center = new Vector3(0, 1, 0); wall.size = new Vector3(4, 4, .5f);
            Set(opponent, "_logicalTileCoordinate", Vector2Int.right * 2);
            Physics.SyncTransforms();
            suspend.Invoke(view, null); sample.Invoke(view, new object[] { _match, _player });
            Assert.That(knowledge.IsCurrentlyVisible(1), Is.False);
            Assert.That(knowledge.TryGetLastKnown(1, out known), Is.True);
            Assert.That(known, Is.EqualTo(Vector2Int.right), "Hidden movement must not update either map.");
            wall.enabled = false; Physics.SyncTransforms();
            suspend.Invoke(view, null); sample.Invoke(view, new object[] { _match, _player });
            Assert.That(knowledge.IsCurrentlyVisible(1), Is.True);
            Assert.That(knowledge.TryGetLastKnown(1, out known), Is.True);
            Assert.That(known, Is.EqualTo(Vector2Int.right * 2));
        }
    }
}
