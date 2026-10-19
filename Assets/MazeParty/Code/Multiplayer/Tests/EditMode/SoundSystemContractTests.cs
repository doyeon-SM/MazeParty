using System.Collections.Generic;
using System.Linq;
using MazeParty.Gameplay;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace MazeParty.Multiplayer.Tests
{
    /// <summary>
    /// The sound system installed by MazeParty/Audio/Install Sound System:
    /// prefab bindings, mixer parameters and snapshots, a cue for every key,
    /// and a sound emitter on every UI control.
    /// </summary>
    public sealed class SoundSystemContractTests
    {
        private const string PrefabPath =
            "Assets/MazeParty/Resources/" + SoundSystem.ResourcePath + ".prefab";
        private const string AudioRoot = "Assets/MazeParty/Audio";
        private const string UiPrefabRoot = "Assets/MazeParty/Prefabs";
        private const string SequenceMemoryBellPath =
            "Assets/Resources/sound/- Bell 7.mp3";
        private const string BalloonClipPath =
            "Assets/Resources/sound/balloon.mp3";
        private const string LightningClipPath =
            "Assets/Resources/sound/lightning.mp3";
        private const string MinefieldExplosionClipPath =
            "Assets/Resources/sound/explosion.mp3";
        private const string MinefieldScanClipPath =
            "Assets/Resources/sound/mine.mp3";
        private const string PistolClipPath =
            "Assets/Resources/sound/pistol.mp3";
        private const string SniperClipPath =
            "Assets/Resources/sound/sniper.mp3";

        [Test]
        public void SoundSystemPrefab_MixerAndLibrary_CoverEveryKey()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.That(prefab, Is.Not.Null, PrefabPath);
            Assert.That(Resources.Load<SoundSystem>(SoundSystem.ResourcePath), Is.Not.Null);
            var system = prefab.GetComponent<SoundSystem>();
            Assert.That(system, Is.Not.Null);
            Assert.That(system.HasRequiredReferences, Is.True, "SoundSystem bindings");
            Assert.That(prefab.GetComponent<BgmDirector>(), Is.Not.Null);
            Assert.That(system.Voices.All(voice => !voice.playOnAwake), Is.True);
            Assert.That(system.MusicSources.All(source => !source.playOnAwake), Is.True);

            var mixer = system.Mixer;
            foreach (var parameter in new[]
                     {
                         SoundSystem.MasterVolumeParameter,
                         SoundSystem.BgmVolumeParameter,
                         SoundSystem.SfxVolumeParameter
                     })
            {
                Assert.That(mixer.GetFloat(parameter, out _), Is.True, "exposed " + parameter);
            }

            Assert.That(system.MusicGroup.audioMixer, Is.SameAs(mixer));
            Assert.That(system.EffectsGroup.audioMixer, Is.SameAs(mixer));
            Assert.That(system.UiGroup.audioMixer, Is.SameAs(mixer));
            Assert.That(system.DefaultSnapshot.name, Is.EqualTo(SoundSystem.DefaultSnapshotName));
            Assert.That(system.PausedSnapshot.name, Is.EqualTo(SoundSystem.PausedSnapshotName));
            Assert.That(system.DuckedSnapshot.name, Is.EqualTo(SoundSystem.DuckedSnapshotName));

            var library = system.Library;
            var problems = new List<string>();
            foreach (var key in SoundKeys.All())
            {
                if (!library.TryGet(key, out var cue))
                {
                    problems.Add("missing cue: " + key);
                }
                else if (cue.Channel != SoundKeys.GetChannel(key))
                {
                    problems.Add("channel " + cue.Channel + ": " + key);
                }
            }

            Assert.That(
                library.TryGet(
                    SoundKeys.MinigameSequenceMemoryTone,
                    out var sequenceTone),
                Is.True);
            var sequenceBell = AssetDatabase.LoadAssetAtPath<AudioClip>(
                SequenceMemoryBellPath);
            Assert.That(sequenceBell, Is.Not.Null, SequenceMemoryBellPath);
            Assert.That(sequenceTone.Channel, Is.EqualTo(AudioChannel.Sfx));
            Assert.That(sequenceTone.Spatial, Is.False);
            Assert.That(sequenceTone.PitchVariance, Is.Zero);
            Assert.That(sequenceTone.StartOffsetSeconds,
                Is.EqualTo(0.21f).Within(0.001f));
            Assert.That(sequenceTone.MaxInstances, Is.EqualTo(8));
            Assert.That(sequenceTone.ClipCount, Is.EqualTo(1));
            Assert.That(sequenceTone.GetClip(0), Is.SameAs(sequenceBell));

            AssertCueUsesClip(
                library,
                SoundKeys.Balloon,
                BalloonClipPath);
            AssertCueUsesClip(
                library,
                SoundKeys.Lightning,
                LightningClipPath);
            AssertSpatialCueUsesClip(
                library,
                SoundKeys.MinigameMinefieldExplosion,
                MinefieldExplosionClipPath);
            AssertSpatialCueUsesClip(
                library,
                SoundKeys.MinigameMinefieldScan,
                MinefieldScanClipPath);
            AssertSpatialCueUsesClip(
                library,
                SoundKeys.ItemUse(PrototypeItemId.Pistol),
                PistolClipPath);
            AssertSpatialCueUsesClip(
                library,
                SoundKeys.ItemUse(PrototypeItemId.Sniper),
                SniperClipPath);

            AssertCueUsesClip(library, SoundKeys.MinigameFinish,
                "Assets/Resources/sound/minigamefinish.mp3");
            AssertCueUsesClip(library, SoundKeys.BoardKeyBuy,
                "Assets/Resources/sound/keybuy 1.mp3");
            Assert.That(library.TryGet(SoundKeys.ItemBulletImpact, out var gunHit), Is.True);
            Assert.That(gunHit.Spatial, Is.True);
            Assert.That(gunHit.VariationMode, Is.EqualTo(SoundVariationMode.RandomNoRepeat));
            Assert.That(gunHit.ClipCount, Is.GreaterThan(1));
            var hitClips = Enumerable.Range(0, gunHit.ClipCount).Select(gunHit.GetClip).ToArray();
            Assert.That(hitClips.All(clip => clip != null), Is.True);
            Assert.That(hitClips.Distinct().Count(), Is.EqualTo(gunHit.ClipCount));
            Assert.That(hitClips.Contains(
                AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Resources/sound/gunhitsound.mp3")), Is.False);

            AssertCueUsesClip(library, SoundKeys.MinigameBombExplosion, "Assets/Resources/sound/explosion.mp3");
            AssertCueUsesClip(library, SoundKeys.MinigameGiftPickup, "Assets/Resources/sound/goldgain.mp3");
            AssertCueUsesClip(library, SoundKeys.MinigameGiftDeposit, "Assets/Resources/sound/shopbuy.mp3");
            AssertCueUsesClip(library, SoundKeys.MinigameGiftThrow, "Assets/Resources/sound/grenade.mp3");
            AssertCueUsesClip(library, SoundKeys.MinigameBallReflect, "Assets/Resources/sound/dicebounce.mp3");
            AssertCueUsesClip(library, SoundKeys.MinigameBallGoal, "Assets/Resources/sound/diceresult.mp3");
            AssertCueUsesClip(library, SoundKeys.MinigameGreenSignal, "Assets/Resources/sound/countdowngo.mp3");
            AssertCueUsesClip(library, SoundKeys.MinigameWrongWayCorrect, "Assets/Resources/sound/confirmandpopupopen.mp3");
            AssertCueUsesClip(library, SoundKeys.MinigameWrongWayIncorrect, "Assets/Resources/sound/errorandnotice.mp3");
            AssertCueUsesClip(library, SoundKeys.BoardChestAcquire, "Assets/Resources/sound/keybuy 1.mp3");
            Assert.That(library.TryGet(SoundKeys.MinigameGiftHit, out var giftHit), Is.True);
            Assert.That(giftHit.ClipCount, Is.EqualTo(3));
            Assert.That(giftHit.VariationMode, Is.EqualTo(SoundVariationMode.RandomNoRepeat));
            foreach (var key in new[] { SoundKeys.MinigameBombExplosion, SoundKeys.MinigameGiftPickup, SoundKeys.MinigameGiftDeposit, SoundKeys.MinigameGiftThrow, SoundKeys.MinigameGiftHit, SoundKeys.MinigameBallReflect, SoundKeys.MinigameBallGoal, SoundKeys.MinigameGreenSignal, SoundKeys.MinigameWrongWayCorrect, SoundKeys.MinigameWrongWayIncorrect, SoundKeys.BoardChestAcquire })
            {
                Assert.That(library.TryGet(key, out var reused), Is.True);
                Assert.That(reused.Channel, Is.EqualTo(AudioChannel.Sfx), key);
                Assert.That(reused.HasClips, Is.True, key);
                Assert.That(reused.Loop, Is.False, key);
            }

            var registered = new HashSet<SoundCue>(library.Cues);
            var keys = new HashSet<string>();
            foreach (var cue in library.Cues)
            {
                if (cue == null || !keys.Add(cue.Key))
                {
                    problems.Add("null or duplicate cue: " + (cue != null ? cue.Key : "null"));
                }
            }

            foreach (var guid in AssetDatabase.FindAssets("t:SoundCue", new[] { AudioRoot }))
            {
                var cue = AssetDatabase.LoadAssetAtPath<SoundCue>(AssetDatabase.GUIDToAssetPath(guid));
                if (cue != null && !registered.Contains(cue))
                {
                    problems.Add("not in the library (Refresh Sound Library): " +
                                 AssetDatabase.GetAssetPath(cue));
                }
            }

            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        private static void AssertCueUsesClip(
            SoundLibrary library,
            string key,
            string clipPath)
        {
            Assert.That(library.TryGet(key, out var cue), Is.True, key);
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(clipPath);
            Assert.That(clip, Is.Not.Null, clipPath);
            Assert.That(cue.ClipCount, Is.EqualTo(1), key);
            Assert.That(cue.GetClip(0), Is.SameAs(clip), key);
        }

        private static void AssertSpatialCueUsesClip(
            SoundLibrary library,
            string key,
            string clipPath)
        {
            AssertCueUsesClip(library, key, clipPath);
            Assert.That(library.TryGet(key, out var cue), Is.True, key);
            Assert.That(cue.Spatial, Is.True, key);
        }

        [Test]
        public void UiPrefabControls_HaveExactlyOneSoundEmitter()
        {
            var problems = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { UiPrefabRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Dev/"))
                {
                    continue;
                }

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                foreach (var control in prefab.GetComponentsInChildren<Selectable>(true))
                {
                    var count = control.GetComponents<UiSoundEmitter>().Length;
                    if (count != 1)
                    {
                        problems.Add(path + " :: " + control.name + " has " + count);
                    }
                }
            }

            Assert.That(problems, Is.Empty,
                "Run MazeParty/Audio/Add UI Sounds To Prefabs.\n" + string.Join("\n", problems));
        }
    }
}
