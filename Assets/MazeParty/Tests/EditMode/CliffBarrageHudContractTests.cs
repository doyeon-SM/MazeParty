using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class CliffBarrageHudContractTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/UI/Prefabs/CliffBarrageHud.prefab";

        [Test]
        public void ArchivedPrefab_PreservesTimerAndRoundBindingsForRecovery()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            var hud = prefab.GetComponent<CliffBarrageHudView>();
            Assert.That(hud, Is.Not.Null);
            Assert.That(hud.HasRequiredReferences, Is.True);
            Assert.That(hud.RootCanvas,
                Is.SameAs(prefab.GetComponent<Canvas>()));
            Assert.That(hud.TimerDial, Is.Not.Null);
            Assert.That(hud.RoundText, Is.Not.Null);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    hud.TimerDial.gameObject),
                Is.EqualTo(
                    "Assets/MazeParty/UI/Prefabs/MinigameTimerDial.prefab"));
        }

    }
}
