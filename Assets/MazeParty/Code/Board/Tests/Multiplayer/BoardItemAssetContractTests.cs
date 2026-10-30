using System.Linq;
using System.Collections.Generic;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class BoardItemAssetContractTests
    {
        [Test]
        public void ItemDefinitions_KeepUniqueIdsValidReferencesAndDeterministicSelection()
        {
            var world = BoardWorldPrefabs.LoadRequired();
            Assert.That(world.Chest.HasRequiredReferences, Is.True);
            Assert.That(PrefabUtility.IsPartOfPrefabAsset(world.Chest), Is.True);
            Assert.That(world.Chest.AcquiredVfxPrefab, Is.Not.Null);
            var definitions = Resources.LoadAll<BoardItemDefinition>("MazeParty/Items");
            Assert.That(definitions.Select(item => item.Id).OrderBy(id => id), Is.EqualTo(new[] {
                PrototypeItemId.DoubleDice, PrototypeItemId.Pistol, PrototypeItemId.Sniper,
                PrototypeItemId.Grenade, PrototypeItemId.Mine, PrototypeItemId.HighDice,
                PrototypeItemId.PositionSwapper, PrototypeItemId.Cloak, PrototypeItemId.Bombardment,
                PrototypeItemId.GhostPass, PrototypeItemId.Wall, PrototypeItemId.TileAmplifier }));
            var heldItems = new HashSet<PrototypeItemId> { PrototypeItemId.Pistol, PrototypeItemId.Sniper,
                PrototypeItemId.Grenade, PrototypeItemId.Mine, PrototypeItemId.PositionSwapper,
                PrototypeItemId.Bombardment, PrototypeItemId.GhostPass, PrototypeItemId.Wall,
                PrototypeItemId.TileAmplifier };
            var icons = new HashSet<Sprite>();
            foreach (var item in definitions)
            {
                Assert.That(item.Icon, Is.Not.Null, item.Id.ToString());
                Assert.That(EditorUtility.IsPersistent(item.Icon), Is.True, item.Id.ToString());
                Assert.That(icons.Add(item.Icon), Is.True, item.Id + " requires a distinct icon.");
                Assert.That(PrototypeItemCatalog.Get(item.Id), Is.SameAs(item));
                if (!heldItems.Contains(item.Id))
                {
                    Assert.That(item.HeldPrefab, Is.Null, item.Id.ToString());
                    Assert.That(item.WorldPrefab, Is.Null, item.Id.ToString());
                    continue;
                }
                Assert.That(item.HeldPrefab, Is.Not.Null, item.Id.ToString());
                Assert.That(item.WorldPrefab, Is.Not.Null, item.Id.ToString());
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(item.HeldPrefab), Is.True);
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(item.WorldPrefab), Is.True);
                Assert.That(item.HeldPrefab.GetComponentsInChildren<Collider>(true), Is.Empty,
                    item.Id + " presentation must not obstruct authoritative casts.");
                if (item.Id == PrototypeItemId.Wall)
                {
                    Assert.That(item.PlacementPreviewPrefab, Is.Not.Null);
                    Assert.That(item.WorldPrefab.GetComponentsInChildren<Collider>(true)
                        .Any(collider => collider.enabled && !collider.isTrigger), Is.True);
                    Assert.That(item.PlacementPreviewPrefab.GetComponentsInChildren<Collider>(true)
                        .All(collider => !collider.enabled), Is.True);
                }
                if (item.Id == PrototypeItemId.Mine)
                {
                    Assert.That(item.WorldPrefab, Is.Not.SameAs(item.HeldPrefab));
                    var markers = item.WorldPrefab.GetComponentsInChildren<BoardWorldMineIcon>(true);
                    Assert.That(markers, Has.Length.EqualTo(1));
                    Assert.That(markers[0].HasRequiredReferences, Is.True);
                    Assert.That(item.WorldPrefab.GetComponentsInChildren<Collider>(true), Is.Empty);
                    Assert.That(item.WorldPrefab.GetComponentsInChildren<Collider2D>(true), Is.Empty);
                    Assert.That(item.WorldPrefab.GetComponentsInChildren<NetworkObject>(true), Is.Empty);
                }
            }
            var first = new System.Random(901);
            var second = new System.Random(901);
            var found = new HashSet<PrototypeItemId>();
            for (var index = 0; index < 100; index++)
            {
                var id = PrototypeItemCatalog.GetRandomId(first);
                Assert.That(id, Is.EqualTo(PrototypeItemCatalog.GetRandomId(second)));
                Assert.That(PrototypeItemCatalog.Get(id), Is.Not.Null);
                found.Add(id);
            }
            Assert.That(found.Count, Is.EqualTo(9));
        }

    }
}
