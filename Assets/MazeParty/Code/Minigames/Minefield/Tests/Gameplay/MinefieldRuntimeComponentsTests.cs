using System.Collections.Generic;
using MazeParty.Gameplay.Minigames.Minefield;
using NUnit.Framework;
using UnityEngine;

namespace MazeParty.Gameplay.Tests
{
    public sealed class MinefieldRuntimeComponentsTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (var index = _objects.Count - 1; index >= 0; index--)
            {
                if (_objects[index] != null)
                {
                    Object.DestroyImmediate(_objects[index]);
                }
            }
            _objects.Clear();
        }

        [Test]
        public void PlayerActor_AppliesAuthoritativeHitsAndRejectsStaleSnapshots()
        {
            var actor = CreateActor(2);

            var first = actor.ApplyAuthoritativeMineHit(null);
            var second = actor.ApplyAuthoritativeMineHit(null);

            Assert.That(first.BecameCrippled, Is.True);
            Assert.That(second.BecameEliminated, Is.True);
            Assert.That(actor.MineHitCount, Is.EqualTo(2));
            Assert.That(actor.State, Is.EqualTo(MinefieldPlayerState.Eliminated));
            Assert.That(
                actor.EliminationCause,
                Is.EqualTo(MinefieldEliminationCause.SecondMineHit));
            Assert.That(actor.CanMove, Is.False);
            Assert.That(actor.HazardsEnabled, Is.False);

            actor.ResetForRoundAuthoritatively(0);
            var currentRevision = actor.StateRevision;

            var applied = actor.ApplyAuthoritativeSnapshot(
                new MinefieldActorSnapshot(
                    currentRevision - 1,
                    0,
                    2,
                    MinefieldPlayerState.Eliminated,
                    MinefieldEliminationCause.SecondMineHit,
                    false));

            Assert.That(applied, Is.False);
            Assert.That(actor.State, Is.EqualTo(MinefieldPlayerState.Healthy));
        }

        [Test]
        public void Mine_ConsumesItselfAfterOneAuthoritativeContact()
        {
            var registry = CreateObject("Registry")
                .AddComponent<MinefieldMineRegistry>();
            var mineObject = CreateObject("Mine");
            mineObject.AddComponent<BoxCollider>();
            var mine = mineObject.AddComponent<MinefieldMine>();
            mine.ConfigureRegistry(registry);
            mine.ArmForRoundAuthoritatively();
            var actor = CreateActor(1);

            var first = mine.ResolveContactAuthoritatively(actor);
            var duplicate = mine.ResolveContactAuthoritatively(actor);

            Assert.That(first.WasApplied, Is.True);
            Assert.That(duplicate.WasApplied, Is.False);
            Assert.That(mine.IsArmed, Is.False);
            Assert.That(actor.State, Is.EqualTo(MinefieldPlayerState.Crippled));
        }

        [Test]
        public void Detection_UsesPlanarDistanceAndRequiresAStationaryPlayer()
        {
            var registry = CreateObject("Registry")
                .AddComponent<MinefieldMineRegistry>();
            var farther = CreateMine("Farther", new Vector3(3f, 20f, 4f), registry);
            var nearer = CreateMine("Nearer", new Vector3(2f, -10f, 0f), registry);

            var found = registry.TryGetNearestArmedMine(
                Vector3.zero,
                out var nearest,
                out var distance);

            Assert.That(found, Is.True);
            Assert.That(nearest, Is.SameAs(nearer));
            Assert.That(distance, Is.EqualTo(2f).Within(0.0001f));

            nearer.ArmForRoundAuthoritatively(false);
            registry.TryGetNearestArmedMine(
                Vector3.zero,
                out nearest,
                out distance);

            Assert.That(nearest, Is.SameAs(farther));
            Assert.That(distance, Is.EqualTo(5f).Within(0.0001f));

            var intensityCases = new[]
            {
                (Distance: 0f, WarningDistance: 5f, Expected: 1f),
                (Distance: 2.5f, WarningDistance: 5f, Expected: 0.5f),
                (Distance: 5f, WarningDistance: 5f, Expected: 0f),
                (Distance: 8f, WarningDistance: 5f, Expected: 0f)
            };
            foreach (var testCase in intensityCases)
            {
                Assert.That(
                    MinefieldProximitySiren.CalculateNormalizedIntensity(
                        testCase.Distance,
                        testCase.WarningDistance),
                    Is.EqualTo(testCase.Expected).Within(0.0001f));
            }

            Assert.That(
                MinefieldSonar.IsStationaryForPulse(
                    0f,
                    Vector2.zero,
                    0.05f,
                    0.05f),
                Is.True);
            Assert.That(
                MinefieldSonar.IsStationaryForPulse(
                    0.1f,
                    Vector2.zero,
                    0.05f,
                    0.05f),
                Is.False);
            Assert.That(
                MinefieldSonar.IsStationaryForPulse(
                    0f,
                    Vector2.right,
                    0.05f,
                    0.05f),
                Is.False);
        }

        [Test]
        public void Crusher_AdvancesByProgressAndEliminatesContact()
        {
            var crusherObject = CreateObject("Crusher");
            crusherObject.AddComponent<BoxCollider>();
            var crusher = crusherObject.AddComponent<MinefieldCrusher>();
            crusher.ConfigurePath(Vector3.zero, Vector3.forward * 10f);
            crusher.ResetForRoundAuthoritatively();
            crusher.BeginSweepAuthoritatively();

            crusher.TickAuthoritatively(2f);

            Assert.That(crusher.Progress, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(crusher.transform.position.z, Is.EqualTo(5f).Within(0.0001f));

            var actor = CreateActor(3);
            Assert.That(crusher.TryRequestElimination(actor), Is.True);
            Assert.That(actor.State, Is.EqualTo(MinefieldPlayerState.Eliminated));
            Assert.That(
                actor.EliminationCause,
                Is.EqualTo(MinefieldEliminationCause.Crusher));
        }

        private MinefieldPlayerActor CreateActor(int slot)
        {
            var actor = CreateObject("Player " + slot)
                .AddComponent<MinefieldPlayerActor>();
            actor.ConfigurePlayerSlot(slot);
            return actor;
        }

        private MinefieldMine CreateMine(
            string name,
            Vector3 position,
            MinefieldMineRegistry registry)
        {
            var mineObject = CreateObject(name);
            mineObject.transform.position = position;
            mineObject.AddComponent<BoxCollider>();
            var mine = mineObject.AddComponent<MinefieldMine>();
            mine.ConfigureRegistry(registry);
            mine.ArmForRoundAuthoritatively();
            return mine;
        }

        private GameObject CreateObject(string name)
        {
            var value = new GameObject(name);
            _objects.Add(value);
            return value;
        }
    }
}
