using System;
using UnityEngine;

namespace MazeParty.Gameplay
{
    public enum AudioChannel : byte
    {
        Sfx = 0,
        Bgm = 1
    }

    /// <summary>
    /// Local volume state. Master volume drives <see cref="AudioListener.volume"/>;
    /// channel volumes are applied by <see cref="AudioChannelSource"/> on each
    /// AudioSource (or multiplied into one-shot volumes by code that plays clips).
    /// Values are normalized to 0..1.
    /// </summary>
    public static class GameAudio
    {
        public const float DefaultVolume = 1f;

        public static float MasterVolume { get; private set; } = DefaultVolume;
        public static float SfxVolume { get; private set; } = DefaultVolume;
        public static float BgmVolume { get; private set; } = DefaultVolume;

        public static event Action VolumesChanged;

        public static float GetChannelVolume(AudioChannel channel)
        {
            return channel == AudioChannel.Bgm ? BgmVolume : SfxVolume;
        }

        public static void SetVolumes(float master, float sfx, float bgm)
        {
            master = Sanitize(master);
            sfx = Sanitize(sfx);
            bgm = Sanitize(bgm);
            var changed =
                !Mathf.Approximately(master, MasterVolume) ||
                !Mathf.Approximately(sfx, SfxVolume) ||
                !Mathf.Approximately(bgm, BgmVolume);

            MasterVolume = master;
            SfxVolume = sfx;
            BgmVolume = bgm;
            AudioListener.volume = master;
            if (changed)
            {
                VolumesChanged?.Invoke();
            }
        }

        public static float Sanitize(float volume)
        {
            return float.IsNaN(volume) || float.IsInfinity(volume)
                ? DefaultVolume
                : Mathf.Clamp01(volume);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            MasterVolume = DefaultVolume;
            SfxVolume = DefaultVolume;
            BgmVolume = DefaultVolume;
            VolumesChanged = null;
        }
    }
}
