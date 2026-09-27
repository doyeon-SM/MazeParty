using UnityEngine;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Entry point for playing sounds by key (<see cref="SoundKeys"/>).
    /// Everything is local presentation: call it from code that already runs
    /// on every client (view updates, ClientsAndHost presentation RPCs).
    /// Unknown keys warn once; keys whose cue has no clips yet are silent.
    /// Calls outside Play Mode or without a sound system do nothing.
    /// </summary>
    public static class GameSound
    {
        /// <summary>2D sound (UI, announcements, local feedback).</summary>
        public static SoundHandle Play(string key, float volumeScale = 1f)
        {
            var system = ActiveSystem;
            return system != null
                ? system.Play(key, SoundPlayRequest.TwoD(volumeScale))
                : default;
        }

        /// <summary>
        /// Sound at a world position; spatial cues fade with distance.
        /// <paramref name="maxDistance"/> above 0 overrides the cue's range.
        /// </summary>
        public static SoundHandle PlayAt(
            string key,
            Vector3 position,
            float volumeScale = 1f,
            float maxDistance = 0f)
        {
            var system = ActiveSystem;
            return system != null
                ? system.Play(key, SoundPlayRequest.At(position, volumeScale, maxDistance))
                : default;
        }

        /// <summary>Sound that follows a transform (loops on moving objects).</summary>
        public static SoundHandle PlayAttached(
            string key,
            Transform target,
            float volumeScale = 1f)
        {
            var system = ActiveSystem;
            return system != null
                ? system.Play(key, SoundPlayRequest.Attached(target, volumeScale))
                : default;
        }

        /// <summary>Stops a sound started by this API, e.g. a loop.</summary>
        public static void Stop(SoundHandle handle, float fadeSeconds = 0.1f)
        {
            ActiveSystem?.Stop(handle, fadeSeconds);
        }

        /// <summary>
        /// Plays a cue through a scene-authored AudioSource that must stay in
        /// place. Returns false when nothing played.
        /// </summary>
        public static bool PlayOn(string key, AudioSource source, float volumeScale = 1f)
        {
            var system = ActiveSystem;
            return system != null && system.PlayOn(key, source, volumeScale);
        }

        /// <summary>Crossfades to a music key; null fades the music out.</summary>
        public static void PlayBgm(string key)
        {
            ActiveSystem?.PlayBgm(key);
        }

        public static void StopBgm()
        {
            ActiveSystem?.StopBgm();
        }

        public static string CurrentBgmKey => ActiveSystem?.CurrentMusicKey;

        /// <summary>True when the key's cue has at least one clip.</summary>
        public static bool HasClips(string key)
        {
            var system = ActiveSystem;
            return system != null && system.HasClips(key);
        }

        /// <summary>Game paused (player pause, reconnect wait): mixer Paused snapshot.</summary>
        public static void SetSimulationPaused(bool paused)
        {
            ActiveSystem?.SetSimulationPaused(paused);
        }

        private static SoundSystem ActiveSystem
        {
            get
            {
                if (!Application.isPlaying)
                {
                    return null;
                }

                // Unity's null check, so ?. never reaches a destroyed instance.
                var instance = SoundSystem.Instance;
                return instance != null ? instance : null;
            }
        }
    }
}
