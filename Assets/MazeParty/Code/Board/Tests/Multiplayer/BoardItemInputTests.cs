using System.Reflection;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardItemInputTests
    {
        [Test]
        public void SniperPriority_OnlyActualInteractableSuppressesScope()
        {
            var player = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/MazeParty/Prefabs/Multiplayer/NetworkPlayer.prefab"));
            var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                player.transform.position = new Vector3(1000, 100, 1000);
                var avatar = player.GetComponent<NetworkPlayerAvatar>();
                obstacle.transform.position = player.transform.position + Vector3.up * .75f + Vector3.forward * 2;
                Physics.SyncTransforms();
                var priority = typeof(NetworkPlayerAvatar).GetMethod("HasAimedPriorityInteraction", BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.That((bool)priority.Invoke(avatar, null), Is.False, "Ordinary wall must not suppress scope.");
                obstacle.AddComponent<PlayerHitZoneOwner>();
                Assert.That((bool)priority.Invoke(avatar, null), Is.False, "A nearby player must not suppress scope.");
                obstacle.AddComponent<KeyShopWorldTarget>();
                Assert.That((bool)priority.Invoke(avatar, null), Is.True, "Shop interaction takes priority over scope.");
            }
            finally { Object.DestroyImmediate(obstacle); Object.DestroyImmediate(player); }
        }
    }
}
