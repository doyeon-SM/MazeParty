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
