using System.Linq;
using System.Collections.Generic;
using MazeParty.Gameplay;
using NUnit.Framework;
using Unity.Netcode;
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
            Assert.That(
                expressions.Faces.Select(face => face.Name),
                Is.EqualTo(Enumerable.Range(1, 15).Select(index => "Face" + index)));
            Assert.That(
                expressions.Faces.Select(face => AssetDatabase.GetAssetPath(face.Sprite)),
                Is.EqualTo(Enumerable.Range(1, 15).Select(index =>
                    "Assets/Ignore/Pack_PartyCharacters/Resources/Materials/Face Images/face " +
                    index + ".png")));
            var expectedHatFiles = new[]
            {
                "chef hat", "orange fedora", "party hat", "alien", "angle hole",
                "bandage", "bonus", "clown", "cowboy hat", "egg", "fez",
                "fireman hat", "goat horns", "hair", "hat", "headphone",
                "heart antenna", "horn", "king crown", "mushroom hat", "noel hat",
                "party crown", "pineapple", "pump", "soldier hat", "sombrero",
                "top hat", "traffic cone", "viking helmet", "witch hat"
            };
            Assert.That(
                expressions.Hats.Select(hat => hat.Name),
                Is.EqualTo(Enumerable.Range(1, 30).Select(index => "Hat" + index)));
            Assert.That(
                expressions.Hats.Select(hat => AssetDatabase.GetAssetPath(hat.Prefab)),
                Is.EqualTo(expectedHatFiles.Select(file =>
                    "Assets/Ignore/Pack_PartyCharacters/Resources/Prefabs/Hats/" +
                    file + ".prefab")));
            Assert.That(expressions.Gestures.Length, Is.EqualTo(3));
            foreach (var face in expressions.Faces) Assert.That(face.Sprite, Is.Not.Null);
            foreach (var hat in expressions.Hats)
            {
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(hat.Prefab), Is.True);
                Assert.That(hat.LocalScale.sqrMagnitude, Is.GreaterThan(0f));
            }
            foreach (var gesture in expressions.Gestures)
            {
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(gesture.HandsPrefab), Is.True);
                Assert.That(gesture.HandsPrefab.GetComponentsInChildren<Collider>(true), Is.Empty);
            }
            var definitions = Resources.LoadAll<BoardItemDefinition>("MazeParty/Items");
            Assert.That(definitions.Select(x => x.Id).OrderBy(x => x), Is.EqualTo(new[] {
                PrototypeItemId.DoubleDice, PrototypeItemId.Pistol, PrototypeItemId.Sniper, PrototypeItemId.Grenade, PrototypeItemId.Mine, PrototypeItemId.LowDice, PrototypeItemId.HighDice, PrototypeItemId.PositionSwapper, PrototypeItemId.Cloak }));
            var expectedWeaponModels = new Dictionary<PrototypeItemId, string>
            {
                {
                    PrototypeItemId.Pistol,
                    "Assets/Ignore/nappin/WeaponStylizedPack/Models/(Msh)Revolver.fbx"
                },
                {
                    PrototypeItemId.Sniper,
                    "Assets/Ignore/nappin/WeaponStylizedPack/Models/(Msh)HuntingRifle.fbx"
                },
                {
                    PrototypeItemId.Mine,
                    "Assets/Ignore/nappin/WeaponStylizedPack/Models/(Msh)Dynamite.fbx"
                },
                {
                    PrototypeItemId.Grenade,
                    "Assets/Ignore/nappin/WeaponStylizedPack/Models/(Msh)Granade.fbx"
                }
            };
            var itemIcons = new HashSet<Sprite>();
            foreach (var definition in definitions)
            {
                Assert.That(definition.Icon, Is.Not.Null, definition.Id.ToString());
                Assert.That(
                    AssetDatabase.GetAssetPath(definition.Icon),
                    Is.EqualTo(
                        "Assets/Ignore/AIImage/Icons/" + definition.Id + ".png"),
                    definition.Id.ToString());
                Assert.That(itemIcons.Add(definition.Icon), Is.True,
                    definition.Id + " must use a unique icon sprite.");
                Assert.That(PrototypeItemCatalog.Get(definition.Id), Is.SameAs(definition));

                if (!expectedWeaponModels.TryGetValue(definition.Id, out var expectedModelPath))
                {
                    Assert.That(definition.HeldPrefab, Is.Null,
                        definition.Id + " uses its gameplay effect instead of a held model.");
                    Assert.That(definition.WorldPrefab, Is.Null,
                        definition.Id + " must not create a placeholder world model.");
                    continue;
                }

                Assert.That(definition.HeldPrefab, Is.Not.Null);
                Assert.That(PrefabUtility.IsPartOfPrefabAsset(definition.HeldPrefab), Is.True);
                Assert.That(definition.WorldPrefab, Is.Not.Null);
                Assert.That(definition.HeldPrefab.GetComponentsInChildren<Collider>(true), Is.Empty,
                    "Presentation must not obstruct authoritative item casts.");

                var meshPaths = definition.HeldPrefab
                    .GetComponentsInChildren<MeshFilter>(true)
                    .Select(filter => filter.sharedMesh)
                    .Concat(definition.HeldPrefab
                        .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Select(renderer => renderer.sharedMesh))
                    .Where(mesh => mesh != null)
                    .Select(AssetDatabase.GetAssetPath)
                    .Distinct()
                    .ToArray();
                Assert.That(meshPaths, Does.Contain(expectedModelPath),
                    definition.Id + " must use its approved WeaponStylizedPack model.");
            }
            Assert.That(itemIcons.Count, Is.EqualTo(9));
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

        [Test]
        public void MinePresentation_KeepsDynamiteInHand_AndUsesRedHelpIconInWorld()
        {
            const string heldPrefabPath =
                "Assets/MazeParty/Prefabs/Board/Items/Mine.prefab";
            const string dynamiteModelPath =
                "Assets/Ignore/nappin/WeaponStylizedPack/Models/(Msh)Dynamite.fbx";
            const string worldPrefabPath =
                "Assets/MazeParty/Prefabs/Board/Items/MineWorldIcon.prefab";
            const string helpFilledPath =
                "Assets/Ignore/Modern UI Pack/Textures/Icon/Navigation/Help Filled.png";

            var mine = Resources.LoadAll<BoardItemDefinition>(
                    "MazeParty/Items")
                .Single(definition =>
                    definition.Id == PrototypeItemId.Mine);

            Assert.That(mine.HeldPrefab, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(mine.HeldPrefab),
                Is.EqualTo(heldPrefabPath));
            var heldMeshPaths = mine.HeldPrefab
                .GetComponentsInChildren<MeshFilter>(true)
                .Select(filter => filter.sharedMesh)
                .Concat(mine.HeldPrefab
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Select(renderer => renderer.sharedMesh))
                .Where(mesh => mesh != null)
                .Select(AssetDatabase.GetAssetPath)
                .Distinct()
                .ToArray();
            Assert.That(heldMeshPaths, Does.Contain(dynamiteModelPath));

            Assert.That(mine.WorldPrefab, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(mine.WorldPrefab),
                Is.EqualTo(worldPrefabPath));
            Assert.That(mine.WorldPrefab, Is.Not.SameAs(mine.HeldPrefab));

            var markers = mine.WorldPrefab
                .GetComponentsInChildren<BoardWorldMineIcon>(true);
            Assert.That(markers, Has.Length.EqualTo(1));
            var marker = markers[0];
            var renderers = mine.WorldPrefab
                .GetComponentsInChildren<SpriteRenderer>(true);
            Assert.That(renderers, Has.Length.EqualTo(1));
            var renderer = marker.IconRenderer;
            Assert.That(marker.HasRequiredReferences, Is.True);
            Assert.That(renderer, Is.SameAs(renderers[0]));
            Assert.That(
                AssetDatabase.GetAssetPath(renderer.sprite),
                Is.EqualTo(helpFilledPath));
            Assert.That(renderer.color.r, Is.EqualTo(1f).Within(0.001f));
            Assert.That(renderer.color.g, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(renderer.color.b, Is.EqualTo(0.18f).Within(0.001f));
            Assert.That(renderer.color.a, Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                mine.WorldPrefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                mine.WorldPrefab.GetComponentsInChildren<Collider2D>(true),
                Is.Empty);
            Assert.That(
                mine.WorldPrefab.GetComponentsInChildren<NetworkObject>(true),
                Is.Empty);
        }

    }
}
