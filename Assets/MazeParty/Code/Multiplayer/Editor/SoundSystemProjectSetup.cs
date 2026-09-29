using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using MazeParty.Gameplay;
using MazeParty.Multiplayer;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MazeParty.Editor
{
    /// <summary>
    /// Installs the sound system: the AudioMixer, one SoundCue per
    /// <see cref="SoundKeys"/> key, the SoundLibrary and the SoundSystem
    /// prefab in Resources. Existing assets are kept (only missing ones are
    /// created and missing prefab bindings filled), so designers' clips and
    /// mixer edits survive re-running it.
    /// </summary>
    public static class SoundSystemProjectSetup
    {
        public const string AudioRoot = "Assets/MazeParty/Audio";
        public const string CueRoot = AudioRoot + "/Cues";
        public const string MixerPath = AudioRoot + "/Mixer/MazeParty.mixer";
        public const string LibraryPath = AudioRoot + "/SoundLibrary.asset";
        public const string PrefabPath =
            "Assets/MazeParty/Resources/" + SoundSystem.ResourcePath + ".prefab";
        public const string PrefabSearchRoot = "Assets/MazeParty/Prefabs";
        public const int VoiceCount = 24;

        [MenuItem("MazeParty/Audio/Install Sound System")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Install Sound System requires Edit Mode.");
                return;
            }

            var mixer = EnsureMixer();
            var createdCues = EnsureCues();
            var library = RefreshLibrary();
            EnsurePrefab(library, mixer);
            var uiSounds = AddUiSoundsToPrefabs();
            AssetDatabase.SaveAssets();
            Debug.Log(
                "Sound system ready: " + createdCues + " new cue(s), " +
                library.Cues.Count + " cue(s) in the library, " +
                uiSounds + " UI control(s) given sounds.");
        }

        [MenuItem("MazeParty/Audio/Refresh Sound Library")]
        public static void RefreshLibraryMenu()
        {
            var library = RefreshLibrary();
            Debug.Log("Sound library: " + library.Cues.Count + " cue(s).", library);
        }

        [MenuItem("MazeParty/Audio/Add UI Sounds To Prefabs")]
        public static void AddUiSoundsMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("Add UI Sounds To Prefabs requires Edit Mode.");
                return;
            }

            Debug.Log("UI sounds added to " + AddUiSoundsToPrefabs() + " control(s).");
        }

        [MenuItem("Assets/Create/MazeParty/Audio/Sound Cue From Selected Clips", false, 300)]
        public static void CreateCueFromSelectedClips()
        {
            var clips = Selection.GetFiltered<AudioClip>(SelectionMode.Assets)
                .OrderBy(clip => clip.name, StringComparer.Ordinal)
                .ToArray();
            if (clips.Length == 0)
            {
                return;
            }

            var key = SuggestKey(clips[0].name);
            var folder = CueRoot + "/Custom";
            EnsureFolder(folder);
            var path = AssetDatabase.GenerateUniqueAssetPath(folder + "/" + key + ".asset");
            var cue = ScriptableObject.CreateInstance<SoundCue>();
            ApplyDefaults(cue, key);
            cue.SetClips(clips);
            AssetDatabase.CreateAsset(cue, path);
            RefreshLibrary();
            Selection.activeObject = cue;
            Debug.Log(
                "Created " + path + " with " + clips.Length + " clip(s). " +
                "Set its key to the one the code plays (see SoundKeys).",
                cue);
        }

        [MenuItem("Assets/Create/MazeParty/Audio/Sound Cue From Selected Clips", true)]
        private static bool CanCreateCueFromSelectedClips()
        {
            return Selection.GetFiltered<AudioClip>(SelectionMode.Assets).Length > 0;
        }

        internal static AudioMixer EnsureMixer()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer != null)
            {
                return mixer;
            }

            EnsureFolder(Path.GetDirectoryName(MixerPath)?.Replace('\\', '/'));
            return AudioMixerAuthoring.Create(MixerPath);
        }

        /// <summary>Creates a cue for every key without one. Returns the count.</summary>
        internal static int EnsureCues()
        {
            var existingKeys = new HashSet<string>(
                FindAllCues().Select(cue => cue.Key),
                StringComparer.Ordinal);
            var created = 0;
            foreach (var key in SoundKeys.All())
            {
                if (existingKeys.Contains(key))
                {
                    continue;
                }

                var path = CuePath(key);
                if (AssetDatabase.LoadAssetAtPath<SoundCue>(path) != null)
                {
                    continue;
                }

                EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
                var cue = ScriptableObject.CreateInstance<SoundCue>();
                ApplyDefaults(cue, key);
                AssetDatabase.CreateAsset(cue, path);
                existingKeys.Add(key);
                created++;
            }

            return created;
        }

        /// <summary>
        /// Registers every SoundCue under <see cref="AudioRoot"/>. A duplicate
        /// key keeps the first cue by path and logs the other.
        /// </summary>
        internal static SoundLibrary RefreshLibrary()
        {
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);
            if (library == null)
            {
                EnsureFolder(AudioRoot);
                library = ScriptableObject.CreateInstance<SoundLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }

            var byKey = new SortedDictionary<string, SoundCue>(StringComparer.Ordinal);
            foreach (var cue in FindAllCues())
            {
                if (string.IsNullOrWhiteSpace(cue.Key))
                {
                    Debug.LogWarning("Sound cue without a key is not registered.", cue);
                    continue;
                }

                if (byKey.TryGetValue(cue.Key, out var kept))
                {
                    Debug.LogWarning(
                        "Duplicate sound key '" + cue.Key + "': keeping " +
                        AssetDatabase.GetAssetPath(kept) + ".",
                        cue);
                    continue;
                }

                byKey.Add(cue.Key, cue);
            }

            library.SetCues(byKey.Values.ToArray());
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        /// <summary>
        /// Creates the SoundSystem prefab when missing; otherwise only fills
        /// missing bindings so an edited prefab is never rebuilt.
        /// </summary>
        internal static void EnsurePrefab(SoundLibrary library, AudioMixer mixer)
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing == null)
            {
                EnsureFolder(Path.GetDirectoryName(PrefabPath)?.Replace('\\', '/'));
                var root = new GameObject("SoundSystem");
                try
                {
                    var system = root.AddComponent<SoundSystem>();
                    root.AddComponent<BgmDirector>();
                    var voices = CreateSources(root.transform, "Voices", "Voice", VoiceCount, false);
                    var music = CreateSources(
                        root.transform,
                        "Music",
                        "Music",
                        SoundSystem.MusicSourceCount,
                        true);
                    Bind(system, library, mixer, voices, music);
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                }
                finally
                {
                    Object.DestroyImmediate(root);
                }

                return;
            }

            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var system = contents.GetComponent<SoundSystem>();
                var changed = false;
                if (system == null)
                {
                    system = contents.AddComponent<SoundSystem>();
                    changed = true;
                }

                if (contents.GetComponent<BgmDirector>() == null)
                {
                    contents.AddComponent<BgmDirector>();
                    changed = true;
                }

                if (!system.HasRequiredReferences)
                {
                    var voices = system.Voices.Where(source => source != null).ToArray();
                    if (voices.Length < SoundSystem.MinimumVoiceCount)
                    {
                        voices = CreateSources(contents.transform, "Voices", "Voice", VoiceCount, false);
                    }

                    var music = system.MusicSources.Where(source => source != null).ToArray();
                    if (music.Length != SoundSystem.MusicSourceCount)
                    {
                        music = CreateSources(
                            contents.transform,
                            "Music",
                            "Music",
                            SoundSystem.MusicSourceCount,
                            true);
                    }

                    Bind(
                        system,
                        system.Library != null ? system.Library : library,
                        system.Mixer != null ? system.Mixer : mixer,
                        voices,
                        music);
                    changed = true;
                }

                if (changed)
                {
                    PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        /// <summary>
        /// Gives every control in the UI prefabs (Dev prefabs excluded) a
        /// <see cref="UiSoundEmitter"/>. Controls inside a nested prefab are
        /// handled in that prefab. Returns the number of controls changed.
        /// </summary>
        internal static int AddUiSoundsToPrefabs()
        {
            var added = 0;
            foreach (var path in FindUiPrefabPaths())
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset == null ||
                    asset.GetComponentsInChildren<Selectable>(true)
                        .All(control => control.GetComponent<UiSoundEmitter>() != null))
                {
                    continue;
                }

                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var count = 0;
                    foreach (var control in root.GetComponentsInChildren<Selectable>(true))
                    {
                        if (control.GetComponent<UiSoundEmitter>() != null ||
                            PrefabUtility.IsPartOfPrefabInstance(control.gameObject))
                        {
                            continue;
                        }

                        var emitter = control.gameObject.AddComponent<UiSoundEmitter>();
                        var sounds = DefaultUiSounds(control);
                        emitter.Configure(sounds.Hover, sounds.Click);
                        count++;
                    }

                    if (count > 0)
                    {
                        PrefabUtility.SaveAsPrefabAsset(root, path);
                        added += count;
                    }
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(root);
                }
            }

            return added;
        }

        /// <summary>Non-Dev prefabs under Assets/MazeParty/Prefabs that contain controls.</summary>
        public static IEnumerable<string> FindUiPrefabPaths()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { PrefabSearchRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.Contains("/Dev/"))
                {
                    continue;
                }

                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (asset != null && asset.GetComponentInChildren<Selectable>(true) != null)
                {
                    yield return path;
                }
            }
        }

        /// <summary>
        /// Default sounds by control type and name: scrollbars silent, input
        /// fields click only, close/cancel/back → back, apply/confirm/ready/
        /// start/create/join/OK/leave → confirm, anything else → click.
        /// </summary>
        public static (string Hover, string Click) DefaultUiSounds(Selectable control)
        {
            if (control is Scrollbar)
            {
                return (string.Empty, string.Empty);
            }

            if (control is InputField)
            {
                return (string.Empty, SoundKeys.UiClick);
            }

            var name = control.name.ToLowerInvariant();
            if (ContainsAny(name, "close", "cancel", "back"))
            {
                return (SoundKeys.UiHover, SoundKeys.UiBack);
            }

            if (ContainsAny(name, "apply", "confirm", "ready", "start", "create", "join") ||
                name == "ok button" ||
                name == "leave button" ||
                name == "leave room button")
            {
                return (SoundKeys.UiHover, SoundKeys.UiConfirm);
            }

            return (SoundKeys.UiHover, SoundKeys.UiClick);
        }

        public static string CuePath(string key)
        {
            var dot = key.IndexOf('.');
            var group = dot > 0 ? key.Substring(0, dot) : "Misc";
            var folder = char.ToUpperInvariant(group[0]) + group.Substring(1);
            return CueRoot + "/" + folder + "/" + key + ".asset";
        }

        public static IEnumerable<SoundCue> FindAllCues()
        {
            return AssetDatabase.FindAssets("t:SoundCue", new[] { AudioRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(AssetDatabase.LoadAssetAtPath<SoundCue>)
                .Where(cue => cue != null);
        }

        /// <summary>
        /// Starting values for a new cue by key. Hits and other repeated
        /// world sounds get random pitch/volume and an instance limit.
        /// </summary>
        public static void ApplyDefaults(SoundCue cue, string key)
        {
            var channel = SoundKeys.GetChannel(key);
            var spatial = false;
            var loop = false;
            var volume = 1f;
            var volumeVariance = 0f;
            var pitchVariance = 0f;
            var variationMode = SoundVariationMode.RandomNoRepeat;
            var maxInstances = 0;
            var minInterval = 0f;
            var priority = 60;
            var duck = false;
            var fade = 0.5f;
            var maxDistance = 30f;

            if (channel == AudioChannel.Bgm)
            {
                loop = true;
                volume = 0.7f;
                fade = 1.5f;
                priority = 100;
            }
            else if (channel == AudioChannel.Ui)
            {
                volume = 0.8f;
                maxInstances = 2;
                minInterval = key == SoundKeys.UiHover ? 0.04f : 0.02f;
                priority = key == SoundKeys.UiHover ? 30 : 70;
            }
            else if (key.StartsWith("combat.hit.", StringComparison.Ordinal) ||
                     key == SoundKeys.ItemBulletImpact)
            {
                spatial = true;
                volumeVariance = 0.1f;
                pitchVariance = 0.08f;
                maxInstances = 4;
                minInterval = 0.05f;
                priority = 70;
            }
            else if (key == SoundKeys.CombatPunchSwing)
            {
                spatial = true;
                volumeVariance = 0.1f;
                pitchVariance = 0.1f;
                maxInstances = 4;
                minInterval = 0.04f;
                priority = 50;
            }
            else if (key == SoundKeys.BoardFootstep)
            {
                spatial = true;
                volumeVariance = 0.1f;
                pitchVariance = 0.05f;
                variationMode = SoundVariationMode.Shuffle;
                maxInstances = 6;
                priority = 20;
            }
            else if (key == SoundKeys.BoardDiceBounce)
            {
                spatial = true;
                volumeVariance = 0.2f;
                pitchVariance = 0.12f;
                maxInstances = 4;
                minInterval = 0.05f;
                priority = 30;
            }
            else if (key == SoundKeys.ItemExplosion)
            {
                spatial = true;
                pitchVariance = 0.06f;
                priority = 90;
                maxDistance = 60f;
            }
            else if (key.StartsWith("combat.", StringComparison.Ordinal) ||
                     key.StartsWith(SoundKeys.ItemUsePrefix, StringComparison.Ordinal) ||
                     key == SoundKeys.BoardDiceRoll ||
                     key == SoundKeys.BoardDiceResult)
            {
                spatial = true;
                pitchVariance = 0.05f;
                priority = 70;
            }
            else if (key == SoundKeys.MinigameReveal ||
                     key == SoundKeys.CeremonyFanfare)
            {
                duck = true;
                priority = 90;
            }
            else if (key == SoundKeys.CeremonyAwardReady)
            {
                // The source clip is longer than the two-second ready phase.
                // Keep ducking off because the global duck timer follows the
                // full clip length even when the voice is stopped early.
                maxInstances = 1;
                priority = 90;
            }
            else if (key.StartsWith("minigame.", StringComparison.Ordinal))
            {
                priority = 80;
            }

            cue.ConfigureDefaults(
                key,
                channel,
                spatial,
                loop,
                volume,
                volumeVariance,
                pitchVariance,
                maxInstances,
                minInterval,
                priority,
                duck,
                fade,
                maxDistance,
                variationMode);
        }

        private static void Bind(
            SoundSystem system,
            SoundLibrary library,
            AudioMixer mixer,
            AudioSource[] voices,
            AudioSource[] music)
        {
            system.Configure(
                library,
                mixer,
                FindGroup(mixer, AudioMixerAuthoring.MusicGroupName),
                FindGroup(mixer, AudioMixerAuthoring.EffectsGroupName),
                FindGroup(mixer, AudioMixerAuthoring.UiGroupName),
                mixer != null ? mixer.FindSnapshot(SoundSystem.DefaultSnapshotName) : null,
                mixer != null ? mixer.FindSnapshot(SoundSystem.PausedSnapshotName) : null,
                mixer != null ? mixer.FindSnapshot(SoundSystem.DuckedSnapshotName) : null,
                voices,
                music);
            EditorUtility.SetDirty(system);
        }

        private static AudioSource[] CreateSources(
            Transform parent,
            string containerName,
            string prefix,
            int count,
            bool loop)
        {
            var container = parent.Find(containerName);
            if (container == null)
            {
                container = new GameObject(containerName).transform;
                container.SetParent(parent, false);
            }

            var sources = new AudioSource[count];
            for (var index = 0; index < count; index++)
            {
                var name = count == SoundSystem.MusicSourceCount && loop
                    ? prefix + " " + (char)('A' + index)
                    : prefix + " " + index.ToString("00");
                var child = container.Find(name);
                if (child == null)
                {
                    child = new GameObject(name).transform;
                    child.SetParent(container, false);
                }

                var source = child.GetComponent<AudioSource>();
                if (source == null)
                {
                    source = child.gameObject.AddComponent<AudioSource>();
                }

                source.playOnAwake = false;
                source.loop = loop;
                source.spatialBlend = 0f;
                source.dopplerLevel = 0f;
                sources[index] = source;
            }

            return sources;
        }

        private static AudioMixerGroup FindGroup(AudioMixer mixer, string name)
        {
            if (mixer == null)
            {
                return null;
            }

            foreach (var group in mixer.FindMatchingGroups(string.Empty))
            {
                if (group.name == name)
                {
                    return group;
                }
            }

            return null;
        }

        private static string SuggestKey(string clipName)
        {
            var trimmed = clipName.Trim().TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9')
                .TrimEnd('_', '-', ' ', '.');
            return string.IsNullOrEmpty(trimmed)
                ? "custom.sound"
                : "custom." + SoundKeys.ToSnakeCase(trimmed.Replace(" ", string.Empty))
                    .Replace("__", "_");
        }

        private static bool ContainsAny(string value, params string[] parts)
        {
            return parts.Any(part => value.Contains(part));
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
