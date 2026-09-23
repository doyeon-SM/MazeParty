using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardItemAssetContractTests
    {
        [Test]
        public void ItemDefinitions_AreUniqueAssets_WithEditablePresentationAndDeterministicShopSelection()
        {
            var expressions = PlayerExpressionCatalog.Instance;
            Assert.That(expressions, Is.Not.Null);
            Assert.That(expressions.Faces.Length, Is.EqualTo(4));
            Assert.That(expressions.Gestures.Length, Is.EqualTo(3));
            foreach (var face in expressions.Faces) Assert.That(face.Sprite, Is.Not.Null);
            foreach (var gesture in expressions.Gestures)
            {
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(gesture.HandsPrefab), Is.True);
                Assert.That(gesture.HandsPrefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            }
            var definitions = Resources.LoadAll<BoardItemDefinition>("MazeParty/Items");
            Assert.That(definitions.Select(x => x.Id).OrderBy(x => x), Is.EqualTo(new[] {
                PrototypeItemId.DoubleDice, PrototypeItemId.Pistol, PrototypeItemId.Sniper, PrototypeItemId.Grenade, PrototypeItemId.Mine, PrototypeItemId.LowDice, PrototypeItemId.HighDice, PrototypeItemId.PositionSwapper, PrototypeItemId.Cloak }));
            foreach (var definition in definitions)
            {
                Assert.That(definition.HeldPrefab, Is.Not.Null);
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(definition.HeldPrefab), Is.True);
                Assert.That(definition.WorldPrefab, Is.Not.Null);
                Assert.That(definition.HeldPrefab.GetComponentsInChildren<Collider>(true), Is.Empty,
                    "Presentation must not obstruct authoritative item casts.");
                Assert.That(PrototypeItemCatalog.Get(definition.Id), Is.SameAs(definition));
            }
            var first = new System.Random(901);
            var second = new System.Random(901);
            var found = new System.Collections.Generic.HashSet<PrototypeItemId>();
            for (int i = 0; i < 100; i++)
            {
                var id = PrototypeItemCatalog.GetRandomId(first);
                Assert.That(id, Is.EqualTo(PrototypeItemCatalog.GetRandomId(second)));
                found.Add(id);
            }
            Assert.That(found.Count, Is.EqualTo(9));
        }

    }
}
