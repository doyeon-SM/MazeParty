using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>How a cue picks the next clip from its variations.</summary>
    public enum SoundVariationMode : byte
    {
        /// <summary>Any clip, repeats allowed.</summary>
        Random = 0,

        /// <summary>Any clip except the one played last (default for hits).</summary>
        RandomNoRepeat = 1,

        /// <summary>Every clip once in random order, then a new order.</summary>
        Shuffle = 2,

        /// <summary>Clips in list order, then from the start.</summary>
        Sequential = 3
    }

    /// <summary>
    /// One named sound. Code plays it by <see cref="Key"/> (see
    /// <see cref="SoundKeys"/>); designers change clips and playback here
    /// without touching code. Add more clips to get random variations, e.g.
    /// several punch hits. An empty clip list is silent, so keys can exist
    /// before their audio does.
    /// </summary>
    [CreateAssetMenu(menuName = "MazeParty/Audio/Sound Cue", fileName = "SoundCue")]
    public sealed class SoundCue : ScriptableObject
    {
        [SerializeField] private string key = string.Empty;
        [SerializeField] private AudioChannel channel = AudioChannel.Sfx;
        [SerializeField] private AudioClip[] clips = Array.Empty<AudioClip>();
        [SerializeField] private SoundVariationMode variationMode =
            SoundVariationMode.RandomNoRepeat;

        [Header("Variation")]
        [SerializeField, Range(0f, 1f)] private float volume = 1f;
        [Tooltip("Random volume reduction per play, 0.1 = up to 10% quieter.")]
        [SerializeField, Range(0f, 0.5f)] private float volumeVariance;
        [Tooltip("Random pitch change per play, 0.08 = up to ±8%.")]
        [SerializeField, Range(0f, 0.5f)] private float pitchVariance;

        [Header("3D")]
        [Tooltip("Played at a world position with distance fall-off. " +
                 "Calls without a position play it in 2D.")]
        [SerializeField] private bool spatial;
        [SerializeField, Min(0.01f)] private float minDistance = 1f;
        [SerializeField, Min(0.1f)] private float maxDistance = 30f;
        [SerializeField] private AudioRolloffMode rolloff = AudioRolloffMode.Linear;

        [Header("Playback")]
        [SerializeField] private bool loop;
        [Tooltip("0 = unlimited. At the limit the oldest instance of this cue is replaced.")]
        [SerializeField, Min(0)] private int maxInstances;
        [Tooltip("Plays closer together than this (seconds) are skipped.")]
        [SerializeField, Min(0f)] private float minInterval;
        [Tooltip("Higher wins when every voice is busy.")]
        [SerializeField, Range(0, 100)] private int priority = 50;
        [Tooltip("Lowers the music while this cue plays (mixer 'Ducked' snapshot).")]
        [SerializeField] private bool duckMusic;
        [Tooltip("Music: crossfade time. Loops: fade-out time when stopped.")]
        [SerializeField, Min(0f)] private float fadeSeconds = 1f;

        public string Key => key ?? string.Empty;
        public AudioChannel Channel => channel;
        public SoundVariationMode VariationMode => variationMode;
        public float Volume => volume;
        public float VolumeVariance => volumeVariance;
        public float PitchVariance => pitchVariance;
        public bool Spatial => spatial;
        public float MinDistance => minDistance;
        public float MaxDistance => maxDistance;
        public AudioRolloffMode Rolloff => rolloff;
        public bool Loop => loop;
        public int MaxInstances => maxInstances;
        public float MinInterval => minInterval;
        public int Priority => priority;
        public bool DuckMusic => duckMusic;
        public float FadeSeconds => fadeSeconds;
        public int ClipCount => clips != null ? clips.Length : 0;

        public bool HasClips
        {
            get
            {
                if (clips == null)
                {
                    return false;
                }

                for (var index = 0; index < clips.Length; index++)
                {
                    if (clips[index] != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public AudioClip GetClip(int index)
        {
            return clips != null && index >= 0 && index < clips.Length
                ? clips[index]
                : null;
        }

        /// <summary>Editor setup: defaults for a newly created cue.</summary>
        public void ConfigureDefaults(
            string cueKey,
            AudioChannel cueChannel,
            bool cueSpatial,
            bool cueLoop,
            float cueVolume,
            float cueVolumeVariance,
            float cuePitchVariance,
            int cueMaxInstances,
            float cueMinInterval,
            int cuePriority,
            bool cueDuckMusic,
            float cueFadeSeconds,
            float cueMaxDistance)
        {
            key = cueKey ?? string.Empty;
            channel = cueChannel;
            spatial = cueSpatial;
            loop = cueLoop;
            volume = Mathf.Clamp01(cueVolume);
            volumeVariance = Mathf.Clamp(cueVolumeVariance, 0f, 0.5f);
            pitchVariance = Mathf.Clamp(cuePitchVariance, 0f, 0.5f);
            maxInstances = Mathf.Max(0, cueMaxInstances);
            minInterval = Mathf.Max(0f, cueMinInterval);
            priority = Mathf.Clamp(cuePriority, 0, 100);
            duckMusic = cueDuckMusic;
            fadeSeconds = Mathf.Max(0f, cueFadeSeconds);
            maxDistance = Mathf.Max(0.1f, cueMaxDistance);
        }

        /// <summary>Editor tooling: replaces the clip list.</summary>
        public void SetClips(AudioClip[] value)
        {
            clips = value ?? Array.Empty<AudioClip>();
        }
    }
}
