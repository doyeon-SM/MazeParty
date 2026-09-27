using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using Random = System.Random;

namespace MazeParty.Gameplay
{
    /// <summary>Identifies one playing sound so a loop can be stopped later.</summary>
    public readonly struct SoundHandle : IEquatable<SoundHandle>
    {
        internal SoundHandle(int voice, int generation)
        {
            Voice = voice;
            Generation = generation;
        }

        internal int Voice { get; }
        internal int Generation { get; }
        public bool IsValid => Generation > 0;

        public bool Equals(SoundHandle other)
        {
            return Voice == other.Voice && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is SoundHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Voice * 397) ^ Generation;
        }
    }

    /// <summary>Where and how loud one play request is.</summary>
    public readonly struct SoundPlayRequest
    {
        private SoundPlayRequest(
            bool hasPosition,
            Vector3 position,
            Transform follow,
            float volumeScale,
            float maxDistance)
        {
            HasPosition = hasPosition;
            Position = position;
            Follow = follow;
            VolumeScale = volumeScale;
            MaxDistance = maxDistance;
        }

        public bool HasPosition { get; }
        public Vector3 Position { get; }
        public Transform Follow { get; }
        public float VolumeScale { get; }
        public float MaxDistance { get; }

        public static SoundPlayRequest TwoD(float volumeScale = 1f)
        {
            return new SoundPlayRequest(false, Vector3.zero, null, volumeScale, 0f);
        }

        public static SoundPlayRequest At(
            Vector3 position,
            float volumeScale = 1f,
            float maxDistance = 0f)
        {
            return new SoundPlayRequest(true, position, null, volumeScale, maxDistance);
        }

        public static SoundPlayRequest Attached(Transform target, float volumeScale = 1f)
        {
            return target != null
                ? new SoundPlayRequest(true, target.position, target, volumeScale, 0f)
                : TwoD(volumeScale);
        }
    }

    /// <summary>
    /// Plays every game sound: a pool of prefab-authored voices for effects
    /// and UI, two music sources for crossfades, and the AudioMixer (settings
    /// volumes on exposed parameters, Default / Paused / Ducked snapshots).
    /// One instance is spawned from Resources before the first scene loads
    /// and lives for the whole session; use <see cref="GameSound"/> to play.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-900)]
    public sealed class SoundSystem : MonoBehaviour, IGameAudioRouting
    {
        public const string ResourcePath = "MazeParty/Audio/SoundSystem";
        public const string MasterVolumeParameter = "MasterVolume";
        public const string BgmVolumeParameter = "BgmVolume";
        public const string SfxVolumeParameter = "SfxVolume";
        public const string DefaultSnapshotName = "Default";
        public const string PausedSnapshotName = "Paused";
        public const string DuckedSnapshotName = "Ducked";
        public const float SnapshotTransitionSeconds = 0.35f;
        public const float DefaultMusicFadeSeconds = 1f;
        public const int MinimumVoiceCount = 16;
        public const int MusicSourceCount = 2;

        [SerializeField] private SoundLibrary library;
        [SerializeField] private AudioMixer mixer;
        [SerializeField] private AudioMixerGroup musicGroup;
        [SerializeField] private AudioMixerGroup effectsGroup;
        [SerializeField] private AudioMixerGroup uiGroup;
        [SerializeField] private AudioMixerSnapshot defaultSnapshot;
        [SerializeField] private AudioMixerSnapshot pausedSnapshot;
        [SerializeField] private AudioMixerSnapshot duckedSnapshot;
        [SerializeField] private AudioSource[] voices = Array.Empty<AudioSource>();
        [SerializeField] private AudioSource[] musicSources = Array.Empty<AudioSource>();
        [Tooltip("Logs every played key. Useful while clips are still missing.")]
        [SerializeField] private bool logPlays;

        private readonly Random _random = new Random();
        private readonly Dictionary<SoundCue, CueState> _cueStates =
            new Dictionary<SoundCue, CueState>();
        private readonly HashSet<string> _warnedKeys =
            new HashSet<string>(StringComparer.Ordinal);

        private Voice[] _voices = Array.Empty<Voice>();
        private SoundVoiceState[] _voiceStates = Array.Empty<SoundVoiceState>();
        private MusicChannel[] _music = Array.Empty<MusicChannel>();
        private int _activeMusic = -1;
        private SoundCue _musicCue;
        private string _musicKey;
        private bool _simulationPaused;
        private float _duckUntil;
        private AudioMixerSnapshot _currentSnapshot;
        private int _nextCueId = 1;
        // Start order of voices; plays in the same frame share a timestamp.
        private long _playSequence;

        public static SoundSystem Instance { get; private set; }

        public SoundLibrary Library => library;
        public AudioMixer Mixer => mixer;
        public AudioMixerGroup MusicGroup => musicGroup;
        public AudioMixerGroup EffectsGroup => effectsGroup;
        public AudioMixerGroup UiGroup => uiGroup;
        public AudioMixerSnapshot DefaultSnapshot => defaultSnapshot;
        public AudioMixerSnapshot PausedSnapshot => pausedSnapshot;
        public AudioMixerSnapshot DuckedSnapshot => duckedSnapshot;
        public IReadOnlyList<AudioSource> Voices => voices ?? Array.Empty<AudioSource>();
        public IReadOnlyList<AudioSource> MusicSources => musicSources ?? Array.Empty<AudioSource>();
        public string CurrentMusicKey => _musicKey;

        public bool HasRequiredReferences =>
            library != null &&
            mixer != null &&
            musicGroup != null &&
            effectsGroup != null &&
            uiGroup != null &&
            defaultSnapshot != null &&
            pausedSnapshot != null &&
            duckedSnapshot != null &&
            voices != null &&
            voices.Length >= MinimumVoiceCount &&
            AllAssigned(voices) &&
            musicSources != null &&
            musicSources.Length == MusicSourceCount &&
            AllAssigned(musicSources);

        /// <summary>Editor setup: binds the authored mixer, library and sources.</summary>
        public void Configure(
            SoundLibrary soundLibrary,
            AudioMixer audioMixer,
            AudioMixerGroup music,
            AudioMixerGroup effects,
            AudioMixerGroup ui,
            AudioMixerSnapshot normal,
            AudioMixerSnapshot paused,
            AudioMixerSnapshot ducked,
            AudioSource[] voiceSources,
            AudioSource[] musicSourcePair)
        {
            library = soundLibrary;
            mixer = audioMixer;
            musicGroup = music;
            effectsGroup = effects;
            uiGroup = ui;
            defaultSnapshot = normal;
            pausedSnapshot = paused;
            duckedSnapshot = ducked;
            voices = voiceSources ?? Array.Empty<AudioSource>();
            musicSources = musicSourcePair ?? Array.Empty<AudioSource>();
        }

        public AudioMixerGroup GetOutputGroup(AudioChannel channel)
        {
            switch (channel)
            {
                case AudioChannel.Bgm:
                    return musicGroup;
                case AudioChannel.Ui:
                    return uiGroup;
                default:
                    return effectsGroup;
            }
        }

        public bool HasClips(string key)
        {
            return library != null &&
                   library.TryGet(key, out var cue) &&
                   cue.HasClips;
        }

        public SoundHandle Play(string key, SoundPlayRequest request)
        {
            if (!TryResolve(key, out var cue))
            {
                return default;
            }

            var state = GetState(cue);
            var now = Time.unscaledTimeAsDouble;
            if (!SoundVoiceRules.PassesInterval(now, state.LastStartedAt, cue.MinInterval))
            {
                return default;
            }

            var clip = PickClip(cue, state);
            if (clip == null)
            {
                LogPlay(key, null, -1);
                return default;
            }

            RefreshVoiceStates();
            var index = SoundVoiceRules.ChooseVoice(
                _voiceStates,
                state.Id,
                cue.MaxInstances,
                cue.Priority);
            if (index < 0)
            {
                LogPlay(key, clip, -1);
                return default;
            }

            var voice = _voices[index];
            StartVoice(voice, cue, state.Id, clip, request);
            state.LastStartedAt = now;
            if (cue.DuckMusic)
            {
                _duckUntil = Mathf.Max(
                    _duckUntil,
                    Time.unscaledTime + clip.length / Mathf.Max(0.01f, voice.Source.pitch));
            }

            LogPlay(key, clip, index);
            return new SoundHandle(index, voice.Generation);
        }

        /// <summary>
        /// Plays a cue through a caller-owned AudioSource (a scene-authored
        /// source that must stay where it is). Clip choice, volume, pitch and
        /// mixer routing still come from the cue.
        /// </summary>
        public bool PlayOn(string key, AudioSource source, float volumeScale)
        {
            if (source == null || !TryResolve(key, out var cue))
            {
                return false;
            }

            var state = GetState(cue);
            var now = Time.unscaledTimeAsDouble;
            if (!SoundVoiceRules.PassesInterval(now, state.LastStartedAt, cue.MinInterval))
            {
                return false;
            }

            var clip = PickClip(cue, state);
            LogPlay(key, clip, -1);
            if (clip == null)
            {
                return false;
            }

            source.outputAudioMixerGroup = GetOutputGroup(cue.Channel);
            source.pitch = RandomPitch(cue);
            source.PlayOneShot(clip, RandomVolume(cue) * Mathf.Max(0f, volumeScale));
            state.LastStartedAt = now;
            return true;
        }

        public void Stop(SoundHandle handle, float fadeSeconds)
        {
            if (!handle.IsValid ||
                handle.Voice < 0 ||
                handle.Voice >= _voices.Length)
            {
                return;
            }

            var voice = _voices[handle.Voice];
            if (!voice.Busy || voice.Generation != handle.Generation)
            {
                return;
            }

            if (fadeSeconds <= 0f)
            {
                ReleaseVoice(voice);
                return;
            }

            voice.FadeFrom = voice.Source.volume;
            voice.FadeStartedAt = Time.unscaledTimeAsDouble;
            voice.FadeSeconds = fadeSeconds;
        }

        /// <summary>
        /// Crossfades to the music of <paramref name="key"/>; null or an empty
        /// cue fades the music out. Playing the current key again does nothing.
        /// </summary>
        public void PlayBgm(string key)
        {
            if (string.Equals(key, _musicKey, StringComparison.Ordinal))
            {
                return;
            }

            if (_music.Length < MusicSourceCount)
            {
                _musicKey = key;
                return;
            }

            var fade = _musicCue != null ? _musicCue.FadeSeconds : DefaultMusicFadeSeconds;
            SoundCue cue = null;
            AudioClip clip = null;
            if (!string.IsNullOrEmpty(key) && TryResolve(key, out cue))
            {
                clip = PickClip(cue, GetState(cue));
                fade = cue.FadeSeconds;
            }

            _musicKey = key;
            LogPlay(key ?? "(music off)", clip, -1);
            if (_activeMusic >= 0)
            {
                FadeMusic(_activeMusic, 0f, fade, true);
            }

            if (clip == null)
            {
                _activeMusic = -1;
                _musicCue = null;
                return;
            }

            _activeMusic = _activeMusic == 0 ? 1 : 0;
            _musicCue = cue;
            StartMusicClip(cue, clip, fade);
        }

        public void StopBgm()
        {
            PlayBgm(null);
        }

        /// <summary>Player pause or reconnect wait: the mixer's Paused snapshot.</summary>
        public void SetSimulationPaused(bool paused)
        {
            _simulationPaused = paused;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            Instance = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void SpawnFromResources()
        {
            if (Instance != null)
            {
                return;
            }

            var prefab = Resources.Load<SoundSystem>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning(
                    "SoundSystem prefab is missing (Resources/" + ResourcePath +
                    "). Run MazeParty/Audio/Install Sound System.");
                return;
            }

            var instance = Instantiate(prefab);
            instance.name = prefab.name;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);
            BuildRuntime();
            if (musicGroup != null && effectsGroup != null && uiGroup != null)
            {
                GameAudio.SetRouting(this);
            }
        }

        private void OnEnable()
        {
            GameAudio.VolumesChanged += ApplyVolumes;
        }

        private void OnDisable()
        {
            GameAudio.VolumesChanged -= ApplyVolumes;
        }

        private void Start()
        {
            // AudioMixer ignores SetFloat before the first frame; apply here.
            ApplyVolumes();
            ApplySnapshot(true);
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Instance = null;
            if (ReferenceEquals(GameAudio.Routing, this))
            {
                GameAudio.SetRouting(null);
            }
        }

        private void Update()
        {
            var now = Time.unscaledTimeAsDouble;
            for (var index = 0; index < _voices.Length; index++)
            {
                UpdateVoice(_voices[index], now);
            }

            for (var index = 0; index < _music.Length; index++)
            {
                UpdateMusic(_music[index], now);
            }

            if (_activeMusic >= 0 &&
                _musicCue != null &&
                !_musicCue.Loop &&
                _music[_activeMusic].Source != null &&
                !_music[_activeMusic].Source.isPlaying)
            {
                // Non-looping music cues play their clips as a playlist.
                StartMusicClip(_musicCue, PickClip(_musicCue, GetState(_musicCue)), 0f);
            }

            ApplySnapshot(false);
        }

        private void BuildRuntime()
        {
            var voiceSources = voices ?? Array.Empty<AudioSource>();
            _voices = new Voice[voiceSources.Length];
            _voiceStates = new SoundVoiceState[voiceSources.Length];
            for (var index = 0; index < voiceSources.Length; index++)
            {
                var source = voiceSources[index];
                if (source != null)
                {
                    source.playOnAwake = false;
                    source.Stop();
                }

                _voices[index] = new Voice(source);
            }

            var music = musicSources ?? Array.Empty<AudioSource>();
            _music = new MusicChannel[music.Length];
            for (var index = 0; index < music.Length; index++)
            {
                var source = music[index];
                if (source != null)
                {
                    source.playOnAwake = false;
                    source.spatialBlend = 0f;
                    source.outputAudioMixerGroup = musicGroup;
                    source.Stop();
                }

                _music[index] = new MusicChannel(source);
            }
        }

        private bool TryResolve(string key, out SoundCue cue)
        {
            cue = null;
            if (string.IsNullOrEmpty(key) || library == null)
            {
                return false;
            }

            if (library.TryGet(key, out cue))
            {
                return true;
            }

            if (_warnedKeys.Add(key))
            {
                Debug.LogWarning(
                    "Sound key '" + key + "' is not in the sound library. " +
                    "Run MazeParty/Audio/Install Sound System or add a SoundCue.",
                    library);
            }

            return false;
        }

        private CueState GetState(SoundCue cue)
        {
            if (!_cueStates.TryGetValue(cue, out var state))
            {
                state = new CueState(_nextCueId++);
                _cueStates.Add(cue, state);
            }

            return state;
        }

        private AudioClip PickClip(SoundCue cue, CueState state)
        {
            if (cue == null || !cue.HasClips)
            {
                return null;
            }

            // Null entries in the clip list are skipped.
            for (var attempt = 0; attempt < cue.ClipCount; attempt++)
            {
                var clip = cue.GetClip(
                    state.Picker.Next(cue.ClipCount, cue.VariationMode, _random));
                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }

        private float RandomVolume(SoundCue cue)
        {
            return cue.Volume * (1f - (float)_random.NextDouble() * cue.VolumeVariance);
        }

        private float RandomPitch(SoundCue cue)
        {
            return 1f + ((float)_random.NextDouble() * 2f - 1f) * cue.PitchVariance;
        }

        private void RefreshVoiceStates()
        {
            for (var index = 0; index < _voices.Length; index++)
            {
                var voice = _voices[index];
                _voiceStates[index] = voice.Source == null
                    // A missing source is never chosen: busy, unstealable.
                    ? new SoundVoiceState(true, 0, int.MaxValue, 0d, true)
                    : new SoundVoiceState(
                        voice.Busy,
                        voice.CueId,
                        voice.Priority,
                        voice.StartedAt,
                        voice.Loop);
            }
        }

        private void StartVoice(
            Voice voice,
            SoundCue cue,
            int cueId,
            AudioClip clip,
            SoundPlayRequest request)
        {
            var source = voice.Source;
            source.Stop();
            voice.Generation++;
            voice.Busy = true;
            voice.CueId = cueId;
            voice.Priority = cue.Priority;
            voice.StartedAt = ++_playSequence;
            voice.Loop = cue.Loop;
            voice.Follow = request.Follow;
            voice.FadeSeconds = 0f;

            var spatial = cue.Spatial && request.HasPosition;
            source.clip = clip;
            source.loop = cue.Loop;
            source.outputAudioMixerGroup = GetOutputGroup(cue.Channel);
            source.spatialBlend = spatial ? 1f : 0f;
            source.minDistance = cue.MinDistance;
            source.maxDistance = request.MaxDistance > 0f
                ? request.MaxDistance
                : cue.MaxDistance;
            source.rolloffMode = cue.Rolloff;
            source.dopplerLevel = 0f;
            source.priority = 256 - Mathf.RoundToInt(cue.Priority * 2.56f);
            source.volume = RandomVolume(cue) * Mathf.Max(0f, request.VolumeScale);
            source.pitch = RandomPitch(cue);
            source.transform.position = request.HasPosition
                ? request.Position
                : transform.position;
            source.Play();
        }

        private void UpdateVoice(Voice voice, double now)
        {
            if (!voice.Busy || voice.Source == null)
            {
                return;
            }

            if (voice.Follow != null)
            {
                voice.Source.transform.position = voice.Follow.position;
            }

            if (voice.FadeSeconds > 0f)
            {
                var t = (float)((now - voice.FadeStartedAt) / voice.FadeSeconds);
                if (t >= 1f)
                {
                    ReleaseVoice(voice);
                    return;
                }

                voice.Source.volume = Mathf.Lerp(voice.FadeFrom, 0f, t);
            }

            if (!voice.Source.isPlaying)
            {
                ReleaseVoice(voice);
            }
        }

        private static void ReleaseVoice(Voice voice)
        {
            if (voice.Source != null)
            {
                voice.Source.Stop();
                voice.Source.clip = null;
            }

            voice.Busy = false;
            voice.Follow = null;
            voice.FadeSeconds = 0f;
        }

        private void StartMusicClip(SoundCue cue, AudioClip clip, float fade)
        {
            if (clip == null || _activeMusic < 0 || _activeMusic >= _music.Length)
            {
                return;
            }

            var channel = _music[_activeMusic];
            var source = channel.Source;
            if (source == null)
            {
                return;
            }

            source.Stop();
            source.clip = clip;
            source.loop = cue.Loop;
            source.pitch = 1f;
            source.outputAudioMixerGroup = musicGroup;
            source.volume = fade > 0f ? 0f : cue.Volume;
            source.Play();
            FadeMusic(_activeMusic, cue.Volume, fade, false);
        }

        private void FadeMusic(int index, float target, float seconds, bool stopWhenSilent)
        {
            var channel = _music[index];
            if (channel.Source == null)
            {
                return;
            }

            channel.FadeFrom = channel.Source.volume;
            channel.FadeTo = target;
            channel.FadeStartedAt = Time.unscaledTimeAsDouble;
            channel.FadeSeconds = Mathf.Max(0f, seconds);
            channel.StopWhenSilent = stopWhenSilent;
            if (channel.FadeSeconds <= 0f)
            {
                channel.Source.volume = target;
                if (stopWhenSilent)
                {
                    channel.Source.Stop();
                }
            }
        }

        private static void UpdateMusic(MusicChannel channel, double now)
        {
            if (channel.Source == null || channel.FadeSeconds <= 0f)
            {
                return;
            }

            var t = Mathf.Clamp01((float)((now - channel.FadeStartedAt) / channel.FadeSeconds));
            channel.Source.volume = Mathf.Lerp(channel.FadeFrom, channel.FadeTo, t);
            if (t < 1f)
            {
                return;
            }

            channel.FadeSeconds = 0f;
            if (channel.StopWhenSilent)
            {
                channel.Source.Stop();
                channel.Source.clip = null;
            }
        }

        private void ApplyVolumes()
        {
            if (mixer == null)
            {
                return;
            }

            mixer.SetFloat(MasterVolumeParameter, GameAudio.ToDecibels(GameAudio.MasterVolume));
            mixer.SetFloat(BgmVolumeParameter, GameAudio.ToDecibels(GameAudio.BgmVolume));
            mixer.SetFloat(SfxVolumeParameter, GameAudio.ToDecibels(GameAudio.SfxVolume));
        }

        private void ApplySnapshot(bool force)
        {
            var desired = _simulationPaused
                ? pausedSnapshot
                : Time.unscaledTime < _duckUntil
                    ? duckedSnapshot
                    : defaultSnapshot;
            if (desired == null || (!force && desired == _currentSnapshot))
            {
                return;
            }

            desired.TransitionTo(force ? 0f : SnapshotTransitionSeconds);
            _currentSnapshot = desired;
        }

        private void LogPlay(string key, AudioClip clip, int voice)
        {
            if (!logPlays)
            {
                return;
            }

            Debug.Log("[Sound] " + key + " → " +
                      (clip != null ? clip.name : "(no clip)") +
                      (voice >= 0 ? " (voice " + voice + ")" : string.Empty));
        }

        private static bool AllAssigned(AudioSource[] sources)
        {
            for (var index = 0; index < sources.Length; index++)
            {
                if (sources[index] == null)
                {
                    return false;
                }
            }

            return true;
        }

        private sealed class CueState
        {
            public CueState(int id)
            {
                Id = id;
            }

            public int Id { get; }
            public SoundVariationPicker Picker { get; } = new SoundVariationPicker();
            public double LastStartedAt { get; set; } = -1d;
        }

        private sealed class Voice
        {
            public Voice(AudioSource source)
            {
                Source = source;
            }

            public AudioSource Source { get; }
            public int Generation { get; set; }
            public bool Busy { get; set; }
            public int CueId { get; set; }
            public int Priority { get; set; }
            public double StartedAt { get; set; }
            public bool Loop { get; set; }
            public Transform Follow { get; set; }
            public float FadeFrom { get; set; }
            public double FadeStartedAt { get; set; }
            public float FadeSeconds { get; set; }
        }

        private sealed class MusicChannel
        {
            public MusicChannel(AudioSource source)
            {
                Source = source;
            }

            public AudioSource Source { get; }
            public float FadeFrom { get; set; }
            public float FadeTo { get; set; }
            public double FadeStartedAt { get; set; }
            public float FadeSeconds { get; set; }
            public bool StopWhenSilent { get; set; }
        }
    }
}
