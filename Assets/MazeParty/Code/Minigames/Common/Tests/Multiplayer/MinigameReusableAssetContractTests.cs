using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MazeParty.Multiplayer.Tests
{
    public sealed class MinigameReusableAssetContractTests
    {
        private const string CommonFolder =
            "Assets/MazeParty/Prefabs/Minigames/Common/Environment/";
        private const string FinishGatePath =
            CommonFolder + "SharedFinishGate.prefab";
        private const string OutdoorFencePath =
            CommonFolder + "SharedOutdoorFence.prefab";
        private const string GroundTilePath =
            CommonFolder + "SharedNatureGroundTile.prefab";
        private const string TripleSwitchPath =
            CommonFolder + "SharedTripleSwitch.prefab";
        private const string SciFiBlockPath =
            CommonFolder + "SharedSciFiBlock.prefab";
        private const string FantasyCliffPath =
            CommonFolder + "SharedFantasyCliff.prefab";
        private const string GeneratedMeshFolder =
            "Assets/Ignore/MazePartyGenerated/Meshes/";
        private const string CenteredGroundMeshPath =
            GeneratedMeshFolder + "SharedNatureGroundCentered.asset";
        private const string BasicBlockFolder =
            "Assets/Ignore/ToyBox/Prefabs/Blocks/BasicBlock/";
        private const string PlayerPresentationPath =
            "Assets/MazeParty/Prefabs/Multiplayer/PlayerAvatarPresentation.prefab";

        private const string GateSourcePath =
            "Assets/Ignore/Polytope Studio/Lowpoly_Village/Prefabs/Modular/Fence/PT_Modular_Gate_Wood_01.prefab";
        private const string FenceSourcePath =
            "Assets/Ignore/Fantasy Lowpoly Pack (Demo)/Prefabs/fence.prefab";
        private const string GroundSourcePath =
            "Assets/Ignore/Pandazole_Ultimate_Pack/Pandazole Nature Environment Pack/Prefabs/TileGround_01.prefab";
        private const string SignalSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Objects/object_008.prefab";
        private const string BombSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Objects/object_016.prefab";
        private const string OrbSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Objects/object_017.prefab";
        private const string MineSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Objects/object_018.prefab";
        private const string SwitchSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Objects/switch_007.prefab";
        private const string RingSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Objects/ring.prefab";
        private const string PlatformSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Structures/platform.prefab";
        private const string BlockSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Structures/block.prefab";
        private const string GiftBoxSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Boxes/box_002.prefab";
        private const string LaserSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Objects/laser-spin.prefab";
        private const string CliffSourcePath =
            "Assets/Ignore/Fantasy Lowpoly Pack (Demo)/Prefabs/cliff-1.prefab";
        private const string CrusherWallSourcePath =
            "Assets/Ignore/FreeLowpolyScifiObjects/Prefabs/Structures/wall_003.prefab";

        [Test]
        public void SharedVisuals_ReuseIgnoreSourcesWithoutGameplayColliders()
        {
            AssertSourceMeshes(FinishGatePath, "Gate Source Visual", GateSourcePath);
            AssertSourceMeshes(OutdoorFencePath, "Fence Source Visual", FenceSourcePath);
            AssertSourceMeshes(GroundTilePath, "Ground Source Visual", GroundSourcePath);
            AssertSourceMeshes(
                TripleSwitchPath,
                "Triple Switch Source Visual",
                SwitchSourcePath);
            AssertSourceMeshes(
                SciFiBlockPath,
                "Block Source Visual",
                BlockSourcePath);
            AssertSourceMeshes(
                FantasyCliffPath,
                "Cliff Source Visual",
                CliffSourcePath);

            AssertUsesNestedPrefab(
                "Assets/MazeParty/Prefabs/Minigames/WrongWay/FinishArch.prefab",
                "Shared Finish Gate",
                FinishGatePath);
            AssertUsesNestedPrefab(
                "Assets/MazeParty/Prefabs/Minigames/Race/FinishLine.prefab",
                "Shared Finish Gate",
                FinishGatePath);

            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/Minefield/ProximitySiren.prefab",
                "Siren Base",
                SignalSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/BombPassing/Bomb.prefab",
                null,
                BombSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/Minefield/DetectedMine.prefab",
                null,
                MineSourcePath,
                null);
            for (var slot = 1; slot <= 4; slot++)
            {
                AssertUsesSourceMesh(
                    "Assets/MazeParty/Prefabs/Minigames/BalloonBlow/Balloon" +
                    slot + ".prefab",
                    "Balloon Body",
                    OrbSourcePath,
                    null);
                AssertUsesNestedPrefab(
                    "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/Station" +
                    slot + ".prefab",
                    "Shared Triple Switch",
                    TripleSwitchPath);
            }

            AssertUsesNestedPrefab(
                "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/Npc.prefab",
                "NPC Player Presentation",
                PlayerPresentationPath);

            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/GiftGrab/Gift.prefab",
                "Gift Box",
                GiftBoxSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/SequenceMemory/Npc.prefab",
                "NPC Podium",
                PlatformSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Ball.prefab",
                null,
                OrbSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/CliffBarrage/Projectile.prefab",
                null,
                OrbSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/CliffBarrage/LaserRig.prefab",
                "Laser Emitter Source Visual",
                LaserSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/CliffBarrage/LaserRig.prefab",
                "Laser Emitter Source Visual/Laser Emitter Beam Detail",
                LaserSourcePath,
                "laser-spin_001");
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/BouncingBallsEnvironment.prefab",
                "Center Disc",
                RingSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/BombPassing/BombPassingEnvironment.prefab",
                "Center Spawn Platform/Outer Ring",
                RingSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/BombPassing/BombPassingEnvironment.prefab",
                "Center Spawn Platform/Center Disc",
                PlatformSourcePath,
                null);
            AssertUsesSourceMesh(
                "Assets/MazeParty/Prefabs/Minigames/SnowySpin/IceArena.prefab",
                "Ice Edge",
                RingSourcePath,
                null);

            for (var slot = 1; slot <= 4; slot++)
            {
                AssertUsesSourceMesh(
                    "Assets/MazeParty/Prefabs/Minigames/GiftGrab/Base" +
                    slot + ".prefab",
                    "Base Pad " + slot,
                    RingSourcePath,
                    null);
                AssertUsesNestedPrefab(
                    "Assets/MazeParty/Prefabs/Minigames/TagChase/SightBlocker" +
                    slot + ".prefab",
                    "Shared Sci-Fi Block",
                    SciFiBlockPath);
                AssertUsesSourceMesh(
                    "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Shield" +
                    slot + ".prefab",
                    null,
                    BlockSourcePath,
                    null);
                AssertUsesSourceMesh(
                    "Assets/MazeParty/Prefabs/Minigames/BouncingBalls/Goal" +
                    slot + ".prefab",
                    null,
                    RingSourcePath,
                    null);
                AssertUsesSourceMesh(
                    "Assets/MazeParty/Prefabs/Minigames/ArenaCombat/PlayerSpawn" +
                    slot + ".prefab",
                    "Spawn Ring",
                    RingSourcePath,
                    null);
                AssertUsesSourceMesh(
                    "Assets/MazeParty/Prefabs/Minigames/SnowySpin/PlayerBall" +
                    slot + ".prefab",
                    null,
                    OrbSourcePath,
                    null);
            }

            AssertCrusherVisual();
            AssertGroundTileSurfaces();
            AssertMappedFloorsUseGroundTiles();
            AssertCliffFaces();
        }

        private static void AssertCrusherVisual()
        {
            const string path =
                "Assets/MazeParty/Prefabs/Minigames/Minefield/Crusher.prefab";
            var owner = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(
                CrusherWallSourcePath);
            Assert.That(owner, Is.Not.Null, path);
            Assert.That(source, Is.Not.Null, CrusherWallSourcePath);

            var visual = owner.transform.Find("Crusher Wall Source Visual");
            Assert.That(visual, Is.Not.Null, path);
            Assert.That(
                visual.GetComponent<MeshFilter>()?.sharedMesh,
                Is.SameAs(source.GetComponent<MeshFilter>()?.sharedMesh));
            var authorityCollider = owner.GetComponent<BoxCollider>();
            Assert.That(authorityCollider, Is.Not.Null, path);
            Assert.That(authorityCollider.isTrigger, Is.True, path);
            Assert.That(
                visual.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                path + " imported visual colliders are forbidden.");
        }

        private static void AssertGroundTileSurfaces()
        {
            AssertUsesNestedPrefab(
                "Assets/MazeParty/Prefabs/Minigames/StableFooting/Tile.prefab",
                "Tile Surface/Shared Nature Ground Tile",
                GroundTilePath);
        }

        [Test]
        public void WrongWayLanePrefabs_UseVariedCollisionFreeUrpBasicBlocks()
        {
            var centeredGroundMesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                CenteredGroundMeshPath);
            Assert.That(
                centeredGroundMesh,
                Is.Not.Null,
                CenteredGroundMeshPath);

            var usedBlocks = new HashSet<string>();
            for (var lane = 1; lane <= 4; lane++)
            {
                var path = "Assets/MazeParty/Prefabs/Minigames/WrongWay/Lane" +
                    lane + ".prefab";
                var owner = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(owner, Is.Not.Null, path);
                var platforms = owner.transform.Cast<Transform>()
                    .Where(item => item.name == "Start Platform" ||
                        item.name == "Finish Platform")
                    .ToArray();
                Assert.That(platforms, Has.Length.EqualTo(2), path);
                foreach (var platform in platforms)
                {
                    Assert.That(
                        platform.GetComponent<MeshFilter>()?.sharedMesh,
                        Is.SameAs(centeredGroundMesh),
                        path + " :: " + platform.name);
                    var renderer = platform.GetComponent<MeshRenderer>();
                    Assert.That(
                        renderer,
                        Is.Not.Null,
                        path + " :: " + platform.name);
                    Assert.That(
                        renderer.enabled,
                        Is.True,
                        path + " :: " + platform.name);
                    Assert.That(
                        platform.Find("Toy Block Visual"),
                        Is.Null,
                        path + " :: " + platform.name);
                }

                var steps = owner.transform.Cast<Transform>()
                    .Where(item => item.name.StartsWith("Step "))
                    .ToArray();
                Assert.That(steps, Has.Length.EqualTo(50), path);
                foreach (var step in steps)
                {
                    Assert.That(
                        step.GetComponent<MeshFilter>()?.sharedMesh,
                        Is.SameAs(centeredGroundMesh),
                        path + " :: " + step.name +
                        " must preserve its authored anchor mesh.");
                    Assert.That(
                        step.GetComponent<MeshRenderer>()?.enabled,
                        Is.False,
                        path + " :: " + step.name);

                    var visual = step.Find("Toy Block Visual");
                    Assert.That(visual, Is.Not.Null, path + " :: " + step.name);
                    var sourcePath =
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                            visual.gameObject);
                    Assert.That(
                        sourcePath,
                        Does.StartWith(BasicBlockFolder),
                        path + " :: " + step.name);
                    usedBlocks.Add(sourcePath);
                    Assert.That(
                        step.GetComponentsInChildren<Collider>(true)
                            .All(collider => !collider.enabled),
                        Is.True,
                        path + " :: " + step.name);
                    Assert.That(
                        visual.GetComponentsInChildren<Renderer>(true)
                            .SelectMany(placed => placed.sharedMaterials)
                            .All(material =>
                                material != null &&
                                material.shader != null &&
                                material.shader.name.StartsWith(
                                    "Universal Render Pipeline/")),
                        Is.True,
                        path + " :: " + step.name);
                }
            }

            Assert.That(usedBlocks.Count, Is.GreaterThanOrEqualTo(4));
        }

        private static void AssertMappedFloorsUseGroundTiles()
        {
            var mappings = new[]
            {
                new[]
                {
                    "Minefield/MinefieldEnvironment.prefab",
                    "Arena Floor Visual",
                    "NatureGroundGrid_4x9.asset"
                },
                new[]
                {
                    "WrongWay/WrongWayEnvironment.prefab",
                    "Backdrop Floor",
                    "NatureGroundGrid_3x9.asset"
                },
                new[]
                {
                    "BalloonBlow/BalloonBlowEnvironment.prefab",
                    "Stage Floor Visual",
                    "NatureGroundGrid_4x2.asset"
                },
                new[]
                {
                    "GiftGrab/GiftGrabEnvironment.prefab",
                    "Arena Floor Visual",
                    "NatureGroundGrid_4x4.asset"
                },
                new[]
                {
                    "TagChase/TagChaseEnvironment.prefab",
                    "Arena Floor Visual",
                    "NatureGroundGrid_5x4.asset"
                },
                new[]
                {
                    "Race/RaceTrack.prefab",
                    string.Empty,
                    "NatureGroundGrid_3x8.asset"
                },
                new[]
                {
                    "SequenceMemory/SequenceMemoryEnvironment.prefab",
                    "Stage Floor Visual",
                    "NatureGroundGrid_4x2.asset"
                },
                new[]
                {
                    "BombPassing/BombPassingEnvironment.prefab",
                    "Inner Floor",
                    "NatureGroundGrid_4x4.asset"
                },
                new[]
                {
                    "ArenaCombat/ArenaStructure.prefab",
                    "Arena Floor",
                    "NatureGroundGrid_4x4.asset"
                },
                new[]
                {
                    "CliffBarrage/CliffArena.prefab",
                    "Cliff Platform",
                    "NatureGroundGrid_4x4.asset"
                },
                new[]
                {
                    "RedLightGreenLight/RedLightGreenLightEnvironment.prefab",
                    "Arena Floor Visual",
                    "NatureGroundGrid_4x9.asset"
                }
            };

            foreach (var mapping in mappings)
            {
                var path =
                    "Assets/MazeParty/Prefabs/Minigames/" + mapping[0];
                var owner = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                Assert.That(owner, Is.Not.Null, path);
                var target = string.IsNullOrEmpty(mapping[1])
                    ? owner.transform
                    : owner.transform.Find(mapping[1]);
                Assert.That(target, Is.Not.Null, path + " :: " + mapping[1]);
                var expectedMeshPath = GeneratedMeshFolder + mapping[2];
                var expectedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(
                    expectedMeshPath);
                Assert.That(expectedMesh, Is.Not.Null, expectedMeshPath);
                Assert.That(
                    target.GetComponent<MeshFilter>()?.sharedMesh,
                    Is.SameAs(expectedMesh),
                    path + " :: " + mapping[1]);
                var renderer = target.GetComponent<MeshRenderer>();
                Assert.That(renderer, Is.Not.Null, path);
                Assert.That(renderer.enabled, Is.True, path);
                Assert.That(
                    target.Find("Shared Ground Tiles"),
                    Is.Null,
                    path + " must not keep expanded ground tile children.");
                Assert.That(
                    owner.transform.Find("Shared Nature Ground Tiles"),
                    Is.Null,
                    path + " must not keep the legacy ground tile container.");
            }
        }

        private static void AssertCliffFaces()
        {
            const string path =
                "Assets/MazeParty/Prefabs/Minigames/CliffBarrage/CliffArena.prefab";
            var owner = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.That(owner, Is.Not.Null, path);
            var faceNames = new[]
            {
                "North Cliff Face", "South Cliff Face",
                "East Cliff Face", "West Cliff Face"
            };
            foreach (var faceName in faceNames)
            {
                for (var index = 1; index <= 4; index++)
                {
                    var visual = owner.transform.Find(
                        faceName + "/Shared Fantasy Cliff " + index);
                    Assert.That(
                        visual,
                        Is.Not.Null,
                        path + " :: " + faceName + " :: " + index);
                    Assert.That(
                        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                            visual.gameObject),
                        Is.EqualTo(FantasyCliffPath),
                        path + " :: " + faceName + " :: " + index);
                }
            }
        }

        private static void AssertSourceMeshes(
            string wrapperPath,
            string childName,
            string sourcePath)
        {
            var wrapper = AssetDatabase.LoadAssetAtPath<GameObject>(wrapperPath);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(wrapper, Is.Not.Null, wrapperPath);
            Assert.That(source, Is.Not.Null, sourcePath);
            var child = wrapper.transform.Find(childName);
            Assert.That(child, Is.Not.Null, childName);
            var sourceMeshes = source.GetComponentsInChildren<MeshFilter>(true)
                .Select(filter => filter.sharedMesh)
                .Where(mesh => mesh != null)
                .ToArray();
            var wrapperMeshes = child.GetComponentsInChildren<MeshFilter>(true)
                .Select(filter => filter.sharedMesh)
                .Where(mesh => mesh != null)
                .ToArray();
            Assert.That(
                wrapperMeshes,
                Is.Not.Empty);
            Assert.That(
                wrapperMeshes.All(sourceMeshes.Contains),
                Is.True,
                wrapperPath + " must keep meshes from " + sourcePath + ".");
            Assert.That(
                wrapper.GetComponentsInChildren<Collider>(true)
                    .Any(collider => collider.enabled),
                Is.False,
                wrapperPath + " must remain visual-only.");
            Assert.That(
                wrapper.GetComponentsInChildren<Renderer>(true)
                    .SelectMany(renderer => renderer.sharedMaterials)
                    .Where(material => material != null)
                    .All(material => material.shader != null &&
                        material.shader.name.StartsWith(
                            "Universal Render Pipeline/")),
                Is.True,
                wrapperPath + " must use URP-safe material overrides.");
        }

        private static void AssertUsesNestedPrefab(
            string ownerPath,
            string childName,
            string expectedPrefabPath)
        {
            var owner = AssetDatabase.LoadAssetAtPath<GameObject>(ownerPath);
            Assert.That(owner, Is.Not.Null, ownerPath);
            var child = owner.transform.Find(childName);
            Assert.That(child, Is.Not.Null, ownerPath + ":" + childName);
            Assert.That(
                PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(
                    child.gameObject),
                Is.EqualTo(expectedPrefabPath));
        }

        private static void AssertUsesSourceMesh(
            string ownerPath,
            string childName,
            string sourcePath,
            string sourceChildName)
        {
            var owner = AssetDatabase.LoadAssetAtPath<GameObject>(ownerPath);
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            Assert.That(owner, Is.Not.Null, ownerPath);
            Assert.That(source, Is.Not.Null, sourcePath);

            var ownerTarget = string.IsNullOrEmpty(childName)
                ? owner.transform
                : owner.transform.Find(childName);
            var sourceTarget = string.IsNullOrEmpty(sourceChildName)
                ? source.transform
                : source.transform.Find(sourceChildName);
            Assert.That(ownerTarget, Is.Not.Null, childName);
            Assert.That(sourceTarget, Is.Not.Null, sourceChildName);
            Assert.That(
                ownerTarget.GetComponent<MeshFilter>()?.sharedMesh,
                Is.SameAs(sourceTarget.GetComponent<MeshFilter>()?.sharedMesh));
            Assert.That(
                owner.GetComponentsInChildren<Collider>(true)
                    .Any(collider => collider.enabled),
                Is.False,
                ownerPath + " imported visual colliders are forbidden.");
        }
    }
}
