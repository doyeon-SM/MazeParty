using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class ArenaCombatHitFlashUiTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/UI/ArenaCombatHitFlash.prefab";
        [Test]
        public void AimView_HidesByDefaultAndRespondsToVisibilityUpdates()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);

            var instance = Object.Instantiate(prefab);
            try
            {
                var aimView = instance.GetComponent<ArenaCombatAimView>();
                Assert.That(aimView, Is.Not.Null);
                Assert.That(aimView.HasRequiredReferences, Is.True);
                Assert.That(aimView.IsVisible, Is.False);

                aimView.SetVisible(true);
                Assert.That(aimView.IsVisible, Is.True);

                aimView.SetVisible(false);
                Assert.That(aimView.IsVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

    }
}
