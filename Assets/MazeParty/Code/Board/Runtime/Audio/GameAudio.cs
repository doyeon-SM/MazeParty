using System;
using UnityEngine;
using UnityEngine.Audio;

namespace MazeParty.Gameplay
{
    /// <summary>
    /// Output channel of a sound. UI sounds have their own mixer group but
    /// follow the effects volume slider.
    /// </summary>
    public enum AudioChannel : byte
    {
        Sfx = 0,
        Bgm = 1,
        Ui = 2
    }

    /// <summary>Supplies the mixer group each channel plays through.</summary>
    public interface IGameAudioRouting
    {
        AudioMixerGroup GetOutputGroup(AudioChannel channel);
    }

    /// <summary>
    /// Local volume state from the settings menu, normalized to 0..1. The
    /// sound system applies it to the AudioMixer's exposed volumes and
    /// registers itself as <see cref="Routing"/>. Without a mixer (no sound
    /// system in the session) the master volume falls back to
    /// <see cref="AudioListener.volume"/> and <see cref="AudioChannelSource"/>
    /// scales its source directly.
    /// </summary>
    public static class GameAudio
    {
        public const float DefaultVolume = 1f;
        public const float MinDecibels = -80f;

        public static float MasterVolume { get; private set; } = DefaultVolume;
        public static float SfxVolume { get; private set; } = DefaultVolume;
        public static float BgmVolume { get; private set; } = DefaultVolume;
        public static IGameAudioRouting Routing { get; private set; }

        public static event Action VolumesChanged;
        public static event Action RoutingChanged;

        /// <summary>Slider volume of a channel. UI follows the effects slider.</summary>
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
            ApplyListenerFallback();
            if (changed)
            {
                VolumesChanged?.Invoke();
            }
        }

        /// <summary>
        /// Registers the mixer routing (the sound system) or clears it. While a
        /// routing is registered the master volume lives in the mixer.
        /// </summary>
        public static void SetRouting(IGameAudioRouting routing)
        {
            if (ReferenceEquals(Routing, routing))
            {
                return;
            }

            Routing = routing;
            ApplyListenerFallback();
            RoutingChanged?.Invoke();
        }

        public static AudioMixerGroup GetOutputGroup(AudioChannel channel)
        {
            return Routing?.GetOutputGroup(channel);
        }

        public static float Sanitize(float volume)
        {
            return float.IsNaN(volume) || float.IsInfinity(volume)
                ? DefaultVolume
                : Mathf.Clamp01(volume);
        }

        /// <summary>
        /// Slider value (0..1, linear) to mixer attenuation in decibels:
        /// 1 → 0 dB, 0.5 → about -6 dB, 0 → <see cref="MinDecibels"/> (silent).
        /// </summary>
        public static float ToDecibels(float volume)
        {
            volume = Sanitize(volume);
            return volume <= 0.0001f
                ? MinDecibels
                : Mathf.Max(MinDecibels, 20f * Mathf.Log10(volume));
        }

        private static void ApplyListenerFallback()
        {
            AudioListener.volume = Routing != null ? 1f : MasterVolume;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            MasterVolume = DefaultVolume;
            SfxVolume = DefaultVolume;
            BgmVolume = DefaultVolume;
            Routing = null;
            VolumesChanged = null;
            RoutingChanged = null;
        }
    }
}
